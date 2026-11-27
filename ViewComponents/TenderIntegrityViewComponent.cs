using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.Integrity;

namespace SourcingTenderManagement.ViewComponents;

/// <summary>One line about the suppliers on a tender. Null means no two suppliers look linked.</summary>
public sealed record SupplierCheck(string Level, int Pairs, IReadOnlyList<string> Names, IReadOnlyList<string> Reasons, string Url);

public sealed record TenderIntegrityModel(int TenderId, SupplierCheck? Suppliers);

/// <summary>
/// Compact "Integrity checks" card for a tender page: whether any suppliers on the tender look linked, with a link to the
/// full supplier map on AI Intelligence. Callers place it only on pages already authorised for the tender.
/// </summary>
public sealed class TenderIntegrityViewComponent(SupplierLinks links) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int tenderId, string workspace)
    {
        var found = await links.ForTenderAsync(tenderId, HttpContext.RequestAborted);
        if (found.Count == 0) return View(new TenderIntegrityModel(tenderId, null));

        var top = found[0];
        var names = found.SelectMany(l => new[] { l.NameA, l.NameB }).Distinct().ToList();
        var reasons = found.SelectMany(l => l.Evidence)
            .Select(e => e.Kind)
            .Distinct()
            .Take(3)
            .Select(SupplierLinks.KindLabel)
            .ToList();
        var suppliers = new SupplierCheck(
            found.Any(l => l.Level == SupplierLinks.High) ? SupplierLinks.High : SupplierLinks.Medium,
            found.Count, names, reasons,
            $"/{workspace}/AiIntelligence?pair={top.SupplierA}-{top.SupplierB}&from={tenderId}#supplier-links");
        return View(new TenderIntegrityModel(tenderId, suppliers));
    }
}
