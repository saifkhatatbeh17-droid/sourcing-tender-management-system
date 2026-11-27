using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

public class CommitteeAssignment
{
    public int CommitteeAssignmentId { get; set; }

    public int TenderId { get; set; }

    public string UserId { get; set; } = null!;

    public string CommitteeType { get; set; } = null!;

    public int Position { get; set; }

    public ICollection<Evaluation> Evaluations { get; set; } = new List<Evaluation>();

    public Tender Tender { get; set; } = null!;

    public ICollection<TenderApproval> TenderApprovalCommitteeAssignmentNavigations { get; set; } = new List<TenderApproval>();

    public ICollection<TenderApproval> TenderApprovalCommitteeAssignments { get; set; } = new List<TenderApproval>();

    public ApplicationUser User { get; set; } = null!;
}
