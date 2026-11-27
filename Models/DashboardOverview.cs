namespace SourcingTenderManagement.Models;

// The 'where things stand' chart on dashboards: counts per stage.
public sealed record DashboardSlice(string Label, int Count, string Tone = "active", string? Link = null);

public sealed record DashboardOverview(string Title, string Scope, string Unit, string FocusLabel,
    IReadOnlyList<DashboardSlice> Segments, string StagesTitle, string StagesDescription,
    IReadOnlyList<DashboardSlice> Stages, string? Link = null)
{
    public int Total => Segments.Sum(s => s.Count);

    public static DashboardOverview Tenders(IReadOnlyDictionary<string, int> states, string scope)
    {
        int Count(params string[] keys) => keys.Sum(k => states.GetValueOrDefault(k));
        var stages = new List<DashboardSlice> {
            new("Receiving offers", Count("Published", "Open")), new("Reviewing offers", Count("Closed")),
            new("Committee evaluation", Count("UnderEvaluation")), new("Officer report review", Count("AwaitingOfficerSummary")),
            new("Manager review", Count("AwaitingManagerReview")), new("CEO & Chiefs decision", Count("AwaitingCommitteeDecision"))
        };
        var active = stages.Sum(s => s.Count);
        var other = states.Values.Sum() - active - Count("Awarded", "Cancelled");
        var segments = new List<DashboardSlice> { new("Active", active), new("Awarded", Count("Awarded"), "positive"), new("Cancelled", Count("Cancelled"), "muted") };
        if (other > 0) segments.Add(new("Other", other, "other"));
        return new("Tender status", scope, "tenders", "active", segments,
            "Where active tenders are", $"{active:N0} active tenders · current stage", stages);
    }
}
