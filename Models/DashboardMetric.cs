namespace SourcingTenderManagement.Models;

// One number card on a dashboard (label, value and a short note).
public record DashboardMetric(string Label, string Value, string Note, bool Primary = false, string? Action = null, string? Link = null);
