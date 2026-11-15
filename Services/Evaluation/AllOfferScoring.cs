using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Evaluation;

public static class AllOfferScoring
{
    public static bool Accepted(CommitteeEvaluationPage page, Models.Entities.Evaluation? review) => page.ConsensusLocked
        && review is { Status: "Approved", ApprovedAt: not null, FinancialMark: not null, TechnicalMark: not null }
        && Valid(review.FinancialMark, page.FinancialWeight) && Valid(review.TechnicalMark, page.TechnicalWeight)
        && review.ApprovedAt >= page.Tender!.CommitteeWeightsLockedAt;
    private static bool Valid(decimal? mark, decimal maximum) => mark.HasValue && mark >= 0 && mark <= maximum && decimal.Truncate(mark.Value) == mark;
    public static int Count(CommitteeEvaluationPage page, Bid offer) => page.Members.Count(m => Accepted(page, page.Evaluation(m, offer)));
    public static bool Complete(CommitteeEvaluationPage page) => page.WeightsReady && page.Offers.All(b => Count(page, b) == 4);
    public static List<CommitteeRanking> Ranking(CommitteeEvaluationPage page) => !Complete(page) ? [] : page.Offers.Select(b =>
        new CommitteeRanking(b.BidId, b.Invitation.SupplierProfile.CompanyName, CommitteeEvaluationPage.Total(b), b.Currency,
            page.Members.Average(m => page.Evaluation(m, b)!.FinancialMark!.Value),
            page.Members.Average(m => page.Evaluation(m, b)!.TechnicalMark!.Value)))
        .Select(r => r with { Score = r.Financial + r.Requirements }).OrderByDescending(r => r.Score).ThenBy(r => r.BidId).ToList();
    public static List<CommitteeRanking> Leaders(CommitteeEvaluationPage page)
    { var ranking = Ranking(page); return ranking.Count == 0 ? [] : ranking.Where(r => r.Score == ranking[0].Score).ToList(); }
    public static int? Winner(CommitteeEvaluationPage page)
    {
        var leaders = Leaders(page);
        if (leaders.Count == 1) return leaders[0].BidId;
        return leaders.FirstOrDefault(r => page.Members.All(m => page.Tender!.CommitteeTieVotes.Any(v =>
            v.CommitteeAssignmentId == m.CommitteeAssignmentId && v.BidId == r.BidId && v.ConfirmedAt >= page.Tender.CommitteeWeightsLockedAt)))?.BidId;
    }
    public static List<CommitteeRanking> Shortlist(CommitteeEvaluationPage page)
    {
        var winner = Winner(page);
        return winner == null ? [] : Ranking(page).OrderByDescending(r => r.BidId == winner.Value).ThenByDescending(r => r.Score).ThenBy(r => r.BidId).Take(3).ToList();
    }
    public static string? Save(CommitteeEvaluationPage page, int bidId, decimal? financial, decimal? technical, string? comment, DateTime now)
    {
        var member = page.Members.SingleOrDefault(m => m.UserId == page.UserId);
        var offer = page.Offers.SingleOrDefault(b => b.BidId == bidId);
        if (page.Locked || !page.ConsensusLocked || !page.WeightsReady || member == null || offer == null) return "Confirm all four percentages before reviewing an eligible offer.";
        var review = page.Evaluation(member, offer);
        if (Accepted(page, review)) return "Your submitted marks for this offer are locked.";
        if (!Valid(financial, page.FinancialWeight) || !Valid(technical, page.TechnicalWeight)) return "Enter both whole-number marks within the displayed financial and technical limits.";
        if (comment?.Length > 4000) return "Keep your comment within 4,000 characters.";
        if (review == null)
        { review = new() { TenderId = page.Tender!.TenderId, BidId = bidId, CommitteeAssignmentId = member.CommitteeAssignmentId }; page.Evaluations.Add(review); }
        review.FinancialMark = financial; review.TechnicalMark = technical; review.Notes = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        review.Status = "Approved"; review.SubmittedAt = review.ApprovedAt = now;
        return null;
    }
    public static void SelectWinner(CommitteeEvaluationPage page, DateTime now)
    {
        if (page.Locked || Winner(page) is not int winner) return;
        page.Tender!.EvaluationReport ??= new() { Status = "Draft" };
        var report = page.Tender.EvaluationReport;
        report.RecommendedBidId = winner; report.CommitteeSummary = null; report.Status = "Draft"; report.SubmittedAt = null;
        report.PreparedByUserId = page.Members.Single(m => m.Position == 1).UserId;
        foreach (var member in page.Members)
        {
            var approval = page.Approvals.SingleOrDefault(a => a.UserId == member.UserId);
            if (approval == null) { approval = new() { Stage = "Evaluation", UserId = member.UserId }; page.Tender.TenderApprovals.Add(approval); }
            approval.CommitteeAssignmentId = member.CommitteeAssignmentId; approval.Decision = "Approved"; approval.DecidedAt = now;
            approval.Notes = "Every eligible offer reviewed. Highest average score progresses to the report step.";
        }
    }
    public static string Report(CommitteeEvaluationPage page)
    {
        if (!Complete(page) || Winner(page) == null) throw new InvalidOperationException("Finish all offer reviews and resolve any tie first.");
        var lines = new List<string> { "Offer score comparison" };
        lines.Add("Each offer’s total is the exact arithmetic mean of four financial-plus-technical reviews. Highest total progresses to the report step.");
        if (Leaders(page).Count > 1) lines.Add("Equal highest scores were resolved by four unanimous committee decisions; original marks remain unchanged.");
        foreach (var row in Ranking(page)) lines.Add($"Offer #{row.BidId} — {row.Supplier}: financial {row.Financial:0.##} / {page.FinancialWeight:0}; technical {row.Requirements:0.##} / {page.TechnicalWeight:0}; overall {row.Score:0.##} / 100.");
        lines.Add("\nMember reviews");
        foreach (var offer in page.Offers) foreach (var member in page.Members)
        { var review = page.Evaluation(member, offer)!; lines.Add($"\nOffer #{offer.BidId} — {CommitteeEvaluationPage.MemberName(member.Position)}\nFinancial: {review.FinancialMark:0} / {page.FinancialWeight:0}; technical: {review.TechnicalMark:0} / {page.TechnicalWeight:0}\nComment: {review.Notes ?? "No comment provided."}"); }
        return string.Join("\n", lines);
    }
}
