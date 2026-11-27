using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models.FaceEnrollment;
using SourcingTenderManagement.Services.FaceEnrollment;

namespace SourcingTenderManagement.Controllers;

// Lets a signed-in user register (or remove) their face for face sign-in.
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FaceEnrollmentController(FaceEnrollmentService enrollment) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try { return View(await enrollment.GetAsync(UserId, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("FaceEnrollment"), RequestSizeLimit(48_000)]
    public Task<IActionResult> Save([FromBody] FaceEnrollmentRequest input, CancellationToken ct) => Change(input, false, ct);

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("FaceEnrollment"), RequestSizeLimit(48_000)]
    public Task<IActionResult> Delete([FromBody] FaceEnrollmentRequest input, CancellationToken ct) => Change(input, true, ct);

    private async Task<IActionResult> Change(FaceEnrollmentRequest input, bool delete, CancellationToken ct)
    {
        if (!ModelState.IsValid || input == null) return BadRequest(new { message = "Invalid request. Check the form and try again." });
        try
        {
            await enrollment.SaveAsync(UserId, input, delete, ct);
            return Ok(new { message = delete ? "Face enrollment removed." : "Face enrollment saved." });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ValidationException error) { return BadRequest(new { message = error.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Enrollment changed. Reload this page before trying again." }); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { return Conflict(new { message = "Enrollment changed. Reload this page before trying again." }); }
    }
}
