using Microsoft.Data.SqlClient;

namespace SourcingTenderManagement.Services.Reports.Hub;

// Request-scoped cache: shared sidebar branches reuse the registry's permission decision.
public sealed class ReportNavigation(ReportRegistry registry, ILogger<ReportNavigation> logger)
{
    private readonly Dictionary<string, Task<bool>> visibility = new(StringComparer.Ordinal);

    public Task<bool> HasReportsAsync(string role, CancellationToken ct = default)
    {
        if (!visibility.TryGetValue(role, out var result))
            visibility[role] = result = LoadAsync(role, ct);
        return result;
    }

    private async Task<bool> LoadAsync(string role, CancellationToken ct)
    {
        try { return (await registry.GetAvailableAsync(role, ct)).Count > 0; }
        catch (SqlException exception) when (Controllers.ReportsController.MissingAccessTable(exception))
        {
            logger.LogWarning("Reports navigation hidden: the ReportAccessRules table is unavailable.");
            return false;
        }
    }
}
