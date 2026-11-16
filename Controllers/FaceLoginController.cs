using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.FaceVerification;

namespace SourcingTenderManagement.Controllers;

// Post-password presentation only: no sign-in, new session state, or biometric access for anonymous users.
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FaceLoginController(FaceVerificationService verification) : Controller
{
    [HttpGet]
    public Task<IActionResult> Index(CancellationToken ct, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return ReadAsync(ct, false);
    }

    [HttpGet]
    public Task<IActionResult> Modal(CancellationToken ct) => ReadAsync(ct, true);

    private async Task<IActionResult> ReadAsync(CancellationToken ct, bool fragment)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id)) return Forbid();
        try
        {
            var model = await verification.GetAsync(id, ct);
            return fragment ? PartialView("_FaceIdModal", model) : View("Index", model);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
