namespace SourcingTenderManagement.Models;

public sealed class AiLogPageViewModel
{
    public const int PageSize = 50;

    public string? Search { get; init; }
    public string? Feature { get; init; }
    public string? Workspace { get; init; }
    public string? Outcome { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; }
    public int TotalMatching { get; init; }

    public AiLogSummary Summary { get; init; } = new(0, 0, 0, 0, 0, null, null);
    public IReadOnlyList<AiLogFeatureRow> Features { get; init; } = [];
    public IReadOnlyList<AiLogRow> Rows { get; init; } = [];
    public IReadOnlyList<string> FeatureOptions { get; init; } = [];
    public IReadOnlyList<string> WorkspaceOptions { get; init; } = [];

    public bool HasFilters => !string.IsNullOrWhiteSpace(Search) || Feature != null || Workspace != null || Outcome != null || From.HasValue || To.HasValue;

    /// <summary>Plain names for the gateway's feature keys.</summary>
    public static string FeatureName(string feature) => feature switch
    {
        "safi.decision" => "Safi: understand the question",
        "safi.synthesis" => "Safi: write the answer",
        "public-safi.answer" => "Safi for visitors",
        "intelligence.portfolio" => "AI Intelligence: brief",
        "intelligence.tender" => "AI Intelligence: tender",
        "intelligence.upload" => "AI Intelligence: uploaded file",
        "reports.recommendation" => "Committee: recommendation",
        "reports.final" => "Committee: final report",
        "reports.shortlist" => "Committee: top-three report",
        "reports.referral-summary" => "Manager: referral summary",
        "reports.tender-importance" => "Manager: tender importance",
        "precheck.offer-documents" => "Officer: offer pre-check",
        _ => feature
    };

    public static string OutcomeName(string outcome) => outcome switch
    {
        "LimitReached" => "Daily limit reached",
        _ => outcome
    };
}

public sealed record AiLogSummary(int Total, int Succeeded, int Failed, int Cancelled, int LimitReached, double? AverageMs, double? ConformRate);

public sealed record AiLogFeatureRow(string Feature, int Calls, double SuccessRate, double? AverageMs, double? ConformRate, int PromptVersions, DateTime LastUsedUtc);

public sealed record AiLogRow(
    long Id,
    DateTime CreatedAtUtc,
    string? UserName,
    string? UserEmail,
    string Workspace,
    string Feature,
    string? RecordType,
    int? RecordId,
    string Outcome,
    string? Model,
    int DurationMs,
    bool? SchemaConforms,
    string? SchemaIssues,
    string? ErrorType,
    string PromptVersion,
    int InputChars,
    int? OutputChars,
    string? OutputPreview,
    bool IsInteractive);
