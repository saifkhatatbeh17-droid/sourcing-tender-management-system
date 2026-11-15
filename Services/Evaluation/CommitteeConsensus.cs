using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Evaluation;

public static class CommitteeConsensus
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

}
