using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.ViewComponents;

// View component: shows the product photos attached to one offer.
public sealed class OfferProductPhotosViewComponent(OfferProductPhotos photos) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bidId) => View(await photos.ListAsync(bidId, HttpContext.User));
}
