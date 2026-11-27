namespace SourcingTenderManagement.Models;

// Data shown on the AI Intelligence page: figures, chart points, tender and supplier rows.
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
