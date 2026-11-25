using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

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
        var rounds = SourcingTenderManagement.Services.Reports.OfferRoundContext.Describe(page.Tender!);
        if (rounds.Length > 0) lines.Add("\n" + rounds);
        return string.Join("\n", lines);
    }
}

public static class CommitteeConsensus
{
    public static string? ConfirmWeight(CommitteeEvaluationPage page, decimal? financialWeight, DateTime now)
    {
        var member = page.Members.SingleOrDefault(m => m.UserId == page.UserId);
        if (page.Locked || member == null || page.Members.Count != 4) return "Four assigned committee members are required.";
        if (page.ConsensusLocked || page.WeightProposals.Any(p => p.CommitteeAssignmentId == member.CommitteeAssignmentId))
            return "Your confirmed percentage is locked and cannot be changed.";
        if (financialWeight is not > 0 or not < 100 || decimal.Truncate(financialWeight.Value) != financialWeight)
            return "Enter a whole-number financial percentage between 1 and 99.";
        page.Tender!.CommitteeWeightProposals.Add(new CommitteeWeightProposal
        {
            TenderId = page.Tender.TenderId, CommitteeAssignmentId = member.CommitteeAssignmentId,
            FinancialWeight = financialWeight.Value, ConfirmedAt = now
        });
        if (page.WeightProposals.Count != 4) return null;
        var average = decimal.Round(page.WeightProposals.Average(p => p.FinancialWeight), 0, MidpointRounding.AwayFromZero);
        foreach (var category in new[] { "Financial", "Requirements" })
        {
            var criteria = page.Criteria.Where(c => c.Category == category).ToList();
            if (criteria.Count == 0 || criteria.Any(c => c.Weight <= 0)) return "Both financial and technical criteria are required before locking percentages.";
            var previousTotal = criteria.Sum(c => c.Weight);
            var target = category == "Financial" ? average : 100 - average;
            decimal assigned = 0;
            for (var index = 0; index < criteria.Count; index++)
            {
                var next = index == criteria.Count - 1 ? target - assigned : decimal.Round(target * criteria[index].Weight / previousTotal, 2);
                if (next <= 0) return "The proposed percentage is too small for the configured criteria. Ask the administrator to review the criteria.";
                criteria[index].Weight = next;
                assigned += next;
            }
        }
        page.Tender.CommitteeFinancialWeight = average;
        page.Tender.CommitteeWeightsLockedAt = now;
        return null;
    }

}

public static class CommitteeSetup
{
    public static async Task<string?> Prepare(ApplicationDbContext db, Tender tender)
    {
        var assigned = await db.CommitteeAssignments.Where(a => a.TenderId == tender.TenderId && a.CommitteeType == "Evaluation").ToListAsync();
        if (assigned.Count == 4 && assigned.Select(a => a.UserId).Distinct().Count() == 4) return null;
        if (assigned.Count != 0) return "The committee assignments are incomplete. Ask the administrator to correct them.";
        var members = new List<string>();
        foreach (var role in RoleNames.EvaluationPositions)
        {
            var candidates = await db.Users.Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role))
                && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.EvaluationCommittee)))
                .Select(u => u.Id).ToListAsync();
            if (candidates.Count != 1) return $"The administrator must assign exactly one active committee member for {role}.";
            members.Add(candidates[0]);
        }
        if (members.Distinct().Count() != 4) return "Four different committee members are required.";
        for (var i = 0; i < members.Count; i++)
        {
            tender.CommitteeAssignments.Add(new CommitteeAssignment { UserId = members[i], CommitteeType = "Evaluation", Position = i + 1 });
            db.Notifications.Add(new Notification { UserId = members[i], Title = "Tender ready for review", Message = $"{tender.Title}: review the offers and choose a supplier.", RelativeUrl = $"/EvaluationCommittee/Evaluations/{tender.TenderId}", CreatedAt = DateTime.UtcNow });
        }
        if (!await db.EvaluationCriteria.AnyAsync(c => c.TenderId == tender.TenderId))
        {
            tender.EvaluationCriteria.Add(new EvaluationCriterion { Name = "Price", Category = "Financial", Weight = 40 });
            tender.EvaluationCriteria.Add(new EvaluationCriterion { Name = "Technical compliance", Category = "Requirements", Weight = 60 });
        }
        if (!await db.EvaluationReports.AnyAsync(r => r.TenderId == tender.TenderId))
            tender.EvaluationReport = new EvaluationReport { Status = "Draft", PreparedByUserId = members[0] };
        return null;
    }
}

/// <summary>Result of comparing one offer's legal warranty with what a department asked for. Verdict is Match, Partial or Missing.</summary>
public sealed record LegalWarrantyVerdict(string Verdict, string Text);

/// <summary>
/// Decides in code, never with AI, whether an offer's legal warranty covers the months a requested item needs.
/// An offer has one legal warranty (start and end dates), so it is compared against each item that asks for one.
/// </summary>
public static class LegalWarrantyMatch
{
    public static LegalWarrantyVerdict Evaluate(int requiredMonths, BidGuarantee? legal)
    {
        var asked = Months(requiredMonths);
        if (legal is null) return new("Missing", $"Legal warranty: {asked} needed, none attached to this offer.");

        // Dates are inclusive (1 Jan to 31 Dec is a full 12 months), like BidGuarantee.DurationDays.
        var meets = legal.ExpiresOn >= legal.StartsOn.AddMonths(requiredMonths).AddDays(-1);
        var offered = Months(FullMonths(legal));
        return meets
            ? new("Match", $"Legal warranty: {asked} needed, {offered} offered.")
            : new("Partial", $"Legal warranty: {asked} needed, only {offered} offered.");
    }

    public static int FullMonths(BidGuarantee legal)
    {
        var end = legal.ExpiresOn.AddDays(1);
        var months = (end.Year - legal.StartsOn.Year) * 12 + end.Month - legal.StartsOn.Month;
        if (legal.StartsOn.AddMonths(months) > end) months--;
        return Math.Max(months, 0);
    }

    private static string Months(int count) => $"{count} month{(count == 1 ? "" : "s")}";
}
