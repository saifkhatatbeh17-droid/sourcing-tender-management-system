using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.ViewComponents;

public sealed class OfferProductPhotosViewComponent(OfferProductPhotos photos) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bidId) => View(await photos.ListAsync(bidId, HttpContext.User));
}
