using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.ViewComponents;

public sealed class OfferGuaranteesViewComponent(ApplicationDbContext db, OfferProductPhotos access) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bidId)
    {
        if (!await access.VisibleBids(UserClaimsPrincipal).AnyAsync(b => b.BidId == bidId))
            return Content("");
        var guarantees = await db.BidGuarantees.AsNoTracking().Where(g => g.BidId == bidId)
            .OrderBy(g => g.Kind).ToListAsync();
        return View(guarantees);
    }
}
