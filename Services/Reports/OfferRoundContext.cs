using System.Globalization;
using System.Text;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Reports;

public static class OfferRoundContext
{
    public static string Describe(Tender tender)
    {
        if (tender.OfferExtensions.Count == 0 && string.IsNullOrWhiteSpace(tender.OfferShortfallReason)) return "";
        var text = new StringBuilder("## Offer rounds and exception\n");
        foreach (var round in tender.OfferExtensions.OrderBy(r => r.Round))
        {
            text.AppendLine($"Round {round.Round - 1} closed {Utc(round.PreviousDeadline)} with {round.PreviousCompliantOffers} compliant offers.")
                .AppendLine($"Round {round.Round} opened {Utc(round.OpenedAt)}; deadline {Utc(round.Deadline)}.");
        }
        if (!string.IsNullOrWhiteSpace(tender.OfferShortfallReason))
            text.AppendLine("Reason for proceeding with fewer than three compliant offers:").AppendLine(tender.OfferShortfallReason.Trim());
        return text.ToString().Trim();
    }

    private static string Utc(DateTime value) => value.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
