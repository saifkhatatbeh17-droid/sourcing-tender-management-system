namespace SourcingTenderManagement.Models.Entities;

// Database table: a committee member's pick when two offers are tied.
public sealed class CommitteeTieVote
{
    public int CommitteeTieVoteId { get; set; }
    public int TenderId { get; set; }
    public int CommitteeAssignmentId { get; set; }
    public int BidId { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public Tender Tender { get; set; } = null!;
    public CommitteeAssignment CommitteeAssignment { get; set; } = null!;
    public Bid Bid { get; set; } = null!;
}
