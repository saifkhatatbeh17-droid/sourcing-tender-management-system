namespace SourcingTenderManagement.Models.Entities;

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
