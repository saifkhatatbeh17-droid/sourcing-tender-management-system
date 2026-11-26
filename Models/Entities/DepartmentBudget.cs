namespace SourcingTenderManagement.Models.Entities;

/// <summary>A department's procurement budget for one fiscal year.</summary>
public class DepartmentBudget
{
    public int DepartmentBudgetId { get; set; }

    public int DepartmentId { get; set; }

    public int FiscalYear { get; set; }

    public string Currency { get; set; } = null!;

    public decimal AllocatedAmount { get; set; }

    /// <summary>Invoiced and paid so far this fiscal year.</summary>
    public decimal SpentToDate { get; set; }

    /// <summary>Awarded or ordered but not yet paid.</summary>
    public decimal CommittedAmount { get; set; }

    public string? Notes { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Department Department { get; set; } = null!;
}
