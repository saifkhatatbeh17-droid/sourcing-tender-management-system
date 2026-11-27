using SourcingTenderManagement.Services.Reports.Data;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Admin;
namespace SourcingTenderManagement.Controllers;
// The Admin workspace: dashboard, users, departments and supplier accounts.
// This class is split into several files (AdminController.*.cs); each file holds one area of the admin pages.
[Authorize(Roles = RoleNames.Admin)]
public partial class AdminController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IConfiguration configuration, IWebHostEnvironment environment) : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        ViewBag.DashboardSupplierStates = await db.SupplierProfiles.AsNoTracking().GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var model = new AdminDashboardViewModel
        {
            UsersCount = await db.Users.CountAsync(cancellationToken),
            SuppliersCount = await db.SupplierProfiles.CountAsync(cancellationToken),
            DepartmentsCount = await db.Departments.CountAsync(cancellationToken),
            PurchaseRequestsCount = await db.PurchaseRequests.CountAsync(cancellationToken),
            TendersCount = await db.Tenders.CountAsync(cancellationToken),
            PendingApplications = await db.SupplierProfiles
                .Where(supplier => supplier.Status == "Pending")
                .OrderByDescending(supplier => supplier.CreatedAt)
                .ThenByDescending(supplier => supplier.SupplierProfileId)
                .Take(5)
                .Select(supplier => new PendingSupplierApplicationViewModel(
                    supplier.SupplierProfileId, supplier.CompanyName, supplier.CreatedAt))
                .ToListAsync(cancellationToken)
        };

        return View(model);
    }
    public async Task<IActionResult> Users(string? search, string? role, bool? isActive) =>
        View(await LoadUsersPage(search: search, role: role, isActive: isActive));
    public async Task<IActionResult> Departments() => View(await LoadDepartmentsPage());

    private async Task<DepartmentsPageViewModel> LoadDepartmentsPage(
        string? createName = null, bool openCreate = false, EditDepartmentViewModel? editInput = null)
    {
        ViewData["ActivePage"] = nameof(Departments);
        return new DepartmentsPageViewModel
        {
            Departments = await db.Departments.AsNoTracking().OrderBy(department => department.Name).ToListAsync(),
            CreateName = createName,
            OpenCreate = openCreate,
            EditInput = editInput
        };
    }
    public Task<IActionResult> Suppliers(string? status) => LoadSupplierList(status, applicationsOnly: false);

    public Task<IActionResult> SupplierApplications(string? status) => LoadSupplierList(status, applicationsOnly: true);

    [HttpGet]
    public async Task<IActionResult> ExportSuppliers(string? status, CancellationToken cancellationToken)
    {
        var suppliers = db.SupplierProfiles.AsNoTracking();
        if (SupplierListViewModel.Decisions.Contains(status))
            suppliers = suppliers.Where(supplier => supplier.Status == status);

        var rows = await (
            from supplier in suppliers
            from document in supplier.SupplierDocuments.DefaultIfEmpty()
            orderby supplier.CompanyName, supplier.SupplierProfileId, document.DocumentType, document.SupplierDocumentId
            select new
            {
                supplier.CompanyName,
                supplier.RegistrationNumber,
                supplier.Status,
                DocumentType = document == null ? null : document.DocumentType,
                ExpiresOn = document == null ? null : document.ExpiresOn
            }).ToListAsync(cancellationToken);

        var csv = new StringBuilder("Company,Registration number,Status,Document type,Expires on\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(",", CsvField(row.CompanyName), CsvField(row.RegistrationNumber),
                CsvField(row.Status), CsvField(row.DocumentType),
                CsvField(row.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            csv.Append("\r\n");
        }

        return File(Encoding.UTF8.GetBytes("\uFEFF" + csv.ToString()), "text/csv; charset=utf-8", "supplier-report.csv");
    }

    private static string CsvField(string? value)
    {
        value ??= "";
        // Prevent spreadsheet applications from interpreting user-entered data as a formula.
        var trimmed = value.TrimStart();
        if (value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n') ||
            (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠'))
            value = "'" + value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private async Task<IActionResult> LoadSupplierList(string? status, bool applicationsOnly)
    {
        var statuses = applicationsOnly
            ? SupplierListViewModel.Decisions.Where(value => value != "Approved").ToArray()
            : SupplierListViewModel.Decisions;
        status = statuses.Contains(status) ? status : null;

        var query = db.SupplierProfiles.AsNoTracking().Include(supplier => supplier.User).AsQueryable();
        if (applicationsOnly) query = query.Where(supplier => supplier.Status != "Approved");
        if (status != null) query = query.Where(supplier => supplier.Status == status);

        var suppliers = applicationsOnly
            ? await query.OrderByDescending(supplier => supplier.Status == "Pending")
                .ThenByDescending(supplier => supplier.CreatedAt).ThenBy(supplier => supplier.SupplierProfileId).ToListAsync()
            : await query.OrderBy(supplier => supplier.CompanyName).ThenBy(supplier => supplier.SupplierProfileId).ToListAsync();

        return View(applicationsOnly ? nameof(SupplierApplications) : nameof(Suppliers), new SupplierListViewModel
        {
            Suppliers = suppliers,
            AvailableStatuses = statuses,
            SelectedStatus = status,
            ApplicationsOnly = applicationsOnly
        });
    }

    [HttpGet]
    public async Task<IActionResult> SupplierDetails(int id)
    {
        var supplier = await FindSupplierDetails(id);
        if (supplier == null) return NotFound();

        return SupplierDetailsView(supplier, new ReviewSupplierViewModel
        {
            Id = id,
            PreviousStatus = supplier.Status,
            ReviewedAtTicks = supplier.ReviewedAt?.Ticks
        });
    }

    private Task<SupplierProfile?> FindSupplierDetails(int id) => db.SupplierProfiles.AsNoTracking()
        .Include(supplier => supplier.User)
        .Include(supplier => supplier.ReviewedByUser)
        .Include(supplier => supplier.SupplierDocuments)
        .SingleOrDefaultAsync(supplier => supplier.SupplierProfileId == id);

    private IActionResult SupplierDetailsView(SupplierProfile supplier, ReviewSupplierViewModel review)
    {
        ViewData["ActivePage"] = supplier.Status == "Approved" ? nameof(Suppliers) : nameof(SupplierApplications);
        return View(nameof(SupplierDetails), new SupplierDetailsViewModel(supplier, review, SupplierDocumentRoot() != null));
    }

    [HttpGet]
    public async Task<IActionResult> SupplierDocument(int id, bool preview = false)
    {
        var root = SupplierDocumentRoot();
        if (root == null) return NotFound();

        var document = await db.SupplierDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SupplierDocumentId == id);
        if (document == null || string.IsNullOrWhiteSpace(document.StoredFileName) ||
            document.StoredFileName is "." or ".." ||
            document.StoredFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            document.StoredFileName.Contains('/') || document.StoredFileName.Contains('\\'))
        {
            return NotFound();
        }

        var path = Path.Combine(root, document.StoredFileName);
        FileStream stream;
        try
        {
            if ((System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return NotFound();
            stream = System.IO.File.OpenRead(path);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        if (preview && document.ContentType is ("application/pdf" or "image/png" or "image/jpeg"))
        {
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            return File(stream, document.ContentType, enableRangeProcessing: true);
        }

        return File(stream, "application/octet-stream", Path.GetFileName(document.OriginalFileName));
    }

    private string? SupplierDocumentRoot()
    {
        var configured = configuration["SupplierDocuments:RootPath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(environment.ContentRootPath, "App_Data", "private-files", "suppliers");
        if (!Path.IsPathFullyQualified(configured)) return null;

        var root = Path.GetFullPath(configured);
        var relativeToWebRoot = Path.GetRelativePath(environment.WebRootPath, root);
        if (relativeToWebRoot == "." || (!Path.IsPathRooted(relativeToWebRoot) &&
            relativeToWebRoot != ".." && !relativeToWebRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            return null;
        }

        return root;
    }

    private async Task<UsersPageViewModel> LoadUsersPage(
        CreateUserViewModel? createUser = null, string? search = null, string? role = null, bool? isActive = null)
    {
        ViewData["ActivePage"] = nameof(Users);
        search = search?.Trim();
        role = RoleNames.Primary.Contains(role) ? role : null;

        var roleAssignments = from link in db.UserRoles
                              join assignedRole in db.Roles on link.RoleId equals assignedRole.Id
                              where assignedRole.Name != null
                              select new { link.UserId, Name = assignedRole.Name! };
        var accounts = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            accounts = accounts.Where(user => user.FullName.Contains(search) ||
                (user.Email != null && user.Email.Contains(search)));
        if (role != null)
            accounts = accounts.Where(user => roleAssignments.Any(assignment =>
                assignment.UserId == user.Id && assignment.Name == role));
        if (isActive.HasValue)
            accounts = accounts.Where(user => user.IsActive == isActive.Value);

        var rows = await accounts.OrderBy(user => user.FullName).ThenBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.PhoneNumber,
                user.IsActive,
                user.DepartmentId,
                Roles = roleAssignments.Where(assignment => assignment.UserId == user.Id)
                    .OrderBy(assignment => assignment.Name).Select(assignment => assignment.Name).ToList()
            })
            .ToListAsync();
        var departments = await db.Departments.AsNoTracking().OrderBy(department => department.Name).ToListAsync();
        ViewBag.Departments = departments;
        return new UsersPageViewModel
        {
            Users = rows.Select(row => new AdminUserRowViewModel(
                row.Id, row.FullName, row.Email, row.PhoneNumber, row.IsActive, row.DepartmentId, row.Roles)).ToList(),
            Departments = departments,
            CreateUser = createUser ?? new CreateUserViewModel(),
            Search = search,
            Role = role,
            IsActive = isActive,
            CurrentUserId = users.GetUserId(User)
        };
    }

    private async Task<IActionResult> InvalidCreateUser(CreateUserViewModel model)
    {
        ViewData["OpenCreateUser"] = true;
        return View("Users", await LoadUsersPage(model));
    }

    private Task<string?> ActivePositionHolder(string position, string? exceptUserId = null) =>
        (from user in db.Users
         join assignment in db.UserRoles on user.Id equals assignment.UserId
         join role in db.Roles on assignment.RoleId equals role.Id
         where user.IsActive && user.Id != exceptUserId && role.Name == position
         select user.FullName).FirstOrDefaultAsync();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 100)
            ModelState.AddModelError(nameof(name), "Enter a department name of 1–100 characters.");
        else if (await db.Departments.AnyAsync(department => department.Name == name))
            ModelState.AddModelError(nameof(name), "A department with this name already exists.");

        if (!ModelState.IsValid)
            return View(nameof(Departments), await LoadDepartmentsPage(createName: name, openCreate: true));

        db.Departments.Add(new Department { Name = name, IsActive = true });
        if (!await SaveDepartment())
            return View(nameof(Departments), await LoadDepartmentsPage(createName: name, openCreate: true));

        TempData["DepartmentsFeedbackTitle"] = "Department created successfully";
        TempData["DepartmentsFeedback"] = $"“{name}” is now active in the department directory.";
        return RedirectToAction(nameof(Departments));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserViewModel model, [FromServices] EmployeeAccounts accounts)
    {
        // Same rules as the spreadsheet import (EmployeeAccounts), so both paths stay in step.
        var input = new EmployeeAccountInput(model.FullName, model.Email, model.PhoneNumber, model.Role, model.CommitteeRole, model.DepartmentId);
        foreach (var error in await accounts.ValidateAsync(input, EmployeeAccounts.FormRoles))
            ModelState.AddModelError(error.Field, error.Message);
        if (!ModelState.IsValid) return await InvalidCreateUser(model);
        var created = await accounts.CreateAsync(input, model.Password, requirePasswordChange: false);
        if (created.PositionHolder is string holder)
        {
            ModelState.AddModelError(nameof(model.CommitteeRole), $"{model.CommitteeRole} already has an active account ({holder}). Deactivate it before assigning this position.");
            return await InvalidCreateUser(model);
        }
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors) ModelState.AddModelError("", error);
            return await InvalidCreateUser(model);
        }
        TempData["UsersFeedback"] = "Account created.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewSupplier([Bind(Prefix = "Review")] ReviewSupplierViewModel review)
    {
        var supplier = await FindSupplierDetails(review.Id);
        if (supplier == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(review.Status) && !SupplierListViewModel.Decisions.Contains(review.Status))
            ModelState.AddModelError("Review.Status", "Choose a review decision.");
        if (!ModelState.IsValid) return SupplierDetailsView(supplier, review);

        var reviewerId = users.GetUserId(User);
        if (reviewerId == null) return Forbid();

        var reviewedAt = DateTime.UtcNow;
        var notes = review.Notes.Trim();
        var previousReview = review.ReviewedAtTicks is long ticks ? (DateTime?)new DateTime(ticks) : null;
        await using var auditTransaction = await db.Database.BeginTransactionAsync();
        var updated = await db.SupplierProfiles
            .Where(item => item.SupplierProfileId == review.Id &&
                item.Status == review.PreviousStatus && item.ReviewedAt == previousReview)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, review.Status)
                .SetProperty(item => item.ReviewNotes, notes)
                .SetProperty(item => item.ReviewedAt, reviewedAt)
                .SetProperty(item => item.ReviewedByUserId, reviewerId));
        if (updated == 0)
        {
            TempData["SupplierReviewConflict"] = "This application changed before your decision could be saved. Review the latest details and try again.";
            return RedirectToAction(nameof(SupplierDetails), new { id = review.Id });
        }

        await HttpContext.RequestServices.GetRequiredService<IAuditLog>().WriteAsync("Modified", "SupplierProfile", review.Id.ToString(),
            "Supplier application reviewed.", changesJson: System.Text.Json.JsonSerializer.Serialize(new { Before = supplier.Status, After = review.Status }));
        await auditTransaction.CommitAsync();
        TempData["SupplierReviewFeedback"] = "Supplier decision saved.";
        return RedirectToAction(nameof(SupplierDetails), new { id = review.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(EditUserViewModel model)
    {
        var user = await users.FindByIdAsync(model.Id);
        if (user == null) return NotFound();
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var isDepartmentAccount = await users.IsInRoleAsync(user, RoleNames.Department);
        if (isDepartmentAccount && model.DepartmentId == null)
            ModelState.AddModelError(nameof(model.DepartmentId), "A department account needs a department.");
        if (isDepartmentAccount && model.DepartmentId.HasValue && model.DepartmentId != user.DepartmentId &&
            !await db.Departments.AnyAsync(d => d.DepartmentId == model.DepartmentId && d.IsActive))
            ModelState.AddModelError(nameof(model.DepartmentId), "Choose an active department.");
        if (model.Id == users.GetUserId(User) && !model.IsActive)
            ModelState.AddModelError(nameof(model.IsActive), "Your own administrator account must stay active.");
        if (model.IsActive && !user.IsActive)
        {
            var positions = (await users.GetRolesAsync(user))
                .Intersect(RoleNames.EvaluationPositions.Concat(RoleNames.TopPositions));
            foreach (var position in positions)
                if (await ActivePositionHolder(position, user.Id) is string holder)
                    ModelState.AddModelError(nameof(model.IsActive), $"{position} already has an active account ({holder}). Deactivate it before activating this account.");
        }
        if (ModelState.IsValid)
        {
            user.FullName = model.FullName;
            if (isDepartmentAccount) user.DepartmentId = model.DepartmentId;
            user.IsActive = model.IsActive;

            var phoneNumber = model.PhoneNumber?.Trim();
            if (user.PhoneNumber != phoneNumber)
            {
                user.PhoneNumber = phoneNumber;
                user.PhoneNumberConfirmed = false;
            }
            var result = await users.UpdateAsync(user);
            if (result.Succeeded)
            {
                await HttpContext.RequestServices.GetRequiredService<IAuditLog>().WriteAsync("Account.Edit",
                    "ApplicationUser", user.Id, "Administrator updated an account.");
                await transaction.CommitAsync();
                TempData["UsersFeedback"] = "Account updated.";
                return RedirectToAction(nameof(Users));
            }
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }
        ViewData["OpenEditUser"] = model.Id;
        ViewData["EditInput"] = model;
        return View("Users", await LoadUsersPage());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDepartment(EditDepartmentViewModel model)
    {
        var department = await db.Departments.FindAsync(model.DepartmentId);
        if (department == null) return NotFound();

        var name = model.Name?.Trim() ?? "";
        if (ModelState.IsValid && await db.Departments.AnyAsync(other =>
            other.Name == name && other.DepartmentId != department.DepartmentId))
        {
            ModelState.AddModelError(nameof(model.Name), "A department with this name already exists.");
        }

        if (!ModelState.IsValid)
            return View(nameof(Departments), await LoadDepartmentsPage(editInput: model));

        department.Name = name;
        department.IsActive = model.IsActive;
        if (!await SaveDepartment())
            return View(nameof(Departments), await LoadDepartmentsPage(editInput: model));

        TempData["DepartmentsFeedbackTitle"] = "Department updated successfully";
        TempData["DepartmentsFeedback"] = $"Changes to “{name}” were saved.";
        return RedirectToAction(nameof(Departments));
    }

    private async Task<bool> SaveDepartment()
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError("Name", "A department with this name already exists.");
            return false;
        }
    }
}
