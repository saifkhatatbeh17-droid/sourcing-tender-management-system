namespace SourcingTenderManagement.Models;

/// <summary>
/// Sales tax on supplier offers. Fixed at 16% (Jordan general sales tax) of the item subtotal;
/// suppliers cannot change it. Offers submitted earlier keep the tax amount they were recorded with.
/// </summary>
public static class OfferTax
{
    public const decimal RatePercent = 16m;

    /// <summary>When the fixed rate started. Offers saved before then may carry the rate the supplier entered.</summary>
    public static readonly DateTime FixedRateSinceUtc = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    public static decimal Amount(decimal itemSubtotal) =>
        decimal.Round(checked(itemSubtotal * RatePercent / 100m), 2, MidpointRounding.AwayFromZero);
}
