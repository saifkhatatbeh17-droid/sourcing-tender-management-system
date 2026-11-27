using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.Evaluation;

// Company facts, department budgets and past purchases that are given to the AI
// so its tender assessment can consider budget and history.
public sealed record CompanyFacts(string Name, string Industry, string Country, string Currency, decimal AnnualRevenue,
    decimal AnnualProcurementBudget, int Employees, string? Sites, string? Overview);

public sealed record DepartmentBudgetFacts(string Department, int FiscalYear, string Currency, decimal Allocated,
    decimal SpentToDate, decimal Committed, decimal Remaining, decimal UsedPercent, decimal FiscalYearElapsedPercent, string? Notes);

public sealed record RequestBusinessFacts(decimal? EstimatedBudget, string? BusinessImpact, decimal? DelayCostPerDay);

/// <summary>Ratios computed here so the AI cites them instead of doing arithmetic. Null when currencies differ or data is missing.</summary>
public sealed record OfferBudgetFacts(decimal OfferTotal, string OfferCurrency, decimal? PercentOfDepartmentRemaining,
    decimal? PercentOfDepartmentAllocated, decimal? PercentOfCompanyProcurementBudget, decimal? VarianceFromEstimatePercent,
    decimal? RemainingAfterAward, string? Limitation);

public sealed record PastPurchaseFacts(string ItemName, string SupplierName, decimal Quantity, decimal UnitPrice, decimal TotalAmount,
    string Currency, DateOnly PurchasedOn, int? PromisedDeliveryDays, int? ActualDeliveryDays, string? Notes);

public sealed record BusinessContext(CompanyFacts? Company, DepartmentBudgetFacts? DepartmentBudget, RequestBusinessFacts Request,
    OfferBudgetFacts? Offer, IReadOnlyList<PastPurchaseFacts> ComparablePurchases, string[] MissingData);

/// <summary>Loads company, budget and purchase-history facts for one tender's AI assessment.</summary>
public static class ProcurementBusinessContext
{
    private const int MaxComparables = 5;

    public static async Task<BusinessContext> LoadAsync(ApplicationDbContext db, int tenderId, decimal? offerTotal,
        string? offerCurrency, DateOnly today, CancellationToken cancellationToken)
    {
        var request = await db.Tenders.AsNoTracking().Where(t => t.TenderId == tenderId)
            .Select(t => new
            {
                t.PurchaseRequest.DepartmentId, Department = t.PurchaseRequest.Department.Name, t.PurchaseRequest.Title,
                t.PurchaseRequest.EstimatedBudget, t.PurchaseRequest.BusinessImpact, t.PurchaseRequest.DelayCostPerDay,
                Items = t.PurchaseRequest.PurchaseRequestItems.Select(i => i.ItemName).ToList()
            }).SingleAsync(cancellationToken);

        var profile = await db.CompanyProfiles.AsNoTracking()
            .OrderBy(c => c.CompanyProfileId)
            .FirstOrDefaultAsync(cancellationToken);
        var company = profile == null ? null : new CompanyFacts(profile.Name, profile.Industry, profile.Country, profile.Currency,
            profile.AnnualRevenue, profile.AnnualProcurementBudget, profile.Employees, profile.Sites, profile.Overview);

        var startMonth = profile?.FiscalYearStartMonth ?? 1;
        var fiscalYear = today.Month >= startMonth ? today.Year : today.Year - 1;
        var fiscalStart = new DateOnly(fiscalYear, startMonth, 1);
        var elapsed = Percent(today.DayNumber - fiscalStart.DayNumber, fiscalStart.AddYears(1).DayNumber - fiscalStart.DayNumber);

        var budget = await db.DepartmentBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.DepartmentId == request.DepartmentId
                && b.FiscalYear == fiscalYear, cancellationToken);
        var budgetFacts = budget == null ? null : new DepartmentBudgetFacts(request.Department, budget.FiscalYear, budget.Currency,
            budget.AllocatedAmount, budget.SpentToDate, budget.CommittedAmount,
            budget.AllocatedAmount - budget.SpentToDate - budget.CommittedAmount,
            Percent(budget.SpentToDate + budget.CommittedAmount, budget.AllocatedAmount) ?? 0, elapsed ?? 0, budget.Notes);

