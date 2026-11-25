using SourcingTenderManagement.Services.Reports.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using Microsoft.AspNetCore.RateLimiting;
namespace SourcingTenderManagement.Controllers;
public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db, IAuditLog? audit = null) : Controller
{
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenNotification(int id, CancellationToken ct)
    {
        var userId = users.GetUserId(User);
        if (userId == null) return Forbid();
        var items = db.Notifications.Where(n => n.NotificationId == id && n.UserId == userId);
        var destination = await items.Select(n => n.RelativeUrl).SingleOrDefaultAsync(ct);
        if (!Url.IsLocalUrl(destination)) return NotFound();
        await items.Where(n => !n.IsRead).ExecuteUpdateAsync(update => update.SetProperty(n => n.IsRead, true), ct);
        return LocalRedirect(destination!);
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        var model = new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) };
        return User.Identity?.IsAuthenticated == true ? ContinueAfterLogin(model) : View(model);
    }

    private string? SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private RedirectToActionResult ContinueAfterLogin(LoginViewModel model) =>
        RedirectToAction(nameof(GoToDashboard), new { returnUrl = SafeReturnUrl(model.ReturnUrl) });

    private bool IsAsyncLogin => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    private IActionResult PasswordLoginSuccess(LoginViewModel model) => IsAsyncLogin
        ? Ok(new { redirectUrl = Url.Action(nameof(GoToDashboard), new { returnUrl = SafeReturnUrl(model.ReturnUrl) }) })
        : ContinueAfterLogin(model);

    private IActionResult PasswordLoginFailure(LoginViewModel model, int statusCode) => IsAsyncLogin
        ? StatusCode(statusCode, new
        {
            message = "Unable to sign in. Check your email and password, or try again later.",
            errors = ModelState.Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray())
        })
        : View("Login", model);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        Response.Headers.CacheControl = "no-store";
        if (User.Identity?.IsAuthenticated == true) return PasswordLoginSuccess(model);
        if (!ModelState.IsValid) return PasswordLoginFailure(model, StatusCodes.Status400BadRequest);
        var user = await users.FindByEmailAsync(model.Email);
        if (user != null && user.IsActive)
        {
            var result = await signIn.PasswordSignInAsync(user, model.Password, false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
                    SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
                if (audit is not null)
                {
                    HttpContext.User = await signIn.CreateUserPrincipalAsync(user);
                    await audit.WriteAsync("Account.SignInSuccess", "ApplicationUser", user.Id, "Password sign-in succeeded.");
                }
                return PasswordLoginSuccess(model);
            }
        }
        if (audit is not null)
            await audit.WriteAsync("Account.SignInFailure", "ApplicationUser", user?.Id ?? "", "Password sign-in failed.");
        ModelState.AddModelError("", "Unable to sign in. Check your email and password, or try again later.");
        return PasswordLoginFailure(model, StatusCodes.Status401Unauthorized);
    }

    // This path must not call Login: that would issue a session before checking the face.
    [HttpPost, ValidateAntiForgeryToken,
     Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> LoginWithFaceId(
        SourcingTenderManagement.Models.FaceEnrollment.FaceSignInInput model,
        [FromServices] SourcingTenderManagement.Services.FaceEnrollment.FaceVerificationService verification,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        const string failure = "Unable to sign in. Your password and enrolled face must both match. Check your details and try again.";
        async Task<IActionResult> RecordFailure(string? userId = null, string? code = null)
        {
            if (audit is not null) await audit.WriteAsync("Account.SignInFailure", "ApplicationUser", userId ?? "", "Face sign-in failed.", ct: ct);
            return BadRequest(new { message = failure, code });
        }
        if (User.Identity?.IsAuthenticated == true)
            return Conflict(new { message = "You are already signed in. Sign out before using Face ID." });
        if (!ModelState.IsValid || model.Password.Length > 1024 ||
            !SourcingTenderManagement.Services.FaceEnrollment.FaceVerificationService.ValidDescriptor(model.Descriptor) ||
            model.ModelIdentifier != SourcingTenderManagement.Services.FaceEnrollment.FaceEnrollmentService.ModelIdentifier)
            return await RecordFailure();

        var user = await users.FindByEmailAsync(model.Email);
        if (user == null || !user.IsActive || !await signIn.CanSignInAsync(user) ||
            (users.SupportsUserLockout && await users.IsLockedOutAsync(user)))
            return await RecordFailure();
        // Do not reset failed face attempts merely because the password is correct.
        if (!await users.CheckPasswordAsync(user, model.Password))
        {
            if (users.SupportsUserLockout && await users.GetLockoutEnabledAsync(user))
                await users.AccessFailedAsync(user);
            return await RecordFailure();
        }
        SourcingTenderManagement.Models.FaceEnrollment.FaceVerificationResult match;
        try
        {
            match = await verification.VerifyAsync(user.Id, new()
            {
                Descriptor = model.Descriptor, ModelIdentifier = model.ModelIdentifier
            }, ct);
        }
        catch (UnauthorizedAccessException) { return await RecordFailure(); }
        if (match.Status != "Matched")
        {
            // Wrong-face attempts share the account's Identity lockout protection.
            if (users.SupportsUserLockout && await users.GetLockoutEnabledAsync(user))
                await users.AccessFailedAsync(user);
            return await RecordFailure(user.Id, match.Status == "Not Matched" ? "face_not_matched" : "face_unavailable");
        }

        // Recheck account/password/lockout immediately before issuing the cookie;
        // preserve Identity's existing two-factor requirements if configured.
        user = await users.FindByIdAsync(user.Id);
        if (user == null || !user.IsActive ||
            !(await signIn.PasswordSignInAsync(user, model.Password, false, lockoutOnFailure: true)).Succeeded)
            return await RecordFailure();
        if (audit is not null)
        {
            HttpContext.User = await signIn.CreateUserPrincipalAsync(user);
            await audit.WriteAsync("Account.SignInSuccess", "ApplicationUser", user.Id, "Face sign-in succeeded.", ct: ct);
        }
        Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
            SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
        return Ok(new { redirectUrl = Url.Action(nameof(GoToDashboard), new { returnUrl = SafeReturnUrl(model.ReturnUrl) }) });
    }

    [HttpPost, ValidateAntiForgeryToken,
     Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> DemoFaceSignIn(
        SourcingTenderManagement.Models.FaceEnrollment.FaceDemoSignInInput model,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] SourcingTenderManagement.Services.FaceEnrollment.FaceVerificationService verification,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!environment.IsDevelopment()) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return Conflict(new { message = "Already signed in." });
        if (!ModelState.IsValid || !SourcingTenderManagement.Services.FaceEnrollment.FaceVerificationService.ValidDescriptor(model.Descriptor)
            || model.ModelIdentifier != SourcingTenderManagement.Services.FaceEnrollment.FaceEnrollmentService.ModelIdentifier)
            return BadRequest(new { message = "Invalid camera sample." });

        var requestedUser = await users.FindByEmailAsync(model.Email.Trim());
        var enrolledIds = requestedUser == null ? new List<string>() : await db.UserFaceCredentials.AsNoTracking()
            .Where(x => x.UserId == requestedUser.Id && x.ProtectedTemplates != null).Select(x => x.UserId).ToListAsync(ct);
        ApplicationUser? matched = null;
        foreach (var id in enrolledIds)
        {
            var candidate = await users.FindByIdAsync(id);
            if (candidate == null || !candidate.IsActive || !await signIn.CanSignInAsync(candidate)
                || (users.SupportsUserLockout && await users.IsLockedOutAsync(candidate))
                || await users.GetTwoFactorEnabledAsync(candidate)) continue;
            try
            {
                var result = await verification.VerifyAsync(id, new() { Descriptor = model.Descriptor, ModelIdentifier = model.ModelIdentifier }, ct);
                if (result.Status != "Matched") continue;
                // Never choose an arbitrary account when more than one enrollment matches.
                if (matched != null) return BadRequest(new { message = "Multiple accounts matched. Use password sign-in.", code = "face_ambiguous" });
                matched = candidate;
            }
            catch (UnauthorizedAccessException) { }
        }
        if (matched == null) return BadRequest(new { message = "No available enrolled account matched your face.", code = "face_not_matched" });
        matched = await users.FindByIdAsync(matched.Id);
        if (matched == null || !matched.IsActive || !await signIn.CanSignInAsync(matched)
            || (users.SupportsUserLockout && await users.IsLockedOutAsync(matched))
            || await users.GetTwoFactorEnabledAsync(matched)) return BadRequest(new { message = "Account unavailable." });
        await signIn.SignInAsync(matched, isPersistent: false, authenticationMethod: "LocalDemoFace");
        if (audit is not null)
        {
            HttpContext.User = await signIn.CreateUserPrincipalAsync(matched);
            await audit.WriteAsync("Account.SignInSuccess", "ApplicationUser", matched.Id, "Local face sign-in succeeded.", ct: ct);
        }
        return Ok(new { redirectUrl = Url.Action(nameof(GoToDashboard), new { returnUrl = SafeReturnUrl(model.ReturnUrl) }) });
    }

    [Authorize]
    public async Task<IActionResult> GoToDashboard(string? returnUrl = null)
    {
        if (User.IsInRole(RoleNames.Supplier))
        {
            var userId = users.GetUserId(User);
            if (!await db.SupplierProfiles.AnyAsync(s => s.UserId == userId && (s.Status == "Approved" || s.Status == "Suspended")))
                return RedirectToAction(nameof(RegistrationStatus));
        }
        if (SafeReturnUrl(returnUrl) is { } destination)
            return LocalRedirect(destination);
        if (await SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.ShouldPromptAsync(db, User, Request))
            return RedirectToAction("Setup", "FaceEnrollment");
        foreach (var role in RoleNames.Primary)
            if (User.IsInRole(role)) return RedirectToAction("Dashboard", role);
        return RedirectToAction(nameof(AccessDenied));
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Profile()
    {
        var userId = users.GetUserId(User);
        var account = await db.Users.AsNoTracking()
            .Include(user => user.Department)
            .SingleOrDefaultAsync(user => user.Id == userId && user.IsActive);
        if (account == null)
        {
            return Challenge();
        }

        var roles = await users.GetRolesAsync(account);
        if (User.IsInRole(RoleNames.Supplier) && roles.Contains(RoleNames.Supplier, StringComparer.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(SupplierController.Profile), "Supplier");
        }

        var profile = new AccountProfileViewModel(
            account.FullName, account.Email, account.PhoneNumber, account.Department?.Name,
            roles.OrderBy(role => role).ToList(), account.UserName,
            account.Department?.IsActive);

        return View(profile);
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> EditProfile()
    {
        var account = await users.GetUserAsync(User);
        if (account == null || !account.IsActive)
        {
            return Challenge();
        }

        return View(new EditProfileViewModel { PhoneNumber = account.PhoneNumber });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile(EditProfileViewModel input)
    {
        var account = await users.GetUserAsync(User);
        if (account == null || !account.IsActive)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var phoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
        if (account.PhoneNumber != phoneNumber)
        {
            var result = await users.SetPhoneNumberAsync(account, phoneNumber);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }

                return View(input);
            }

            await signIn.RefreshSignInAsync(account);
        }

        TempData["ProfileFeedback"] = "Phone number saved.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet, Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ChangePassword() => RedirectToProfileSecurity();

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ChangePassword")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel input)
    {
        var account = await users.GetUserAsync(User);
        if (account == null || !account.IsActive)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            TempData["PasswordChangeErrors"] = ModelState.Values
                .SelectMany(field => field.Errors)
                .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                    ? "Check the password fields and try again."
                    : error.ErrorMessage)
                .Distinct()
                .ToArray();
            return RedirectToProfileSecurity();
        }

        var result = await users.ChangePasswordAsync(account, input.CurrentPassword, input.NewPassword);
        if (!result.Succeeded)
        {
            TempData["PasswordChangeErrors"] = result.Errors.Select(error => error.Description).ToArray();
            return RedirectToProfileSecurity();
        }

        // A temporary password from the account import is now replaced; lift the forced-change restriction.
        var temporary = (await users.GetClaimsAsync(account))
            .Where(claim => claim.Type == SourcingTenderManagement.Services.Admin.EmployeeAccounts.PasswordChangeRequiredClaim)
            .ToList();
        if (temporary.Count > 0) await users.RemoveClaimsAsync(account, temporary);

        await signIn.RefreshSignInAsync(account);
        TempData["PasswordChangeSuccess"] = "Password changed successfully.";
        return RedirectToProfileSecurity();
    }

    private RedirectToActionResult RedirectToProfileSecurity() =>
        RedirectToAction(nameof(Profile), User.IsInRole(RoleNames.Supplier) ? "Supplier" : "Account",
            routeValues: null, fragment: "profile-security");

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await signIn.SignOutAsync();
        Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
            SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
        if (SafeReturnUrl(returnUrl) is { } destination)
            return RedirectToAction(nameof(Login), new { returnUrl = destination });
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Register(RegisterViewModel model, List<IFormFile>? officialDocuments)
    {
        if (User.Identity?.IsAuthenticated == true) return BadRequest("Sign out before registering a supplier.");
        // Trim and collapse spaces first so the duplicate check and saved values are consistent.
        model.Normalize();
        if (model.PhoneNumber.Count(char.IsDigit) is < 7 or > 15)
            ModelState.AddModelError(nameof(model.PhoneNumber), "Enter a phone number with 7–15 digits, for example +962 7X XXX XXXX.");
        if (await db.SupplierProfiles.AnyAsync(s => s.RegistrationNumber == model.RegistrationNumber))
            ModelState.AddModelError(nameof(model.RegistrationNumber), "This registration number is already registered.");
        if (officialDocuments == null || officialDocuments.Count == 0)
            ModelState.AddModelError(nameof(officialDocuments), "Upload at least one official company document.");
        if (officialDocuments?.Any(f => f.Length == 0 || f.Length > 10_000_000 || !new[] { ".pdf", ".docx", ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())) == true)
            ModelState.AddModelError(nameof(officialDocuments), "Documents must be PDF, DOCX, JPG, or PNG and no larger than 10 MB each.");
        if (!ModelState.IsValid) return View(model);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = model.Email, Email = model.Email, PhoneNumber = model.PhoneNumber, FullName = model.ContactName };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, RoleNames.Supplier);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View(model);
        }
        var profile = new SupplierProfile { UserId = user.Id, CompanyName = model.CompanyName, Category = model.SelectedCategory,
            RegistrationNumber = model.RegistrationNumber, TaxNumber = model.TaxNumber,
            ContactName = model.ContactName, Address = model.Address, Status = "Pending", CreatedAt = DateTime.UtcNow };
        db.SupplierProfiles.Add(profile);
        await db.SaveChangesAsync();
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "private-files", "suppliers"); Directory.CreateDirectory(folder);
        foreach (var file in officialDocuments!)
        {
            var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
            await using var stream = System.IO.File.Create(Path.Combine(folder, stored)); await file.CopyToAsync(stream);
            db.SupplierDocuments.Add(new SupplierDocument { SupplierProfileId = profile.SupplierProfileId,
                DocumentType = "Official company document", OriginalFileName = Path.GetFileName(file.FileName), StoredFileName = stored,
                ContentType = file.ContentType, FileSizeBytes = file.Length, UploadedAt = DateTime.UtcNow });
        }
        foreach (var admin in (await users.GetUsersInRoleAsync(RoleNames.Admin)).Where(u => u.IsActive))
            db.Notifications.Add(new Notification { UserId = admin.Id, Title = "Supplier application received", Message = $"{profile.CompanyName} submitted a supplier registration and official documents for review.", RelativeUrl = "/Admin/SupplierApplications", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await signIn.SignInAsync(user, false);
        return RedirectToAction(nameof(RegistrationStatus));
    }

    [Authorize(Roles = RoleNames.Supplier)]
    public async Task<IActionResult> RegistrationStatus()
    {
        var id = users.GetUserId(User);
        return View(await db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == id));
    }

    [Authorize(Roles = RoleNames.Supplier)]
    public async Task<IActionResult> EditRegistration()
    {
        var id = users.GetUserId(User);
        var profile = await db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == id);
        if (profile?.Status != "ChangesRequested") return Forbid();
        var model = new SupplierCompanyViewModel { CompanyName = profile.CompanyName,
            RegistrationNumber = profile.RegistrationNumber, TaxNumber = profile.TaxNumber,
            ContactName = profile.ContactName, Address = profile.Address };
        model.LoadCategory(profile.Category);
        return View(model);
    }

    [HttpPost, Authorize(Roles = RoleNames.Supplier), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRegistration(SupplierCompanyViewModel input)
    {
        var id = users.GetUserId(User);
        var profile = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == id);
        if (profile?.Status != "ChangesRequested") return Forbid();
        input.NormalizeCompany();
        if (await db.SupplierProfiles.AnyAsync(s => s.RegistrationNumber == input.RegistrationNumber && s.UserId != id))
            ModelState.AddModelError(nameof(input.RegistrationNumber), "This registration number is already registered.");
        if (!ModelState.IsValid) return View(input);
        profile.Category = input.SelectedCategory;
        profile.CompanyName = input.CompanyName; profile.RegistrationNumber = input.RegistrationNumber;
        profile.TaxNumber = input.TaxNumber; profile.ContactName = input.ContactName; profile.Address = input.Address;
        profile.Status = "Pending"; profile.ReviewNotes = null;
        await db.SaveChangesAsync();
        foreach (var admin in (await users.GetUsersInRoleAsync(RoleNames.Admin)).Where(u => u.IsActive))
            db.Notifications.Add(new Notification { UserId = admin.Id, Title = "Supplier application resubmitted", Message = $"{profile.CompanyName} updated its company details and resubmitted the application for review.", RelativeUrl = "/Admin/SupplierApplications", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(RegistrationStatus));
    }

    public IActionResult AccessDenied(string? returnUrl = null)
    {
        Response.StatusCode = 403;
        return View(model: SafeReturnUrl(returnUrl));
    }

    // Passkeys (/Account/Passkeys/...).
    [HttpGet("/Account/Passkeys"), Authorize]
    public IActionResult Passkeys() => RedirectToAction("Index", "FaceEnrollment", null, "passkey");

    [HttpPost("/Account/Passkeys/EnrollmentOptions"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyEnrollmentOptions()
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

    [HttpPost("/Account/Passkeys/Enroll"), Authorize, ValidateAntiForgeryToken, EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> PasskeyEnroll(string? credential, string? password)
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

    [HttpPost("/Account/Passkeys/LoginOptions"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("FaceLogin")]
    public async Task<IActionResult> PasskeyLoginOptions()
    {
        if (User.Identity?.IsAuthenticated == true) return Conflict();
        var options = await signIn.MakePasskeyRequestOptionsAsync(null);
        return Content(options, "application/json");
    }

    [HttpPost("/Account/Passkeys/SignIn"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> SignInWithPasskey(string? credential, string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return Conflict();
        if (string.IsNullOrWhiteSpace(credential)) return BadRequest();

        PasskeyAssertionResult<ApplicationUser> result;
        try
        {
            result = await signIn.PerformPasskeyAssertionAsync(credential);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No passkey assertion is underway.", StringComparison.Ordinal))
        {
            if (audit is not null) await audit.WriteAsync("Account.SignInFailure", "ApplicationUser", "", "Passkey sign-in expired.");
            return Unauthorized(new { message = "Your passkey sign-in attempt expired. Please start again or sign in with your password." });
        }
        if (!result.Succeeded || !result.User.IsActive ||
            !await signIn.CanSignInAsync(result.User) || await users.IsLockedOutAsync(result.User) ||
            await users.GetTwoFactorEnabledAsync(result.User))
        {
            if (audit is not null) await audit.WriteAsync("Account.SignInFailure", "ApplicationUser", "", "Passkey sign-in failed.");
            return Unauthorized(new { message = "Passkey sign-in failed. Try again or sign in with your password." });
        }

        var saved = await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey);
        if (!saved.Succeeded)
            return Unauthorized(new { message = "Passkey sign-in failed. Please try again." });

        await signIn.SignInAsync(result.User, isPersistent: false, authenticationMethod: "Passkey");
        if (audit is not null)
        {
            HttpContext.User = await signIn.CreateUserPrincipalAsync(result.User);
            await audit.WriteAsync("Account.SignInSuccess", "ApplicationUser", result.User.Id, "Passkey sign-in succeeded.");
        }
        return Ok(new { redirect = Url.Action("GoToDashboard", "Account", new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null }) });
    }
}
