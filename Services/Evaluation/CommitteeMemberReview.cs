using System.Globalization;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Evaluation;

public static class CommitteeMemberReview
{
    public static string? Save(CommitteeEvaluationPage page, decimal? financial, decimal? technical, string? comment, DateTime now)
    {
        if (page.Locked || !page.ConsensusLocked || !page.SelectionReady) return "Select an offer after the percentages are locked.";
        var member = page.Members.SingleOrDefault(m => m.UserId == page.UserId);
        if (member == null || Reviews(page).Any(e => e.CommitteeAssignmentId == member.CommitteeAssignmentId)) return "Your review cannot be changed.";
        if (!Valid(financial, page.FinancialWeight) || !Valid(technical, page.TechnicalWeight))
            return "Enter whole-number marks from zero to each displayed maximum.";
        if (comment?.Trim().Length > 4000) return "Keep your comment within 4,000 characters.";
        var bid = page.Offers.Single(b => b.BidId == page.Tender!.EvaluationReport!.RecommendedBidId);
        var entry = page.Evaluation(member, bid);
        if (entry == null)
        {
            entry = new Models.Entities.Evaluation { TenderId = page.Tender!.TenderId, BidId = bid.BidId,
                CommitteeAssignmentId = member.CommitteeAssignmentId };
            page.Evaluations.Add(entry);
        }
        entry.FinancialMark = financial;
        entry.TechnicalMark = technical;
        entry.Notes = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        entry.Status = "Approved";
        entry.SubmittedAt = entry.ApprovedAt = now;
        return null;
    }

    private static bool Valid(decimal? mark, decimal maximum) => mark.HasValue
        && mark >= 0 && mark <= maximum && decimal.Truncate(mark.Value) == mark;

    public static List<Models.Entities.Evaluation> Reviews(CommitteeEvaluationPage page) => page.Members
        .Where(m => page.Approvals.Any(a => a.UserId == m.UserId))
        .Select(m => page.Evaluations.SingleOrDefault(e => e.CommitteeAssignmentId == m.CommitteeAssignmentId
            && e.BidId == page.Tender?.EvaluationReport?.RecommendedBidId))
        .OfType<Models.Entities.Evaluation>().Where(e => Valid(e.FinancialMark, page.FinancialWeight) && Valid(e.TechnicalMark, page.TechnicalWeight)).ToList();

    public static bool Complete(CommitteeEvaluationPage page) => Reviews(page).Count == 4;

    public static string Report(CommitteeEvaluationPage page)
    {
        var reviews = Reviews(page);
        if (reviews.Count != 4) throw new InvalidOperationException("Four member reviews are required.");
        string Whole(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        var result = $"Committee marks\nFinancial average: {Whole(reviews.Average(e => e.FinancialMark!.Value))} / {Whole(page.FinancialWeight)}\nTechnical average: {Whole(reviews.Average(e => e.TechnicalMark!.Value))} / {Whole(page.TechnicalWeight)}\nOverall average: {Whole(reviews.Average(e => e.FinancialMark!.Value + e.TechnicalMark!.Value))} / 100\nAverages rounded to the nearest whole number; overall calculated from unrounded member totals.\n\nMember reviews\n";
        return result + string.Join("\n\n", reviews.Select(e => {
            var member = page.Members.Single(m => m.CommitteeAssignmentId == e.CommitteeAssignmentId);
            return $"{CommitteeEvaluationPage.MemberName(member.Position)} — {member.User.FullName}\nFinancial: {Whole(e.FinancialMark!.Value)} / {Whole(page.FinancialWeight)}; technical: {Whole(e.TechnicalMark!.Value)} / {Whole(page.TechnicalWeight)}\nComment: {e.Notes ?? "No comment provided."}";
        }));
    }
}
