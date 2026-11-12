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
            if (result.Succeeded) return RedirectToAction(nameof(GoToDashboard));
        }
        ModelState.AddModelError("", "Unable to sign in. Check your email and password, or try again later.");
        return View(model);
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
        foreach (var role in RoleNames.Primary)
            if (User.IsInRole(role)) return RedirectToAction("Dashboard", role);
        return RedirectToAction(nameof(AccessDenied));
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (User.Identity?.IsAuthenticated == true) return BadRequest("Sign out before registering a supplier.");
        if (await db.SupplierProfiles.AnyAsync(s => s.RegistrationNumber == model.RegistrationNumber))
            ModelState.AddModelError(nameof(model.RegistrationNumber), "This registration number is already registered.");
        if (!ModelState.IsValid) return View(model);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = model.Email, Email = model.Email, FullName = model.ContactName };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, RoleNames.Supplier);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View(model);
        }
        db.SupplierProfiles.Add(new SupplierProfile { UserId = user.Id, CompanyName = model.CompanyName,
            RegistrationNumber = model.RegistrationNumber, TaxNumber = model.TaxNumber,
            ContactName = model.ContactName, Address = model.Address, Status = "Pending", CreatedAt = DateTime.UtcNow });
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
        profile.Status = "Pending";
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(RegistrationStatus));
    }

    public IActionResult AccessDenied() { Response.StatusCode = 403; return View(); }
}
