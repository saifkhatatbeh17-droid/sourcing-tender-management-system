using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Data;
public class ApplicationUser : IdentityUser
{
    [MaxLength(150)] public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int? DepartmentId { get; set; }
    public ICollection<AiAnalysis> AiAnalyses { get; set; } = new List<AiAnalysis>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<Clarification> ClarificationRecipientUsers { get; set; } = new List<Clarification>();
    public ICollection<ClarificationResponse> ClarificationResponses { get; set; } = new List<ClarificationResponse>();
    public ICollection<Clarification> ClarificationSenderUsers { get; set; } = new List<Clarification>();
    public ICollection<CommitteeAssignment> CommitteeAssignments { get; set; } = new List<CommitteeAssignment>();
    public Department? Department { get; set; }
    public ICollection<EvaluationReport> EvaluationReports { get; set; } = new List<EvaluationReport>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<PurchaseRequest> PurchaseRequestRequestedByUsers { get; set; } = new List<PurchaseRequest>();
    public ICollection<PurchaseRequest> PurchaseRequestReviewedByUsers { get; set; } = new List<PurchaseRequest>();
    public ICollection<SupplierProfile> SupplierProfileReviewedByUsers { get; set; } = new List<SupplierProfile>();
    public SupplierProfile? SupplierProfileUser { get; set; }
    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
}
