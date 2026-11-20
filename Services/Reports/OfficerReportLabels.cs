using System.Globalization;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Reports;

/// <summary>
/// Wording for committee reports in the Sourcing Officer workspace. The Reports page and its PDF exports
/// both use these, so a downloaded PDF says exactly what the page says.
/// </summary>
public static class OfficerReportLabels
{
    public static bool NeedsReview(EvaluationReport report) =>
        report.Status == "CommitteeApproved" && report.Tender.Status == "AwaitingOfficerSummary";

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "In preparation",
        "CommitteeApproved" => "Committee approved",
        "OfficerSubmitted" => "With sourcing manager",
        "ManagerApproved" => "Manager approved",
        "TopApproved" => "CEO & Chiefs approved",
        "ReturnedForReview" => "Returned to committee",
        "ManagerRejected" => "Manager rejected",
        _ => status
    };

    /// <summary>The status as the officer sees it, including the prompt when the report waits on them.</summary>
    public static string Status(EvaluationReport report) =>
        NeedsReview(report) ? "Your review needed" : StatusLabel(report.Status);

    public static string Reference(EvaluationReport report) =>
        $"Report #{report.EvaluationReportId} · Tender #{report.TenderId}";

    /// <summary>Submitted time as shown in the report detail facts, e.g. "14 Nov 2026, 12:34 UTC".</summary>
    public static string SubmittedDetail(DateTime? submittedAt) =>
        submittedAt.HasValue
            ? submittedAt.Value.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "Not submitted";

    public static string Submitted(DateTime? submittedAt) =>
        submittedAt.HasValue
            ? submittedAt.Value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "Not submitted";
}
