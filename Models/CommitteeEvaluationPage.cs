using SourcingTenderManagement.Services.Evaluation;
using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

public class CommitteeEvaluationPage
{
    public SourcingTenderManagement.Services.Evaluation.BidRecommendation? AiRecommendation { get; set; }
    public DateTime? AiCreatedAt { get; set; }
    public bool AiOutdated { get; set; }
    public List<Tender> Tenders { get; set; } = [];
    public Tender? Tender { get; set; }
    public List<Evaluation> Evaluations { get; set; } = [];
    public string UserId { get; set; } = "";
    public int? ReviewOfferId { get; set; }
    public Bid? ReviewOffer => Offers.FirstOrDefault(b => b.BidId == ReviewOfferId)
        ?? Offers.FirstOrDefault(b => Members.Any(m => m.UserId == UserId && !AllOfferScoring.Accepted(this, Evaluation(m, b))))
        ?? Offers.FirstOrDefault();
    public bool MyReviewApproved(Bid offer) => Members.FirstOrDefault(m => m.UserId == UserId) is { } member
        && AllOfferScoring.Accepted(this, Evaluation(member, offer));
    public List<Bid> Offers => Tender?.Invitations.SelectMany(i => i.Bids).OrderBy(b => b.BidId).ToList() ?? [];
    public List<CommitteeAssignment> Members => Tender?.CommitteeAssignments.Where(a => a.CommitteeType == "Evaluation").OrderBy(a => a.Position).ToList() ?? [];
    public List<EvaluationCriterion> Criteria => Tender?.EvaluationCriteria.OrderBy(c => c.EvaluationCriterionId).ToList() ?? [];
    public List<TenderApproval> Approvals => Tender?.TenderApprovals.Where(a => a.Stage == "Evaluation").ToList() ?? [];
    public List<CommitteeWeightProposal> WeightProposals => Tender?.CommitteeWeightProposals
        .Where(p => Members.Any(m => m.CommitteeAssignmentId == p.CommitteeAssignmentId)).ToList() ?? [];
    public decimal FinancialWeight => decimal.Round(Tender?.CommitteeFinancialWeight ?? Criteria.Where(c => c.Category == "Financial").Sum(c => c.Weight), 0, MidpointRounding.AwayFromZero);
    public decimal TechnicalWeight => 100 - FinancialWeight;
    public bool ConsensusLocked => Members.Count == 4 && WeightProposals.Count == 4
        && Tender?.CommitteeWeightsLockedAt != null && Tender.CommitteeFinancialWeight is > 0 and < 100;
    // Completed historical evaluations remain readable without retroactive percentage proposals.
    public bool PercentageReady => Locked || ConsensusLocked;
    public bool Locked => Tender?.Status != "UnderEvaluation";
    public bool SelectionReady => Tender?.EvaluationReport is { RecommendedBidId: int bidId } report
        && (Locked || (AllOfferScoring.Complete(this) && AllOfferScoring.Winner(this) == bidId)) && Offers.Any(b => b.BidId == bidId);
    public static string StatusLabel(string status) => status switch
    {
        "UnderEvaluation" => "Under evaluation", "AwaitingOfficerSummary" => "With sourcing officer",
        "AwaitingManagerReview" => "With sourcing manager", "AwaitingCommitteeDecision" => "With CEO & Chiefs",
        _ => System.Text.RegularExpressions.Regex.Replace(status, "([a-z])([A-Z])", "$1 $2")
    };
    public bool ReportReady => Tender?.EvaluationReport is { RecommendedBidId: not null, CommitteeSummary: not null } report
        && !string.IsNullOrWhiteSpace(report.CommitteeSummary) && Offers.Any(b => b.BidId == report.RecommendedBidId);
    public bool WeightsReady => PercentageReady && Members.Count == 4 && Offers.Count > 0
        && Criteria.All(c => c.Weight > 0) && Criteria.Sum(c => c.Weight) == 100
        && Criteria.Any(c => c.Category == "Financial") && Criteria.Any(c => c.Category == "Requirements")
        && Offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
    public bool Ready => Members.Count == 4 && Offers.Count > 0 && Criteria.Count > 0
        && Criteria.Sum(c => c.Weight) == 100 && Criteria.Any(c => c.Category == "Financial") && Criteria.Any(c => c.Category == "Requirements")
        && Offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
        && Members.All(m => Offers.All(b => ForMember(m).All(c => Score(m, b, c) != null)));

    public string SharedVersion => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new {
            Consensus = new { Tender?.CommitteeFinancialWeight, Tender?.CommitteeWeightsLockedAt },
            Proposals = WeightProposals.OrderBy(p => p.CommitteeAssignmentId).Select(p => new { p.CommitteeAssignmentId, p.FinancialWeight, p.ConfirmedAt }),
            Reviews = Evaluations.OrderBy(e => e.CommitteeAssignmentId).ThenBy(e => e.BidId).Select(e => new { e.CommitteeAssignmentId, e.BidId, e.Status, e.ApprovedAt, e.FinancialMark, e.TechnicalMark, e.Notes }),
            TieVotes = Tender?.CommitteeTieVotes.OrderBy(v => v.CommitteeAssignmentId).Select(v => new { v.CommitteeAssignmentId, v.BidId, v.ConfirmedAt }),
            Weights = Criteria.Select(c => new { c.EvaluationCriterionId, c.Weight }),
            Report = new { Tender?.EvaluationReport?.RecommendedBidId, Tender?.EvaluationReport?.CommitteeSummary },
            Approvals = Approvals.OrderBy(a => a.UserId).Select(a => new { a.UserId, a.DecidedAt }) }))));

    public bool IsDepartmentManager => Members.Any(m => m.Position == 1 && m.UserId == UserId);
    public static string MemberName(int position) => position switch
    {
        1 => "Department Manager", 2 => "Department Financial Officer", 3 => "Sourcing Department Representative", _ => "Financial Manager"
    };
    public List<EvaluationCriterion> ForMember(CommitteeAssignment member) => Criteria
        .Where(c => c.Category == (member.Position == 3 ? "Financial" : "Requirements")).ToList();
    public Evaluation? Evaluation(CommitteeAssignment member, Bid bid) => Evaluations
        .SingleOrDefault(e => e.CommitteeAssignmentId == member.CommitteeAssignmentId && e.BidId == bid.BidId);
    public decimal? Score(CommitteeAssignment member, Bid bid, EvaluationCriterion criterion) => Evaluation(member, bid)?
        .EvaluationScores.SingleOrDefault(s => s.EvaluationCriterionId == criterion.EvaluationCriterionId)?.Score;
    public static decimal Total(Bid bid) => bid.BidItems.Sum(i => i.Quantity * i.UnitPrice) + bid.TaxAmount + bid.AdditionalCosts;
}

public record CommitteeRanking(int BidId, string Supplier, decimal Total, string Currency, decimal Financial, decimal Requirements)
{
    public decimal Score { get; init; }
}
