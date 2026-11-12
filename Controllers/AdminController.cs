using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.Admin)]
public class AdminController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public async Task<IActionResult> Users() => View(await LoadUsersPage());
    public async Task<IActionResult> Departments() => View(await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync());
    public async Task<IActionResult> Suppliers() => View(await db.SupplierProfiles.AsNoTracking().Include(s => s.User).OrderBy(s => s.CompanyName).ToListAsync());
    public async Task<IActionResult> SupplierApplications() => View(await db.SupplierProfiles.AsNoTracking().Where(s => s.Status != "Approved").ToListAsync());

    private async Task<UsersPageViewModel> LoadUsersPage(CreateUserViewModel? createUser = null)
    {
        ViewData["ActivePage"] = "Users";
        ViewBag.Departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        return new UsersPageViewModel
        {
            Users = await db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(),
            CreateUser = createUser ?? new CreateUserViewModel()
        };
    }

    private async Task<IActionResult> InvalidCreateUser(CreateUserViewModel model)
    {
        ViewData["OpenCreateUser"] = true;
        return View("Users", await LoadUsersPage(model));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 100) return BadRequest("Department name must be 1-100 characters.");
        if (await db.Departments.AnyAsync(d => d.Name == name)) return BadRequest("Department already exists.");
        db.Departments.Add(new SourcingTenderManagement.Models.Entities.Department { Name = name, IsActive = true });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Departments));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserViewModel model)
    {
        ViewBag.Departments = await db.Departments.Where(d => d.IsActive).ToListAsync();
        if (!RoleNames.Primary.Where(r => r != RoleNames.Supplier).Contains(model.Role)) ModelState.AddModelError(nameof(model.Role), "Choose an employee role.");
        if (model.DepartmentId.HasValue && !await db.Departments.AnyAsync(d => d.DepartmentId == model.DepartmentId && d.IsActive)) ModelState.AddModelError(nameof(model.DepartmentId), "Choose an active department.");
        if (model.Role == RoleNames.Department && model.DepartmentId == null) ModelState.AddModelError(nameof(model.DepartmentId), "A department account needs a department.");
        var positions = model.Role == RoleNames.TopCommittee ? RoleNames.TopPositions : RoleNames.EvaluationPositions;
        if (model.CommitteeRole != null && !positions.Contains(model.CommitteeRole)) ModelState.AddModelError(nameof(model.CommitteeRole), "Choose a matching committee position.");
        if (model.Role == RoleNames.TopCommittee || model.Role == RoleNames.EvaluationCommittee)
        {
            if (model.CommitteeRole == null) ModelState.AddModelError(nameof(model.CommitteeRole), "Select this member's position.");
        }
        if (model.CommitteeRole != null && model.Role != RoleNames.TopCommittee && model.Role != RoleNames.EvaluationCommittee && model.Role != RoleNames.SourcingManager)
            ModelState.AddModelError(nameof(model.CommitteeRole), "This account is not a committee member.");
        if (!ModelState.IsValid) return await InvalidCreateUser(model);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = model.Email, Email = model.Email, FullName = model.FullName, DepartmentId = model.DepartmentId, EmailConfirmed = true };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            var assignedRoles = new List<string> { model.Role };
            if (model.CommitteeRole != null)
            {
                assignedRoles.Add(model.CommitteeRole);
                if (RoleNames.EvaluationPositions.Contains(model.CommitteeRole)) assignedRoles.Add(RoleNames.EvaluationCommittee);
            }
            result = await users.AddToRolesAsync(user, assignedRoles.Distinct());
        }
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return await InvalidCreateUser(model);
        }
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewSupplier(int id, string status, string? notes)
    {
        if (!new[] { "Approved", "Rejected", "ChangesRequested", "Suspended" }.Contains(status)) return BadRequest();
        if (status != "Approved" && string.IsNullOrWhiteSpace(notes)) return BadRequest("Please provide review notes.");
        if (notes?.Length > 2000) return BadRequest();
        var supplier = await db.SupplierProfiles.FindAsync(id);
        if (supplier == null) return NotFound();
        supplier.Status = status;
        supplier.ReviewNotes = notes;
        supplier.ReviewedByUserId = users.GetUserId(User);
        supplier.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(SupplierApplications));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(EditUserViewModel model)
    {
        var user = await users.FindByIdAsync(model.Id);
        if (user == null) return NotFound();
        if (await users.IsInRoleAsync(user, RoleNames.Department) && model.DepartmentId == null)
            ModelState.AddModelError(nameof(model.DepartmentId), "A department account needs a department.");
        if (model.DepartmentId.HasValue && !await db.Departments.AnyAsync(d => d.DepartmentId == model.DepartmentId && d.IsActive))
            ModelState.AddModelError(nameof(model.DepartmentId), "Choose an active department.");
        if (model.Id == users.GetUserId(User) && !model.IsActive)
            ModelState.AddModelError(nameof(model.IsActive), "Your own administrator account must stay active.");
        if (ModelState.IsValid)
        {
            user.FullName = model.FullName;
            user.DepartmentId = model.DepartmentId;
            user.IsActive = model.IsActive;
            var result = await users.UpdateAsync(user);
            if (result.Succeeded) return RedirectToAction(nameof(Users));
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }
        ViewData["OpenEditUser"] = model.Id;
        ViewData["EditInput"] = model;
        return View("Users", await LoadUsersPage());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDepartment(EditDepartmentViewModel model)
    {
        if (!ModelState.IsValid) return BadRequest("Enter a department name of 1–100 characters.");
        var department = await db.Departments.FindAsync(model.DepartmentId);
        if (department == null) return NotFound();
        var name = model.Name.Trim();
        if (await db.Departments.AnyAsync(d => d.Name == name && d.DepartmentId != model.DepartmentId))
            return BadRequest("Department already exists.");
        department.Name = name;
        department.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Departments));
    }
}
