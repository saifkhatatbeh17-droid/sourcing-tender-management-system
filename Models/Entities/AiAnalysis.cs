using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a saved AI recommendation for a tender (the weights used and the AI's answer).
public class AiAnalysis
{
    public int AiAnalysisId { get; set; }

    public int TenderId { get; set; }

    public string RequestedByUserId { get; set; } = null!;

    public decimal FinancialWeight { get; set; }

    public decimal RequirementsWeight { get; set; }

    public string ModelName { get; set; } = null!;

    public string ResultJson { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public ApplicationUser RequestedByUser { get; set; } = null!;

    public Tender Tender { get; set; } = null!;
}
