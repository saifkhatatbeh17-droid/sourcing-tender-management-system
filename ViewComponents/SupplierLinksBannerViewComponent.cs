using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.Integrity;

namespace SourcingTenderManagement.ViewComponents;

/// <summary>
/// Warning banner for one tender's offers. Renders nothing when no two suppliers on the tender look linked.
/// Callers place it only on pages already authorised for that tender (Sourcing Officer, Top Committee).
/// </summary>
public sealed class SupplierLinksBannerViewComponent(SupplierLinks links) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int tenderId, string advice)
    {
        var found = await links.ForTenderAsync(tenderId, HttpContext.RequestAborted);
        if (found.Count == 0) return Content("");
        ViewData["TenderId"] = tenderId;
        ViewData["Advice"] = advice;
        return View(found);
    }
}
