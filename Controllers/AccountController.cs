using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Controllers;
public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        var model = new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) };
        return User.Identity?.IsAuthenticated == true ? ContinueAfterLogin(model) : View(model);
    }

    private string? SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private RedirectToActionResult ContinueAfterLogin(LoginViewModel model) =>
        RedirectToAction(nameof(GoToDashboard), new { returnUrl = SafeReturnUrl(model.ReturnUrl) });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (User.Identity?.IsAuthenticated == true) return ContinueAfterLogin(model);
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByEmailAsync(model.Email);
        if (user != null && user.IsActive)
        {
            var result = await signIn.PasswordSignInAsync(user, model.Password, false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
                    SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
                return ContinueAfterLogin(model);
            }
        }
        ModelState.AddModelError("", "Unable to sign in. Check your email and password, or try again later.");
        return View(model);
    }

    // This path must not call Login: that would issue a session before checking the face.
    [HttpPost, ValidateAntiForgeryToken,
     Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("FaceLogin"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> LoginWithFaceId(
        SourcingTenderManagement.Models.FaceLogin.FaceSignInInput model,
        [FromServices] SourcingTenderManagement.Services.FaceVerification.FaceVerificationService verification,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        const string failure = "Unable to sign in. Your password and enrolled face must both match. Check your details and try again.";
        if (User.Identity?.IsAuthenticated == true)
            return Conflict(new { message = "You are already signed in. Sign out before using Face ID." });
        if (!ModelState.IsValid || model.Password.Length > 1024 ||
            !SourcingTenderManagement.Services.FaceVerification.FaceVerificationService.ValidDescriptor(model.Descriptor) ||
            model.ModelIdentifier != SourcingTenderManagement.Services.FaceEnrollment.FaceEnrollmentService.ModelIdentifier)
            return BadRequest(new { message = failure });

        var user = await users.FindByEmailAsync(model.Email);
        if (user == null || !user.IsActive || !await signIn.CanSignInAsync(user) ||
            (users.SupportsUserLockout && await users.IsLockedOutAsync(user)))
            return BadRequest(new { message = failure });
        // Do not reset failed face attempts merely because the password is correct.
        if (!await users.CheckPasswordAsync(user, model.Password))
        {
            if (users.SupportsUserLockout && await users.GetLockoutEnabledAsync(user))
                await users.AccessFailedAsync(user);
            return BadRequest(new { message = failure });
        }
        SourcingTenderManagement.Models.FaceVerification.FaceVerificationResult match;
        try
        {
            match = await verification.VerifyAsync(user.Id, new()
            {
                Descriptor = model.Descriptor, ModelIdentifier = model.ModelIdentifier
            }, ct);
        }
        catch (UnauthorizedAccessException) { return BadRequest(new { message = failure }); }
        if (match.Status != "Matched")
        {
            // Wrong-face attempts share the account's Identity lockout protection.
            if (users.SupportsUserLockout && await users.GetLockoutEnabledAsync(user))
                await users.AccessFailedAsync(user);
            return BadRequest(new { message = failure, code = match.Status == "Not Matched" ? "face_not_matched" : "face_unavailable" });
        }

        // Recheck account/password/lockout immediately before issuing the cookie;
        // preserve Identity's existing two-factor requirements if configured.
        user = await users.FindByIdAsync(user.Id);
        if (user == null || !user.IsActive ||
            !(await signIn.PasswordSignInAsync(user, model.Password, false, lockoutOnFailure: true)).Succeeded)
            return BadRequest(new { message = failure });
        Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
            SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
        return Ok(new { redirectUrl = Url.Action(nameof(GoToDashboard), new { returnUrl = SafeReturnUrl(model.ReturnUrl) }) });
    }

    [Authorize]
    public async Task<IActionResult> GoToDashboard(string? returnUrl = null)
    {
        if (User.IsInRole(RoleNames.Supplier))
        {
            var userId = users.GetUserId(User);
            if (!await db.SupplierProfiles.AnyAsync(s => s.UserId == userId && s.Status == "Approved"))
                return RedirectToAction(nameof(RegistrationStatus));
        }
        if (SafeReturnUrl(returnUrl) is { } destination)
            return LocalRedirect(destination);
        if (await SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.ShouldPromptAsync(db, User, Request))
            return RedirectToAction("Index", "FaceSetup");
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
        var profile = new SupplierProfile { UserId = user.Id, CompanyName = model.CompanyName,
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
        return View(new SupplierCompanyViewModel { CompanyName = profile.CompanyName,
            RegistrationNumber = profile.RegistrationNumber, TaxNumber = profile.TaxNumber,
            ContactName = profile.ContactName, Address = profile.Address });
    }

    [HttpPost, Authorize(Roles = RoleNames.Supplier), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRegistration(SupplierCompanyViewModel input)
    {
        var id = users.GetUserId(User);
        var profile = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == id);
        if (profile?.Status != "ChangesRequested") return Forbid();
        if (!ModelState.IsValid) return View(input);
        if (await db.SupplierProfiles.AnyAsync(s => s.RegistrationNumber == input.RegistrationNumber && s.UserId != id))
            return BadRequest("This registration number is already registered.");
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
}
