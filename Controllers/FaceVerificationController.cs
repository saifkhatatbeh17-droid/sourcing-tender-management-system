using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Models.FaceVerification;
using SourcingTenderManagement.Services.FaceVerification;

namespace SourcingTenderManagement.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FaceVerificationController(FaceVerificationService verification) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try { return View(await verification.GetAsync(UserId, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("FaceVerification"), RequestSizeLimit(12_000)]
    public async Task<IActionResult> Verify([FromBody] FaceVerificationRequest input, CancellationToken ct)
    {
        if (!ModelState.IsValid || input == null) return BadRequest(FaceVerificationResult.Failed());
        try
        {
            var result = await verification.VerifyAsync(UserId, input, ct);
            return result.Status == "Verification Failed" ? BadRequest(result) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
