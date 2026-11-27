using System.Text.RegularExpressions;

namespace SourcingTenderManagement.Services.Reports.Hub;

// Cleans up report filter values and explains any that were ignored.
public static class ReportQueryValidation
{
    public static (ReportQuery Query, List<string> Messages) Normalize(IReport report, ReportQuery query)
    {
        var messages = new List<string>();
        bool Supports(string name) => report.Filters.Contains(name, StringComparer.Ordinal);
        int? PositiveId(int? value)
        {
            if (value is null or > 0) return value;
            messages.Add("Invalid record filters were cleared. Choose a record from the list.");
            return null;
        }

        var from = Supports("date") ? query.From : null;
        var to = Supports("date") ? query.To : null;
        if (from.HasValue && to.HasValue && (to < from ||
            (from.Value.Year <= 9994 && to.Value > from.Value.AddYears(5))))
        {
            messages.Add("Choose an end date on or after the start date, with a range of at most five years. The date filter was cleared.");
            from = null;
            to = null;
        }

        var status = Supports("status") ? query.Status?.Trim() : null;
        if (!string.IsNullOrEmpty(status) && !report.StatusOptions.Contains(status, StringComparer.Ordinal))
        {
            messages.Add("The selected status is not supported by this report. All statuses are shown.");
            status = null;
        }

        var search = Supports("search") ? query.Search?.Trim() : null;
        if (search?.Length > 200)
        {
            messages.Add("Search text was shortened to 200 characters.");
            search = search[..200];
        }

        var drill = query.Drill?.Trim();
        if (!string.IsNullOrEmpty(drill) && (drill.Length > 160 || drill.Any(char.IsControl) ||
            !Regex.IsMatch(drill, @"\A[a-z][a-z-]*:[^:]+\z")))
        {
            messages.Add("The drill-down filter was invalid and has been cleared.");
            drill = null;
        }
        if (drill != null)
        {
            var parts = drill.Split(':', 2);
            if (parts[0] is "department" or "supplier" or "tender" &&
                (!int.TryParse(parts[1], out var id) || id <= 0))
            {
                messages.Add("The drill-down record was invalid and has been cleared.");
                drill = null;
            }
            if (parts[0] == "decision" && parts[1] is not ("Approved" or "Rejected" or "NotVoted"))
            {
                messages.Add("The decision drill-down was unsupported and has been cleared.");
                drill = null;
            }
        }

        if (drill != null && report is PurchaseRequestsReport)
        {
            var parts = drill.Split(':', 2);
            if (parts[0] != "department" && (parts[0] != "status" || !report.StatusOptions.Contains(parts[1], StringComparer.Ordinal)))
            {
                messages.Add("The purchase request drill-down was unsupported and has been cleared.");
                drill = null;
            }
        }

        if (drill != null && report is TendersReport)
        {
            var supported = drill.StartsWith("status:", StringComparison.Ordinal) && report.StatusOptions.Contains(drill[7..], StringComparer.Ordinal) ||
                drill is "chart:status" or "chart:activity" or "activity:received" or "activity:invited-no-offers" or "activity:no-invitations" or "activity:zero-offers" or "activity:evaluation-age";
            if (!supported)
            {
                messages.Add("The tender drill-down was unsupported and has been cleared.");
                drill = null;
            }
        }

        return (new ReportQuery(from, to,
            Supports("department") ? PositiveId(query.DepartmentId) : null,
            Supports("supplier") ? PositiveId(query.SupplierId) : null,
            Supports("tender") ? PositiveId(query.TenderId) : null,
            string.IsNullOrEmpty(status) ? null : status,
            string.IsNullOrEmpty(search) ? null : search,
            string.IsNullOrEmpty(drill) ? null : drill,
            Math.Clamp(query.Page, 1, int.MaxValue / 25), ForExport: false), messages);
    }
}
