namespace SourcingTenderManagement.Models;

public record DashboardMetric(string Label, string Value, string Note, bool Primary = false, string? Action = null);
