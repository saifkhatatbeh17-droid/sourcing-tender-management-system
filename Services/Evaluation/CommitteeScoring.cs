using System.Globalization;
using System.Text;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Evaluation;

public static class CommitteeScoring
{
    public static string? ConfirmWeight(CommitteeEvaluationPage page, decimal? financialWeight, DateTime now)
    {
        var member = page.Members.SingleOrDefault(m => m.UserId == page.UserId);
        if (page.Locked || member == null || page.Members.Count != 4) return "Four assigned committee members are required.";
        if (page.ConsensusLocked || page.WeightProposals.Any(p => p.CommitteeAssignmentId == member.CommitteeAssignmentId))
            return "Your confirmed percentage is locked and cannot be changed.";
        if (financialWeight is not > 0 or not < 100 || decimal.Round(financialWeight.Value, 2) != financialWeight)
            return "Enter a financial percentage between 0.01 and 99.99, with at most two decimal places.";
        page.Tender!.CommitteeWeightProposals.Add(new CommitteeWeightProposal
        {
            TenderId = page.Tender.TenderId, CommitteeAssignmentId = member.CommitteeAssignmentId,
            FinancialWeight = financialWeight.Value, ConfirmedAt = now
        });
        if (page.WeightProposals.Count != 4) return null;
        var average = decimal.Round(page.WeightProposals.Average(p => p.FinancialWeight), 2, MidpointRounding.AwayFromZero);
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

    public static string? AcceptOffer(CommitteeEvaluationPage page, int bidId, decimal? financialMark, decimal? technicalMark, string? notes, DateTime now)
    {
        var member = page.Members.SingleOrDefault(m => m.UserId == page.UserId);
        var bid = page.Offers.SingleOrDefault(b => b.BidId == bidId);
        if (page.Locked || member == null || bid == null) return "This offer is not available for your evaluation.";
        if (!page.ConsensusLocked) return "All four members must confirm their percentages before scoring offers.";
        if (!page.WeightsReady) return "Offers must use one currency and valid financial and technical criteria before scoring.";
        if (!ValidMark(financialMark, page.FinancialWeight) || !ValidMark(technicalMark, page.TechnicalWeight))
            return $"Enter both marks: financial 0–{page.FinancialWeight:0.##} and technical 0–{page.TechnicalWeight:0.##}, with at most two decimal places.";
        if (notes?.Length > 4000) return "Keep your review note within 4,000 characters.";
        var entry = page.Evaluation(member, bid);
        if (page.IsAccepted(entry)) return "Your accepted marks are locked and cannot be changed.";
        if (entry == null)
        {
            entry = new Models.Entities.Evaluation { TenderId = page.Tender!.TenderId, BidId = bidId, CommitteeAssignmentId = member.CommitteeAssignmentId };
            page.Evaluations.Add(entry);
        }
        entry.FinancialMark = financialMark;
        entry.TechnicalMark = technicalMark;
        entry.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entry.Status = "Approved";
        entry.SubmittedAt = now;
        entry.ApprovedAt = now;
        foreach (var criterion in page.Criteria)
        {
            var mark = criterion.Category == "Financial" ? financialMark!.Value : technicalMark!.Value;
            var maximum = criterion.Category == "Financial" ? page.FinancialWeight : page.TechnicalWeight;
            var score = entry.EvaluationScores.SingleOrDefault(s => s.EvaluationCriterionId == criterion.EvaluationCriterionId);
            if (score == null)
            {
                score = new EvaluationScore { TenderId = page.Tender!.TenderId, EvaluationCriterionId = criterion.EvaluationCriterionId };
                entry.EvaluationScores.Add(score);
            }
            // Existing reports consume category marks normalised to 100.
            score.Score = decimal.Round(mark / maximum * 100, 2);
        }
        return null;
    }

    public static string? ResolveTie(CommitteeEvaluationPage page, int bidId, DateTime now)
    {
        var member = page.Members.SingleOrDefault(m => m.UserId == page.UserId);
        if (page.Locked || member == null || !page.HasTie || !page.Leaders.Any(r => r.BidId == bidId))
            return "Choose one of the tied highest-scoring offers after all reviews are complete.";
        var vote = page.Tender!.CommitteeTieVotes.SingleOrDefault(v => v.CommitteeAssignmentId == member.CommitteeAssignmentId);
        if (vote == null)
        {
            vote = new CommitteeTieVote { TenderId = page.Tender.TenderId, CommitteeAssignmentId = member.CommitteeAssignmentId };
            page.Tender.CommitteeTieVotes.Add(vote);
        }
        vote.BidId = bidId;
        vote.ConfirmedAt = now;
        return null;
    }

    public static bool FinalizeIfReady(CommitteeEvaluationPage page, DateTime now)
    {
        if (page.Locked || page.ScoredWinnerId is not int winnerId) return false;
        var winner = page.Rank().Single(r => r.BidId == winnerId);
        var tender = page.Tender!;
        tender.EvaluationReport ??= new EvaluationReport { TenderId = tender.TenderId };
        var report = tender.EvaluationReport;
        report.RecommendedBidId = winnerId;
        report.PreparedByUserId = page.Members.OrderBy(m => m.Position).First().UserId;
        report.Status = "CommitteeApproved";
        report.SubmittedAt = now;
        var text = new StringBuilder();
        text.AppendLine("Committee scoring result").AppendLine($"Selected offer #{winnerId}: {winner.Supplier}.")
            .AppendLine($"Final score: {Number(winner.Score)} / 100. Financial maximum: {Number(page.FinancialWeight)}; technical maximum: {Number(page.TechnicalWeight)}.")
            .AppendLine("Each assigned member independently scored every eligible offer. Scores below are the arithmetic averages of four locked member reviews.");
        foreach (var row in page.Rank())
            text.AppendLine($"Offer #{row.BidId} — {row.Supplier}: financial {Number(row.Financial)}, technical {Number(row.Requirements)}, total {Number(row.Score)} / 100; quoted total {row.Total.ToString("N2", CultureInfo.InvariantCulture)} {row.Currency}.");
        if (page.HasTie) text.AppendLine("Equal highest scores were resolved by four unanimous committee tie decisions; original marks were unchanged.");
        text.AppendLine("The selected offer progresses for sourcing officer review. This is a committee score report, not a final procurement award.");
        report.CommitteeSummary = text.ToString();
        foreach (var member in page.Members)
        {
            var approval = page.Approvals.SingleOrDefault(a => a.UserId == member.UserId);
            if (approval == null)
            {
                approval = new TenderApproval { TenderId = tender.TenderId, Stage = "Evaluation", UserId = member.UserId };
                tender.TenderApprovals.Add(approval);
            }
            approval.CommitteeAssignmentId = member.CommitteeAssignmentId;
            approval.Decision = "Approved";
            approval.DecidedAt = now;
            approval.Notes = "All eligible offers reviewed with locked financial and technical marks. Highest-scoring offer determined by the committee scoring workflow.";
        }
        tender.Status = "AwaitingOfficerSummary";
        return true;
    }

    // An explicit upstream return starts fresh reviews under the same frozen percentage.
    public static void ReopenReviews(Tender tender, IEnumerable<Models.Entities.Evaluation> evaluations)
    {
        tender.Status = "UnderEvaluation";
        if (tender.EvaluationReport is { } report)
        {
            report.Status = "ReturnedForReview";
            report.RecommendedBidId = null;
            report.CommitteeSummary = null;
            report.SubmittedAt = null;
        }
        foreach (var evaluation in evaluations)
        {
            evaluation.Status = "Submitted";
            evaluation.ApprovedAt = null;
        }
    }

    private static bool ValidMark(decimal? mark, decimal maximum) => mark.HasValue && mark >= 0 && mark <= maximum && decimal.Round(mark.Value, 2) == mark;
    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
