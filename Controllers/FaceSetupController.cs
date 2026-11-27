using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.FaceEnrollment;
using SourcingTenderManagement.Services.FaceVerification;

namespace SourcingTenderManagement.Controllers;

// The 'set up face sign-in' prompt shown after login, which the user can accept or decline.
[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FaceSetupController(ApplicationDbContext db, FaceEnrollmentService enrollment, FaceVerificationService verification) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!await FaceSetupFlow.ShouldPromptAsync(db, User, Request, ct)) return RedirectToAction("GoToDashboard", "Account");
        try { return View(await enrollment.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet]
    public async Task<IActionResult> Modal(CancellationToken ct, string? returnUrl = null)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == null) return Forbid();
        try
        {
            ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            if (await FaceSetupFlow.ShouldPromptAsync(db, User, Request, ct))
                return PartialView("_SetupModal", await enrollment.GetAsync(id, ct));
            return PartialView("~/Views/FaceLogin/_FaceIdModal.cshtml", await verification.GetAsync(id, ct));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4_000)]
    public IActionResult Decline()
    {
        Response.Cookies.Append(FaceSetupFlow.CookieName, "1", FaceSetupFlow.CookieOptions(Request));
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Ok(new { redirectUrl = Url.Action("GoToDashboard", "Account") });
        return RedirectToAction("GoToDashboard", "Account");
    }
}
