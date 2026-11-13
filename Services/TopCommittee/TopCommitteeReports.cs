using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
namespace SourcingTenderManagement.Services.TopCommittee;
public sealed class TopCommitteeReports(ApplicationDbContext db,TopCommitteeAccessService access)
{
 public static readonly string[] Statuses=["Draft","Published","Open","Closed","UnderEvaluation","AwaitingOfficerSummary","AwaitingManagerReview","AwaitingCommitteeDecision","Awarded","Cancelled"];
 public async Task<CommitteeReportPage> GetAsync(ClaimsPrincipal actor,CommitteeReportFilter f,bool export=false,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);Validator.ValidateObject(f,new ValidationContext(f),true);
  if(f.FromDate>f.ToDate||f.Status is {Length:>0}&&!Statuses.Contains(f.Status)||f.Decision is {Length:>0}&&f.Decision is not("Approved" or "Rejected" or "NotVoted"))throw new ValidationException("Check status, decision and date range.");
  var assigned=db.Tenders.AsNoTracking().Where(t=>t.CommitteeAssignments.Any(a=>a.UserId==id&&a.CommitteeType=="Top"));var q=assigned;
  if(!string.IsNullOrWhiteSpace(f.Search)){var text=f.Search.Trim();q=q.Where(t=>t.Title.Contains(text));}if(!string.IsNullOrEmpty(f.Status))q=q.Where(t=>t.Status==f.Status);
  if(f.FromDate is {} from){var start=from.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);q=q.Where(t=>t.CreatedAt>=start);}if(f.ToDate is {} to){var end=to.ToDateTime(TimeOnly.MaxValue,DateTimeKind.Utc);q=q.Where(t=>t.CreatedAt<=end);}
  if(f.Decision=="NotVoted")q=q.Where(t=>!t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id));else if(!string.IsNullOrEmpty(f.Decision))q=q.Where(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id&&v.Decision==f.Decision));
  var count=await q.CountAsync(ct);if(export&&count>5000)throw new ValidationException("More than 5000 matching tenders. Narrow the filters before exporting.");
  var summary=new CommitteeReportSummary(await assigned.CountAsync(ct),count,await q.CountAsync(t=>t.Status=="AwaitingCommitteeDecision"&&!t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),ct),await q.CountAsync(t=>t.Status=="Awarded",ct),await q.CountAsync(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),ct),await q.CountAsync(t=>t.Status=="AwaitingManagerReview",ct),await q.CountAsync(t=>t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.UserId==id),ct));
  var ordered=q.OrderByDescending(t=>t.CreatedAt).ThenByDescending(t=>t.TenderId);
  var rows=await (export?ordered.Take(5000):ordered.Skip((f.Page-1)*20).Take(20)).Select(t=>new CommitteeReportRow(t.TenderId,t.Title,t.Status,t.CreatedAt,t.EvaluationReport==null?null:t.EvaluationReport.RecommendedBidId,t.AwardedBidId,t.TenderApprovals.Count(v=>v.Stage=="Top"&&v.Decision=="Approved"),t.TenderApprovals.Any(v=>v.Stage=="Top"&&v.Decision=="Rejected"),t.TenderApprovals.Where(v=>v.Stage=="Top"&&v.UserId==id).Select(v=>v.Decision).FirstOrDefault(),t.AwardedAt??t.TenderApprovals.Where(v=>v.Stage=="Top").Max(v=>(DateTime?)v.DecidedAt))).ToListAsync(ct);
  return new(f,summary,rows,DateTime.UtcNow);
 }
}
