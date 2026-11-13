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
  var tender=context.Access.Tender!;if(tender.Status is "Awarded" or "AwaitingManagerReview")await AddAsync(db,tender.TenderId,context.Access.Members.Where(m=>m.IsEligible).Select(m=>m.UserId),$"Tender #{tender.TenderId}: {(tender.Status=="Awarded"?"awarded":"rejected and returned to Manager")}",ct);
 }
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
