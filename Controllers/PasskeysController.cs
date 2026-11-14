using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Controllers;

[Route("Account/Passkeys")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PasskeysController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : Controller
{
    [HttpGet, Authorize]
    public IActionResult Index() => RedirectToAction("Index", "FaceEnrollment", null, "passkey");

    [HttpPost("EnrollmentOptions"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrollmentOptions()
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id,
            Name = user.Email ?? user.UserName ?? user.Id,
            DisplayName = user.FullName
        });
        return Content(options, "application/json");
    }

    [HttpPost("Enroll"), Authorize, ValidateAntiForgeryToken, EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> Enroll(string? credential, string? password)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();
        if (string.IsNullOrWhiteSpace(credential) || string.IsNullOrEmpty(password))
            return BadRequest(new { message = "Enter your current password and try again." });

        var proof = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!proof.Succeeded)
            return BadRequest(new { message = "Unable to confirm your password. Try again later if your account is locked." });

        var result = await signIn.PerformPasskeyAttestationAsync(credential);
        if (!result.Succeeded || result.UserEntity.Id != user.Id)
            return BadRequest(new { message = "Passkey registration could not be verified. Try again." });

        var saved = await users.AddOrUpdatePasskeyAsync(user, result.Passkey);
        return saved.Succeeded ? Ok() : BadRequest(new { message = "The passkey could not be saved." });
    }

    [HttpPost("LoginOptions"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("FaceLogin")]
    public async Task<IActionResult> LoginOptions()
    {
        if (User.Identity?.IsAuthenticated == true) return Conflict();
        var options = await signIn.MakePasskeyRequestOptionsAsync(null);
        return Content(options, "application/json");
    }

    [HttpPost("SignIn"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> SignInWithPasskey(string? credential, string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return Conflict();
        if (string.IsNullOrWhiteSpace(credential)) return BadRequest();

        var result = await signIn.PerformPasskeyAssertionAsync(credential);
        if (!result.Succeeded || !result.User.IsActive ||
            !await signIn.CanSignInAsync(result.User) || await users.IsLockedOutAsync(result.User) ||
            await users.GetTwoFactorEnabledAsync(result.User))
        {
            return Unauthorized(new { message = "Passkey sign-in failed. Try again or sign in with your password." });
        }

        var saved = await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey);
        if (!saved.Succeeded)
            return Unauthorized(new { message = "Passkey sign-in failed. Please try again." });

        await signIn.SignInAsync(result.User, isPersistent: false, authenticationMethod: "Passkey");
        return Ok(new { redirect = Url.Action("GoToDashboard", "Account", new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null }) });
    }
}
