using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Services.TopCommittee;
public static class TopCommitteeNotificationEvents
{
 private static async Task AddAsync(ApplicationDbContext db,int tenderId,IEnumerable<string> recipients,string title,CancellationToken ct)
 {
  await TopCommitteeTenderTransaction.RequireLockAsync(db,tenderId,ct);var url=$"/TopCommittee/TenderDetails/{tenderId}";
  foreach(var recipient in recipients.Distinct())
   if(!db.Notifications.Local.Any(n=>n.UserId==recipient&&n.Title==title&&n.RelativeUrl==url)&&!await db.Notifications.AnyAsync(n=>n.UserId==recipient&&n.Title==title&&n.RelativeUrl==url,ct))db.Notifications.Add(new(){UserId=recipient,Title=title,Message="Open the tender to review its current committee status.",RelativeUrl=url,IsRead=false,CreatedAt=DateTime.UtcNow});
 }
 internal static async Task DecisionAsync(ApplicationDbContext db,CommitteeVotingContext context,CancellationToken ct)
 {
  if (context.Access.Tender!.Status == "AwaitingManagerReview")
  {
   var isEditRequest = context.Access.Tender.TopEditState == "WithManager";
   foreach (var manager in context.Access.Managers.Where(m => m.IsActive && m.HasManagerRole))
    db.Notifications.Add(new Notification { UserId = manager.UserId,
     Title = isEditRequest ? "Top Committee requested edits on tender" : "Top Committee rejected tender",
     Message = isEditRequest
      ? $"CEO & Chiefs requested edits/clarification on Tender #{context.Access.Tender.TenderId}. Answer directly or push back to the Sourcing Officer."
      : $"Tender #{context.Access.Tender.TenderId} was completely rejected. Review the reason and forward to its officer.",
     RelativeUrl = $"/SourcingManager/Submissions?rejectedTenderId={context.Access.Tender.TenderId}&showRejections=true", CreatedAt = DateTime.UtcNow });
  }
  var tender=context.Access.Tender!;if(tender.Status is "Awarded" or "AwaitingManagerReview")await AddAsync(db,tender.TenderId,context.Access.Members.Where(m=>m.IsEligible).Select(m=>m.UserId),$"Tender #{tender.TenderId}: {(tender.Status=="Awarded"?"awarded":tender.TopEditState=="WithManager"?"returned to Manager for edits":"rejected and returned to Manager")}",ct);
  if (tender.Status == "Awarded" && tender.AwardedBidId.HasValue)
  {
   var offers = await db.Bids.Where(b => b.TenderId == tender.TenderId && b.SubmittedAt.HasValue && b.Status != "Draft" && b.Status != "Withdrawn")
    .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile).ToListAsync(ct);
   foreach (var offer in offers)
   {
    var won = offer.BidId == tender.AwardedBidId;
    offer.Status = won ? "Accepted" : "Rejected";
    var recipient = offer.Invitation.SupplierProfile.UserId;
    var url = $"/Supplier/Offer/{offer.BidId}";
    var title = won ? "Your offer was selected" : "Your offer was not selected";
    if (!db.Notifications.Local.Any(n => n.UserId == recipient && n.RelativeUrl == url && n.Title == title)
     && !await db.Notifications.AnyAsync(n => n.UserId == recipient && n.RelativeUrl == url && n.Title == title, ct))
     db.Notifications.Add(new Notification { UserId = recipient, Title = title, RelativeUrl = url,
      Message = won ? $"Your offer #{offer.BidId} for {tender.Title} won the tender." : $"The tender {tender.Title} has been awarded. Your offer #{offer.BidId} was not selected.",
      IsRead = false, CreatedAt = DateTime.UtcNow });
   }
   // Reuse the locked award context and the caller's SaveChanges/commit. Never broadcast by role.
   var request = await db.PurchaseRequests.AsNoTracking()
       .SingleOrDefaultAsync(r => r.PurchaseRequestId == tender.PurchaseRequestId, ct);
   var candidates = RelatedAwardCandidates(tender, context.Access.Managers, request);
   var ids = candidates.Select(c => c.UserId).Distinct().ToArray();
   var accounts = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
       .Select(u => new AwardRecipientAccount(u.Id, u.IsActive, u.DepartmentId)).ToListAsync(ct);
   var roles = await (from userRole in db.UserRoles.AsNoTracking()
       join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
       where ids.Contains(userRole.UserId)
       select new { userRole.UserId, Role = role.Name! }).ToListAsync(ct);
   var existingRecipients = context.Access.Members.Where(m => m.IsEligible).Select(m => m.UserId)
       .Concat(offers.Select(b => b.Invitation.SupplierProfile.UserId)).ToHashSet(StringComparer.Ordinal);
   var notices = RelatedAwardNotices(tender, candidates, accounts,
       roles.Select(r => (r.UserId, r.Role)), existingRecipients);
   foreach (var notice in notices)
       if (!db.Notifications.Local.Any(n => SameNotice(n, notice)) &&
           !await db.Notifications.AnyAsync(n => n.UserId == notice.UserId && n.Title == notice.Title && n.RelativeUrl == notice.RelativeUrl, ct))
           db.Notifications.Add(notice);
  }
 }
 internal sealed record AwardRecipientCandidate(string UserId, string Role, string Url, string Message, int? DepartmentId = null);
 internal sealed record AwardRecipientAccount(string UserId, bool IsActive, int? DepartmentId);

 internal static IReadOnlyList<AwardRecipientCandidate> RelatedAwardCandidates(Tender tender,
     IReadOnlyList<Models.TopCommittee.CommitteeManager> managers, PurchaseRequest? request)
 {
  var recipients = new List<AwardRecipientCandidate>();
  if (tender.Status != "Awarded" || !tender.AwardedBidId.HasValue || !tender.AwardedAt.HasValue) return recipients;
  if (managers.Count == 1 && managers[0].IsActive && managers[0].HasManagerRole)
      recipients.Add(new(managers[0].UserId, Data.RoleNames.SourcingManager, "/SourcingManager/ChosenOffers",
          $"Tender #{tender.TenderId} has been awarded. Review the award outcome in Chosen Offers."));
  if (!string.IsNullOrWhiteSpace(tender.CreatedByUserId))
      recipients.Add(new(tender.CreatedByUserId, Data.RoleNames.SourcingOfficer, $"/SourcingOfficer/Tenders?id={tender.TenderId}",
          $"Tender #{tender.TenderId} has been awarded. Review the selected offer in your tender workspace."));
  if (request != null && request.PurchaseRequestId == tender.PurchaseRequestId && !string.IsNullOrWhiteSpace(request.RequestedByUserId))
      recipients.Add(new(request.RequestedByUserId, Data.RoleNames.Department, $"/Department/Details/{request.PurchaseRequestId}",
          $"Tender #{tender.TenderId} for your purchase request #{request.PurchaseRequestId} has been awarded. View your request for reference.", request.DepartmentId));
  return recipients;
 }

 internal static IReadOnlyList<Notification> RelatedAwardNotices(Tender tender,
     IEnumerable<AwardRecipientCandidate> candidates, IEnumerable<AwardRecipientAccount> accounts,
     IEnumerable<(string UserId, string Role)> roles, IEnumerable<string> existingRecipients)
 {
  var result = new List<Notification>();
  if (tender.Status != "Awarded" || !tender.AwardedBidId.HasValue || !tender.AwardedAt.HasValue) return result;
  var seen = existingRecipients.ToHashSet(StringComparer.Ordinal);
  var active = accounts.Where(a => a.IsActive).ToDictionary(a => a.UserId, StringComparer.Ordinal);
  var authorized = roles.ToHashSet();
  foreach (var candidate in candidates)
  {
   if (!active.TryGetValue(candidate.UserId, out var account) || !authorized.Contains((candidate.UserId,candidate.Role)) ||
       candidate.DepartmentId.HasValue && account.DepartmentId != candidate.DepartmentId) continue;
   if (seen.Add(candidate.UserId)) result.Add(new Notification { UserId=candidate.UserId,
       Title=$"Tender #{tender.TenderId}: awarded", Message=candidate.Message, RelativeUrl=candidate.Url,
       IsRead=false, CreatedAt=DateTime.UtcNow });
  }
  return result;
 }
 internal static bool SameNotice(Notification left, Notification right) =>
     left.UserId == right.UserId && left.Title == right.Title && left.RelativeUrl == right.RelativeUrl;
 internal static async Task ClarificationsAsync(ApplicationDbContext db,CommitteeClarificationContext context,CancellationToken ct)
 {
  db.ChangeTracker.DetectChanges();
  foreach(var entry in db.ChangeTracker.Entries<Clarification>().Where(e=>e.State==EntityState.Modified&&e.Property(c=>c.Status).IsModified).ToList()){
   var q=entry.Entity;if(!TopCommitteeClarificationScope.Contains(q,context))continue;
   if(q.Status=="Answered"&&entry.Property(c=>c.Status).OriginalValue=="Pending")await AddAsync(db,q.TenderId!.Value,[q.SenderUserId],$"Tender #{q.TenderId}: clarification #{q.ClarificationId} answered",ct);
   if(q.Status=="Resolved"&&entry.Property(c=>c.Status).OriginalValue=="Answered"&&TopCommitteeClarificationScope.OpenCount(context)==0)await AddAsync(db,q.TenderId!.Value,context.Members.Where(m=>m.IsEligible).Select(m=>m.UserId),$"Tender #{q.TenderId}: clarification #{q.ClarificationId} resolved",ct);
  }
 }
 // Manager team calls after persisting a valid referral, using its authenticated principal.
 public static Task PublishAssignmentsAsync(ITopCommitteeTenderTransaction transaction,ClaimsPrincipal manager,int tenderId,CancellationToken ct=default)=>transaction.ExecuteAsync(tenderId,async(db,token)=>{
  var access=new TopCommitteeAccessService(db);var id=await access.RequireRoleAsync(manager,RoleNames.SourcingManager,token);var managers=await access.GetManagersAsync(tenderId,token);if(managers.Count!=1||managers[0].UserId!=id||!managers[0].IsActive||!managers[0].HasManagerRole)throw new CommitteeAccessDeniedException();
  if(!await db.Tenders.AnyAsync(t=>t.TenderId==tenderId&&t.Status=="AwaitingCommitteeDecision"&&!t.TenderApprovals.Any(v=>v.Stage=="Top"),token))throw new CommitteeStateConflictException("Assignment notifications require a valid referral before committee voting.");
  foreach(var member in await access.GetMembersAsync(tenderId,token))if(member.IsEligible)await AddAsync(db,tenderId,[member.UserId],$"Tender #{tenderId}: committee assignment #{member.AssignmentId}",token);await db.SaveChangesAsync(token);return true;
 },ct);
}
