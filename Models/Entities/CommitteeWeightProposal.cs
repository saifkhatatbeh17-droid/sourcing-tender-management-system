namespace SourcingTenderManagement.Models.Entities;

// Database table: a committee member's proposed financial/technical weight for a tender.
public sealed class CommitteeWeightProposal
{
    public int CommitteeWeightProposalId { get; set; }
    public int TenderId { get; set; }
    public int CommitteeAssignmentId { get; set; }
    public decimal FinancialWeight { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public Tender Tender { get; set; } = null!;
    public CommitteeAssignment CommitteeAssignment { get; set; } = null!;
}
