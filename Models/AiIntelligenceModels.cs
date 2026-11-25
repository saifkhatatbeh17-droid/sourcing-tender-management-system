namespace SourcingTenderManagement.Models;

public sealed record AiKpiCard(
    string Label,
    string Value,
    string Subtext,
    string Tone = "primary");

public sealed record AiChartPoint(
    string Label,
    decimal Value,
    decimal SecondaryValue = 0,
    string ValueFormatted = "",
    string SecondaryFormatted = "",
    string Color = "#1d5560");

public sealed record AiAnalysisCard(
    string Category,
    string Title,
    string Metric,
    string Summary,
    string Tone = "info");

public sealed record AiTenderAnalyticsRow(
    int TenderId,
    string Title,
    string Category,
    string Department,
    string Status,
    int Round,
    int InvitedCount,
    int SubmittedOffersCount,
    int CompliantOffersCount,
    decimal? LowestOfferTotal,
    decimal? HighestOfferTotal,
    string Currency,
    string? WinningSupplier,
    double? LifecycleHours,
    string LifecycleFormatted,
    string CreatedUtc,
    string ClosesUtc,
    string? AwardedUtc);

public sealed record AiSupplierAnalyticsRow(
    int SupplierId,
    string CompanyName,
    string Category,
    string Status,
    int InvitationsCount,
    int SubmittedBidsCount,
    int CompliantBidsCount,
    int AwardedCount,
    decimal ParticipationRatePercent,
    decimal ComplianceRatePercent,
    decimal AwardedValue = 0);

public sealed record AiTenderSelectOption(
    int TenderId,
    string Title,
    string Status,
    string Category);

public sealed record AiTenderItemRow(
    int ItemId,
    string ItemName,
    decimal Quantity,
    string Unit,
    string Specifications);

public sealed record AiTenderOfferVisualRow(
    int BidId,
    string SupplierName,
    string Status,
    string ComplianceStatus,
    decimal Subtotal,
    decimal TaxAmount,
    decimal AdditionalCosts,
    decimal Total,
    string Currency,
    int DeliveryDays,
    string PaymentTerms,
    decimal? AverageScore,
    decimal? FinancialAvg,
    decimal? TechnicalAvg,
    bool IsLowestPrice,
    bool IsFastestDelivery,
    bool IsRecommended,
    bool IsAwardedWinner);

public sealed record AiTenderReportViewModel(
    int TenderId,
    int PurchaseRequestId,
    string Title,
    string Category,
    string Department,
    string Status,
    int Round,
    string Description,
    string? Terms,
    string CreatedUtc,
    string OpensUtc,
    string ClosesUtc,
    string? AwardedUtc,
    string? WinnerNotifiedUtc,
    double? LifecycleHours,
    string LifecycleFormatted,
    int LifecycleStageIndex,
    decimal? FinancialWeight,
    decimal? TechnicalWeight,
    int InvitedCount,
    int SubmittedOffersCount,
    int CompliantOffersCount,
    decimal? LowestTotal,
    decimal? HighestTotal,
    decimal? PotentialSpreadAmount,
    string Currency,
    string? ReportStatus,
    string? RecommendedSupplier,
    string? CommitteeSummaryExcerpt,
    string? OfficerSummary,
    string? ManagerSummary,
    int TopCommitteeApprovals,
    bool CanViewSupplierOffers,
    IReadOnlyList<AiTenderItemRow> RequestedItems,
    IReadOnlyList<AiTenderOfferVisualRow> Offers);

public sealed class AiIntelligencePageModel
{
    public required string Workspace { get; init; }
    public required string WorkspaceDisplayName { get; init; }
    public required string UserFullName { get; init; }
    public required bool CanViewSupplierPricing { get; init; }
    public required string DataScopeDescription { get; init; }
    public required string GeneratedUtc { get; init; }
    public int? SelectedTenderId { get; init; }

    public required IReadOnlyList<AiKpiCard> Kpis { get; init; }
    public required IReadOnlyList<AiChartPoint> StatusDistribution { get; init; }
    public required IReadOnlyList<AiChartPoint> CategoryBreakdown { get; init; }
    public required IReadOnlyList<AiChartPoint> SupplierPerformance { get; init; }
    public required IReadOnlyList<AiChartPoint> TurnaroundTimeline { get; init; }
    public required IReadOnlyList<AiAnalysisCard> AnalysisCards { get; init; }
    public required IReadOnlyList<AiTenderAnalyticsRow> Tenders { get; init; }
    public required IReadOnlyList<AiSupplierAnalyticsRow> Suppliers { get; init; }
    public required IReadOnlyList<AiTenderSelectOption> TenderOptions { get; init; }
    public AiTenderReportViewModel? SelectedTenderReport { get; init; }
}

public sealed record AiGeneratedInsightItem(
    string Category,
    string Title,
    string Finding,
    string Recommendation,
    string Tone);

public sealed record AiGeneratedInsightsResponse(
    string Headline,
    string ExecutiveBrief,
    AiGeneratedInsightItem[] Insights,
    string AdvisoryNotice);

public sealed record AiUploadedFileAnalysisResponse(
    string FileName,
    string FileType,
    string Summary,
    AiKpiCard[] ExtractedMetrics,
    AiGeneratedInsightItem[] Insights,
    string[] TableHeaders,
    string[][] TableRows,
    string AdvisoryNotice,
    Services.Chat.AiChatDownloadLink[] Downloads);

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
