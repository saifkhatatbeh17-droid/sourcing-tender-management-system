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
    public IActionResult Login() => User.Identity?.IsAuthenticated == true
        ? RedirectToAction(nameof(GoToDashboard))
        : View(new LoginViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(GoToDashboard));
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByEmailAsync(model.Email);
        if (user != null && user.IsActive)
        {
            var result = await signIn.PasswordSignInAsync(user, model.Password, false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
                    SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
                return RedirectToAction(nameof(GoToDashboard));
            }
        }
        ModelState.AddModelError("", "Unable to sign in. Check your email and password, or try again later.");
        return View(model);
    }

    // Password authentication is the existing Login action; the face check is optional and post-login.
    [HttpPost, ValidateAntiForgeryToken,
     Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("FaceLogin"), RequestSizeLimit(16_000)]
    public async Task<IActionResult> LoginWithFaceId(LoginViewModel model)
    {
        var inline = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        var result = await Login(model);
        if (result is RedirectToActionResult { ActionName: nameof(GoToDashboard) })
        {
            if (inline) return Ok(new { modalUrl = Url.Action("Modal", "FaceSetup") });
            return RedirectToAction("Index", "FaceLogin");
        }
        if (inline) return BadRequest(new { message = "Unable to sign in. Check your email and password, or try again later." });
        // Preserve the current Login validation and generic errors under the original view name.
        if (result is ViewResult view) view.ViewName = nameof(Login);
        return result;
    }

    [Authorize]
    public async Task<IActionResult> GoToDashboard()
    {
        if (User.IsInRole(RoleNames.Supplier))
        {
            var userId = users.GetUserId(User);
            if (!await db.SupplierProfiles.AnyAsync(s => s.UserId == userId && s.Status == "Approved"))
                return RedirectToAction(nameof(RegistrationStatus));
        }
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
            roles.OrderBy(role => role).ToList());

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

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        Response.Cookies.Delete(SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieName,
            SourcingTenderManagement.Services.FaceEnrollment.FaceSetupFlow.CookieOptions(Request));
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

    public IActionResult AccessDenied() { Response.StatusCode = 403; return View(); }
}
