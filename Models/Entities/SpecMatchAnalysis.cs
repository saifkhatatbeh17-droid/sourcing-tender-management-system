namespace SourcingTenderManagement.Models.Entities;

// Database table: a saved spec match result for a tender, so it is not recomputed every time.
public sealed class SpecMatchAnalysis
{
    public int SpecMatchAnalysisId { get; set; }
    public int TenderId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public string? ModelName { get; set; }
    public bool AiAvailable { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
