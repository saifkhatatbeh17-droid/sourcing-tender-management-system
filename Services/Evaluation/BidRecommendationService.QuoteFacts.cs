using System.Globalization;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Evaluation;

public partial class BidRecommendationService
{
    // Preserve a structurally valid advisory pick, but never display the model's rejected claims.
    private static BidRecommendation WithVerifiedQuoteFacts(BidRecommendation result, CommitteeEvaluationPage page)
    {
        static string Amount(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
        static string Facts(Bid bid) => $"Offer #{bid.BidId}. Recorded total: {Amount(CommitteeEvaluationPage.Total(bid))} {bid.Currency}. " +
            $"Quoted delivery: {bid.DeliveryDays} days. Tax included in total: {Amount(bid.TaxAmount)}. Additional costs included in total: {Amount(bid.AdditionalCosts)}.";
        var scenarios = result.Scenarios.Select(s => {
            var bid = page.Offers.SingleOrDefault(b => b.BidId == s.RecommendedBidId);
            var reason = "Recommendation\nThe AI explanation could not be verified. Only recorded quote facts are shown; the AI suggestion is advisory and does not replace committee scoring or approvals." +
                $"\n\nEvaluation basis\nFinancial: {s.FinancialWeight}%. Technical: {100 - s.FinancialWeight}%." +
                (bid == null ? "\n\nNo offer was suggested." : "\n\nQuote facts\n" + Facts(bid)) +
                "\n\nReview required\nCompare the offered specifications with the purchase request and complete every committee review before deciding.";
            return s with { Reason = reason };
        }).ToArray();
        var comparisons = page.Offers.Select(b => new BidComparison(b.BidId, Facts(b),
            "Technical suitability and any tradeoffs require review of the submitted specifications. No unverified AI advantages are included.")).ToArray();
        var recovered = new BidRecommendation(scenarios, result.MissingInformation, comparisons, null, true);
        Validate(recovered, page, scenarios.Length == 1 ? scenarios[0].FinancialWeight : null);
        return recovered;
    }
}
