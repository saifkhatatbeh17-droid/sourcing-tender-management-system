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
    public List<Bid> Offers => Tender?.Invitations.SelectMany(i => i.Bids).OrderBy(b => b.BidId).ToList() ?? [];
    public List<CommitteeAssignment> Members => Tender?.CommitteeAssignments.Where(a => a.CommitteeType == "Evaluation").OrderBy(a => a.Position).ToList() ?? [];
    public List<EvaluationCriterion> Criteria => Tender?.EvaluationCriteria.OrderBy(c => c.EvaluationCriterionId).ToList() ?? [];
    public List<TenderApproval> Approvals => Tender?.TenderApprovals.Where(a => a.Stage == "Evaluation").ToList() ?? [];
    public bool UsesScoredWorkflow => !Locked || Tender?.CommitteeWeightsLockedAt != null;
    public List<CommitteeWeightProposal> WeightProposals => Tender?.CommitteeWeightProposals
        .Where(p => Members.Any(m => m.CommitteeAssignmentId == p.CommitteeAssignmentId)).ToList() ?? [];
    public decimal FinancialWeight => Tender?.CommitteeFinancialWeight ?? Criteria.Where(c => c.Category == "Financial").Sum(c => c.Weight);
    public decimal TechnicalWeight => 100 - FinancialWeight;
    public bool ConsensusLocked => Members.Count == 4 && WeightProposals.Count == 4
        && Tender?.CommitteeWeightsLockedAt != null && Tender.CommitteeFinancialWeight is > 0 and < 100;
    public bool IsAccepted(Evaluation? evaluation) => ConsensusLocked && evaluation is { Status: "Approved", ApprovedAt: not null,
        FinancialMark: not null, TechnicalMark: not null }
        && evaluation.FinancialMark >= 0 && evaluation.FinancialMark <= FinancialWeight
        && evaluation.TechnicalMark >= 0 && evaluation.TechnicalMark <= TechnicalWeight;
    public int AcceptedCount(Bid bid) => Members.Count(m => IsAccepted(Evaluation(m, bid)));
    public bool AllOffersAccepted => ConsensusLocked && Offers.Count > 0
        && Offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
        && Offers.All(b => AcceptedCount(b) == 4);
    public List<CommitteeRanking> Leaders
    {
        get { var ranking = Rank(); return ranking.Count == 0 ? [] : ranking.Where(r => r.Score == ranking[0].Score).ToList(); }
    }
    public bool HasTie => AllOffersAccepted && Leaders.Count > 1;
    public int? ScoredWinnerId
    {
        get
        {
            if (!AllOffersAccepted) return null;
            var leaders = Leaders;
            if (leaders.Count == 1) return leaders[0].BidId;
            return leaders.FirstOrDefault(r => Members.All(m => Tender!.CommitteeTieVotes
                .Any(v => v.CommitteeAssignmentId == m.CommitteeAssignmentId && v.BidId == r.BidId)))?.BidId;
        }
    }
    public bool Locked => Tender?.Status != "UnderEvaluation";
    public bool SelectionReady => Tender?.EvaluationReport is { RecommendedBidId: int bidId } report
        && ((report.Status == "Draft" && string.IsNullOrWhiteSpace(report.CommitteeSummary)) || Locked) && Offers.Any(b => b.BidId == bidId);
    public static string StatusLabel(string status) => status switch
    {
        "UnderEvaluation" => "Under evaluation", "AwaitingOfficerSummary" => "With sourcing officer",
        "AwaitingManagerReview" => "With sourcing manager", "AwaitingCommitteeDecision" => "With top committee",
        _ => System.Text.RegularExpressions.Regex.Replace(status, "([a-z])([A-Z])", "$1 $2")
    };
    public bool ReportReady => Tender?.EvaluationReport is { RecommendedBidId: not null, CommitteeSummary: not null } report
        && !string.IsNullOrWhiteSpace(report.CommitteeSummary) && Offers.Any(b => b.BidId == report.RecommendedBidId);
    public bool WeightsReady => (!UsesScoredWorkflow || ConsensusLocked) && Members.Count == 4 && Offers.Count > 0
        && Criteria.All(c => c.Weight > 0) && Criteria.Sum(c => c.Weight) == 100
        && Criteria.Any(c => c.Category == "Financial") && Criteria.Any(c => c.Category == "Requirements")
        && Offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
    public bool Ready => UsesScoredWorkflow ? AllOffersAccepted : LegacyReady;
    private bool LegacyReady => Members.Count == 4 && Offers.Count > 0 && Criteria.Count > 0
        && Criteria.Sum(c => c.Weight) == 100 && Criteria.Any(c => c.Category == "Financial") && Criteria.Any(c => c.Category == "Requirements")
        && Offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
        && Members.All(m => Offers.All(b => ForMember(m).All(c => Score(m, b, c) != null)));

    public decimal? SharedScore(Bid bid, EvaluationCriterion criterion) => Members
        .Where(m => ForMember(m).Contains(criterion)).Select(m => Score(m, bid, criterion)).FirstOrDefault(s => s.HasValue);
    public string SharedVersion => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new {
            Weights = Criteria.Select(c => new { c.EvaluationCriterionId, c.Weight }),
            Consensus = new { Tender?.CommitteeFinancialWeight, Tender?.CommitteeWeightsLockedAt },
            Proposals = WeightProposals.OrderBy(p => p.CommitteeAssignmentId).Select(p => new { p.CommitteeAssignmentId, p.FinancialWeight, p.ConfirmedAt }),
            Offers = Offers.Select(b => new { b.BidId, b.Currency, b.DeliveryDays, b.TechnicalDetails, Total = Total(b), Items = b.BidItems.Select(i => new { i.PurchaseRequestItemId, i.Quantity, i.UnitPrice, i.OfferedSpecifications }) }),
            Forms = Evaluations.OrderBy(e => e.CommitteeAssignmentId).ThenBy(e => e.BidId).Select(e => new { e.CommitteeAssignmentId, e.BidId, e.FinancialMark, e.TechnicalMark, e.Status, e.ApprovedAt }),
            TieVotes = Tender?.CommitteeTieVotes.OrderBy(v => v.CommitteeAssignmentId).Select(v => new { v.CommitteeAssignmentId, v.BidId, v.ConfirmedAt }),
            Report = new { Tender?.EvaluationReport?.RecommendedBidId, Tender?.EvaluationReport?.CommitteeSummary },
            Approvals = Approvals.OrderBy(a => a.UserId).Select(a => new { a.UserId, a.DecidedAt }) }))));

    public static string MemberName(int position) => position switch
    {
        1 => "Committee Manager", 2 => "Department Representative", 3 => "Financial Representative", _ => "Technical Representative"
    };
    public List<EvaluationCriterion> ForMember(CommitteeAssignment member) => UsesScoredWorkflow ? Criteria : Criteria
        .Where(c => c.Category == (member.Position == 3 ? "Financial" : "Requirements")).ToList();
    public Evaluation? Evaluation(CommitteeAssignment member, Bid bid) => Evaluations
        .SingleOrDefault(e => e.CommitteeAssignmentId == member.CommitteeAssignmentId && e.BidId == bid.BidId);
    public decimal? Score(CommitteeAssignment member, Bid bid, EvaluationCriterion criterion) => Evaluation(member, bid)?
        .EvaluationScores.SingleOrDefault(s => s.EvaluationCriterionId == criterion.EvaluationCriterionId)?.Score;
    public static decimal Total(Bid bid) => bid.BidItems.Sum(i => i.Quantity * i.UnitPrice) + bid.TaxAmount + bid.AdditionalCosts;
    public decimal PriceScore(Bid bid)
    {
        var minimum = Offers.Min(Total);
        return Total(bid) == 0 ? 100 : decimal.Round(minimum / Total(bid) * 100, 2);
    }
    public List<CommitteeRanking> Rank(decimal? financialWeight = null)
    {
        if (UsesScoredWorkflow)
        {
            if (!AllOffersAccepted) return [];
            return Offers.Select(b => new CommitteeRanking(b.BidId, b.Invitation.SupplierProfile.CompanyName, Total(b), b.Currency,
                Members.Average(m => Evaluation(m, b)!.FinancialMark!.Value),
                Members.Average(m => Evaluation(m, b)!.TechnicalMark!.Value)))
                .Select(r => r with { Score = r.Financial + r.Requirements })
                .OrderByDescending(r => r.Score).ThenBy(r => r.BidId).ToList();
        }
        if (!Ready) return [];
        var financial = financialWeight ?? Criteria.Where(c => c.Category == "Financial").Sum(c => c.Weight);
        decimal CategoryScore(Bid bid, string category)
        {
            var criteria = Criteria.Where(c => c.Category == category).ToList();
            return criteria.Sum(c => Members.Where(m => ForMember(m).Contains(c))
                .Average(m => Score(m, bid, c)!.Value) * c.Weight) / criteria.Sum(c => c.Weight);
        }
        return Offers.Select(b => new CommitteeRanking(b.BidId, b.Invitation.SupplierProfile.CompanyName, Total(b), b.Currency,
            CategoryScore(b, "Financial"), CategoryScore(b, "Requirements")))
            .Select(r => r with { Score = (r.Financial * financial + r.Requirements * (100 - financial)) / 100 })
            .OrderByDescending(r => r.Score).ThenBy(r => r.BidId).ToList();
    }
}

public record CommitteeRanking(int BidId, string Supplier, decimal Total, string Currency, decimal Financial, decimal Requirements)
{
    public decimal Score { get; init; }
}
public class CommitteeCriterionInput
{
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [Required, RegularExpression("Financial|Requirements")] public string Category { get; set; } = "";
    [Range(typeof(decimal), "0.01", "100")] public decimal Weight { get; set; }
}
public class CommitteeScoreInput
{
    public int BidId { get; set; }
    public int CriterionId { get; set; }
    [Required, Range(typeof(decimal), "0", "100")] public decimal? Score { get; set; }
}
