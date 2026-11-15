using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using QRCoder;

namespace SourcingTenderManagement.ViewComponents;

public sealed class DetailsQrViewComponent(IConfiguration configuration) : ViewComponent
{
    public IViewComponentResult Invoke(string controller, string action, int id, string label,
        string title, string status, string? description)
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

        var scanUrl = QueryHelpers.AddQueryString(new Uri(origin, detailsUrl).AbsoluteUri, "scan", "true");
        using var data = QRCodeGenerator.GenerateQrCode(scanUrl, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var imageData = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(pixelsPerModule: 8));

        return View(new DetailsQrViewModel(label, title, status, description, detailsUrl, scanUrl, imageData, origin.IsLoopback));
    }
}

public sealed record DetailsQrViewModel(string Label, string Title, string Status, string? Description,
    string DetailsUrl, string ScanUrl, string ImageData, bool IsLoopback);
