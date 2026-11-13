using System.ComponentModel.DataAnnotations;
namespace SourcingTenderManagement.Models.TopCommittee;
public sealed class CommitteeReportFilter
{
 [StringLength(200)] public string? Search {get;set;}
 public string? Status {get;set;}
 public string? Decision {get;set;}
 public DateOnly? FromDate {get;set;}
 public DateOnly? ToDate {get;set;}
 [Range(1,1000000)] public int Page {get;set;}=1;
}
public sealed record CommitteeReportRow(int TenderId,string Title,string Status,DateTime CreatedAt,int? NominatedBidId,int? AwardedBidId,int Approvals,bool Rejected,string? MyDecision,DateTime? DecisionAt);
public sealed record CommitteeReportSummary(int Assigned,int Matching,int Pending,int Awarded,int Rejected,int Returned,int MyDecisions);
public sealed record CommitteeReportPage(CommitteeReportFilter Filter,CommitteeReportSummary Summary,IReadOnlyList<CommitteeReportRow> Rows,DateTime GeneratedAt);