        OfferBudgetFacts? offer = null;
        if (offerTotal is decimal total && !string.IsNullOrWhiteSpace(offerCurrency))
        {
            var budgetCurrency = budgetFacts?.Currency ?? company?.Currency;
            var sameCurrency = budgetCurrency != null
                && string.Equals(budgetCurrency, offerCurrency, StringComparison.OrdinalIgnoreCase);
            offer = new OfferBudgetFacts(total, offerCurrency,
                sameCurrency && budgetFacts != null ? Percent(total, budgetFacts.Remaining) : null,
                sameCurrency && budgetFacts != null ? Percent(total, budgetFacts.Allocated) : null,
                sameCurrency && company != null ? Percent(total, company.AnnualProcurementBudget) : null,
                sameCurrency
                    && request.EstimatedBudget is > 0 ? Percent(total - request.EstimatedBudget.Value, request.EstimatedBudget.Value) : null,
                sameCurrency && budgetFacts != null ? budgetFacts.Remaining - total : null,
                budgetCurrency == null ? "No budget currency is recorded."
                    : sameCurrency ? null : $"Offer is in {offerCurrency} but the budget is in {budgetCurrency}; no conversion rate is recorded.");
        }

        var history = await db.PastPurchases.AsNoTracking().Where(p => p.DepartmentId == request.DepartmentId)
            .OrderByDescending(p => p.PurchasedOn).Take(60).ToListAsync(cancellationToken);
        var words = Keywords(string.Join(' ', request.Items.Append(request.Title)));
        var comparables = history
            .Select(p => (Purchase: p, Score: Keywords(p.ItemName + " " + p.Category).Count(words.Contains)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => x.Purchase.PurchasedOn)
            .Take(MaxComparables)
            .Select(x => new PastPurchaseFacts(x.Purchase.ItemName, x.Purchase.SupplierName, x.Purchase.Quantity, x.Purchase.UnitPrice,
                x.Purchase.TotalAmount, x.Purchase.Currency, x.Purchase.PurchasedOn, x.Purchase.PromisedDeliveryDays,
                x.Purchase.ActualDeliveryDays, x.Purchase.Notes))
            .ToList();

        var missing = new List<string>();
        if (company == null) missing.Add("Company profile");
        if (budgetFacts == null) missing.Add($"{request.Department} budget for fiscal year {fiscalYear}");
        if (request.EstimatedBudget == null) missing.Add("Requester's estimated budget");
        if (string.IsNullOrWhiteSpace(request.BusinessImpact)) missing.Add("Business impact statement");
        if (request.DelayCostPerDay == null) missing.Add("Estimated cost of delay");
        if (comparables.Count == 0) missing.Add("Comparable past purchases");

        return new BusinessContext(company, budgetFacts,
            new RequestBusinessFacts(request.EstimatedBudget, request.BusinessImpact, request.DelayCostPerDay),
            offer, comparables, missing.ToArray());
    }

    private static decimal? Percent(decimal part, decimal whole) => whole > 0 ? Math.Round(part / whole * 100, 1) : null;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "and", "for", "the", "with", "set", "kit", "demo", "request", "package", "unit", "units", "new" };

    private static HashSet<string> Keywords(string text) => text
        .Split([' ', ',', '-', '/', '(', ')', ':', '.', '&'], StringSplitOptions.RemoveEmptyEntries)
        .Select(w => w.ToLowerInvariant().TrimEnd('s'))
        .Where(w => w.Length > 3 && !StopWords.Contains(w))
        .ToHashSet();
}
