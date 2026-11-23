using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Evaluation;

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
