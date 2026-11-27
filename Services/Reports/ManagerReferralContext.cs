using System.Globalization;
using System.Text;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Reports;

// Writes the purchase facts (request, tender, winning offer) that go with the manager's referral to the CEO & Chiefs.
public static class ManagerReferralContext
{
    // Saved purchase facts accompany every referral, including manually written referrals.
    public static string Describe(Tender tender, Bid winner)
    {
        var request = tender.PurchaseRequest;
        var text = new StringBuilder().AppendLine("## Tender and purchase requirements")
            .AppendLine($"Tender #{tender.TenderId}: {tender.Title}")
            .AppendLine($"Scope: {Value(tender.Description)}")
            .AppendLine($"Request: {request.Title}. Department: {request.Department.Name}.")
            .AppendLine($"Purchase purpose: {Value(request.Reason)}")
            .AppendLine($"Recorded priority: {Value(request.Priority)}. Required date: {request.TargetDate:yyyy-MM-dd}.")
            .AppendLine("Requested items:");
        foreach (var item in request.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId))
            text.AppendLine($"- {item.ItemName}: {Number(item.Quantity)} {item.Unit}. Specifications: {Value(item.Specifications)}");
        text.AppendLine($"Tender terms: {Value(tender.Terms)}").AppendLine().AppendLine("## Winning offer")
            .AppendLine($"{winner.Invitation.SupplierProfile.CompanyName}, offer #{winner.BidId}: {CommitteeEvaluationPage.Total(winner).ToString("N2", CultureInfo.InvariantCulture)} {winner.Currency} including tax and fees; {winner.DeliveryDays}-day delivery.")
            .AppendLine($"Payment: {Value(winner.PaymentTerms)}")
            .AppendLine($"Supplier-stated technical details: {Value(winner.TechnicalDetails)}");
        text.AppendLine("Purchase requirements are recorded facts; supplier statements require verification. Missing or unclear requirements must be clarified, not inferred.");
        var rounds = OfferRoundContext.Describe(tender);
        if (rounds.Length > 0) text.AppendLine().AppendLine(rounds);
        return text.ToString().Trim();
    }
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Not specified" : value.Trim();
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
