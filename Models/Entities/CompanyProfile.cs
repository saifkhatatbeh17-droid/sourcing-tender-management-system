namespace SourcingTenderManagement.Models.Entities;

/// <summary>Company-wide facts that give AI assessments a sense of scale. One row is expected.</summary>
public class CompanyProfile
{
    public int CompanyProfileId { get; set; }

    public string Name { get; set; } = null!;

    public string Industry { get; set; } = null!;

    public string Country { get; set; } = null!;

    public string Currency { get; set; } = null!;

    /// <summary>Month (1-12) the fiscal year starts.</summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    public decimal AnnualRevenue { get; set; }

    public decimal AnnualProcurementBudget { get; set; }

    public int Employees { get; set; }

    public string? Sites { get; set; }

    public string? Overview { get; set; }

    public DateTime UpdatedAt { get; set; }
}
