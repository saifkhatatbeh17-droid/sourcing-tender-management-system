using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
namespace SourcingTenderManagement.Services.TopCommittee;
public sealed class TopCommitteeReports(ApplicationDbContext db,TopCommitteeAccessService access)
{
 public static readonly string[] Statuses=["Draft","Published","Open","Closed","UnderEvaluation","AwaitingOfficerSummary","AwaitingManagerReview","AwaitingCommitteeDecision","Awarded","Cancelled"];
 public async Task<CommitteeReportPage> GetAsync(ClaimsPrincipal actor,CommitteeReportFilter f,bool export=false,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);Validator.ValidateObject(f,new ValidationContext(f),true);
  if(f.FromDate>f.ToDate||f.Status is {Length:>0}&&!Statuses.Contains(f.Status)||f.Decision is {Length:>0}&&f.Decision is not("Approved" or "Rejected" or "NotVoted"))throw new ValidationException("Check status, decision and date range.");
  var assigned=AssignedTenders(db,id);var q=assigned;
  if(!string.IsNullOrWhiteSpace(f.Search)){var text=f.Search.Trim();q=q.Where(t=>t.Title.Contains(text));}if(!string.IsNullOrEmpty(f.Status))q=q.Where(t=>t.Status==f.Status);
  if(f.FromDate is {} from){var start=from.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);q=q.Where(t=>t.CreatedAt>=start);}if(f.ToDate is {} to){var end=to.ToDateTime(TimeOnly.MaxValue,DateTimeKind.Utc);q=q.Where(t=>t.CreatedAt<=end);}
  if(f.Decision=="NotVoted")q=q.Where(t=>!t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id));else if(!string.IsNullOrEmpty(f.Decision))q=q.Where(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id&&v.Decision==f.Decision));
  var count=await q.CountAsync(ct);if(export&&count>5000)throw new ValidationException("More than 5000 matching tenders. Narrow the filters before exporting.");
  var summary=new CommitteeReportSummary(await assigned.CountAsync(ct),count,await q.CountAsync(t=>t.Status=="AwaitingCommitteeDecision"&&!t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),ct),await q.CountAsync(t=>t.Status=="Awarded",ct),await q.CountAsync(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),ct),await q.CountAsync(t=>t.Status=="AwaitingManagerReview",ct),await q.CountAsync(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id),ct));
  var ordered=q.OrderByDescending(t=>t.CreatedAt).ThenByDescending(t=>t.TenderId);
  var rows=await ProjectRows(export?ordered.Take(5000):ordered.Skip((f.Page-1)*7).Take(7),id).ToListAsync(ct);
  // System-wide aggregates are export-only. Detail rows retain their assignment scope above.
  if (!export) return new(f,summary,rows,DateTime.UtcNow);
  var supplierCount = await db.SupplierProfiles.AsNoTracking().CountAsync(ct);
  if (supplierCount > 10000) throw new ValidationException("More than 10000 suppliers. The comprehensive export exceeds its safe size; contact the administrator.");
  var suppliers = await SupplierRows(db).ToListAsync(ct);
  var system = new CommitteeSystemStatistics(supplierCount, await db.Tenders.AsNoTracking().CountAsync(ct),
      await db.Tenders.AsNoTracking().CountAsync(t => t.Status == "Awarded",ct));
  return new(f,summary,rows,DateTime.UtcNow) { SystemStatistics = system, Suppliers = suppliers };
 }
 internal static IQueryable<Tender> AssignedTenders(ApplicationDbContext context,string memberId) =>
     context.Tenders.AsNoTracking().Where(t=>t.CommitteeAssignments.Any(a=>a.UserId==memberId&&a.CommitteeType=="Top"));
 internal static IQueryable<CommitteeSupplierStatistics> SupplierRows(ApplicationDbContext context) =>
     from supplier in context.SupplierProfiles.AsNoTracking()
     join award in AwardCounts(context.Tenders.AsNoTracking()) on supplier.SupplierProfileId equals award.SupplierId into matches
     from award in matches.DefaultIfEmpty()
     orderby supplier.CompanyName, supplier.SupplierProfileId
     select new CommitteeSupplierStatistics(supplier.SupplierProfileId,supplier.CompanyName,(int?)award.AwardedTenders ?? 0);
 // One projection is shared by production SQL and database-free regression tests.
 internal static IQueryable<CommitteeReportRow> ProjectRows(IQueryable<Tender> tenders, string memberId) =>
     tenders.Select(t => new CommitteeReportRow(t.TenderId,t.Title,t.Status,t.CreatedAt,
         t.EvaluationReport == null ? null : t.EvaluationReport.RecommendedBidId,t.AwardedBidId,
         t.TenderApprovals.Count(v=>v.Stage=="Top"&&v.Decision=="Approved"),
         t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),
         t.TenderApprovals.Where(v=>v.Stage=="Top"&&v.UserId==memberId).Select(v=>v.Decision).FirstOrDefault(),
         t.TenderApprovals.Where(v=>v.Stage=="Top"&&v.UserId==memberId).Select(v=>(DateTime?)v.DecidedAt).FirstOrDefault())
     {
         AwardedAt = t.AwardedAt,
         CommitteeOutcomeAt = t.AwardedAt ?? t.TenderApprovals.Where(v=>v.Stage=="Top").Max(v=>(DateTime?)v.DecidedAt)
     });
 internal static IQueryable<CommitteeSupplierAwardCount> AwardCounts(IQueryable<Tender> tenders) =>
     tenders.Where(t=>t.Status=="Awarded" && t.AwardedBidId.HasValue && t.Bid != null && t.Bid.TenderId==t.TenderId)
         .GroupBy(t=>t.Bid!.Invitation.SupplierProfileId)
         .Select(g=>new CommitteeSupplierAwardCount { SupplierId=g.Key, AwardedTenders=g.Select(t=>t.TenderId).Distinct().Count() });
}

internal sealed class CommitteeSupplierAwardCount
{
 public int SupplierId { get; init; }
 public int AwardedTenders { get; init; }
}
