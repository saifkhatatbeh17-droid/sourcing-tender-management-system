using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace SourcingTenderManagement.ViewComponents;

// View component: a small QR code button that opens the details page of a record on a phone.
public sealed class DetailsQrViewComponent(IConfiguration configuration) : ViewComponent
{
    public IViewComponentResult Invoke(string controller, string action, int id, string label, bool compact = false)
    {
        var detailsUrl = Url.Action(action, controller, new { id });
        if (detailsUrl is null)
            return Content(string.Empty);

        var origin = new Uri($"{Request.Scheme}://{Request.Host.ToUriComponent()}");
        if (Uri.TryCreate(configuration["Links:PublicOrigin"], UriKind.Absolute, out var publicOrigin)
            && publicOrigin.Scheme is "http" or "https"
            && publicOrigin.Host.Length > 0 && publicOrigin.UserInfo.Length == 0
            && publicOrigin.AbsolutePath == "/" && publicOrigin.Query.Length == 0 && publicOrigin.Fragment.Length == 0)
        {
            origin = publicOrigin;
        }

        var scanUrl = new Uri(origin, detailsUrl).AbsoluteUri;
        using var data = QRCodeGenerator.GenerateQrCode(scanUrl, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var imageData = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(pixelsPerModule: 8));

        return View(new DetailsQrViewModel(label, detailsUrl, scanUrl, imageData, origin.IsLoopback, compact));
    }
}

public sealed record DetailsQrViewModel(string Label, string DetailsUrl, string ScanUrl, string ImageData, bool IsLoopback, bool Compact = false);
