using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Reports;
using SourcingTenderManagement.Services.Reports;
using SourcingTenderManagement.Services.Reports.Hub;
using System.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

[Authorize]
[Route("Reports")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ReportsController(ApplicationDbContext db, ReportRegistry registry,
    ReportFilterOptions filters, ReportExport exporter, ILogger<ReportsController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var scope = await GetScopeAsync(ct);
        if (scope == null) return StatusCode(StatusCodes.Status403Forbidden);
        ViewData["WorkspaceController"] = scope.Role;
        try
        {
            return View(new ReportsHubPage(scope, await registry.GetAvailableAsync(scope.Role, ct)));
        }
        catch (SqlException exception) when (MissingAccessTable(exception))
        {
            return PermissionsUnavailable(exception);
        }
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Show(string key, [FromQuery] ReportQuery query, CancellationToken ct)
    {
        var report = registry.Find(key);
        if (report == null) return NotFound();
        var scope = await GetScopeAsync(ct);
        if (scope == null) return StatusCode(StatusCodes.Status403Forbidden);
        ViewData["WorkspaceController"] = scope.Role;
        try
        {
            if (!await registry.CanAccessAsync(key, scope.Role, ct))
                return StatusCode(StatusCodes.Status403Forbidden);

            var (normalized, choices, messages) = await PrepareQueryAsync(report, query, scope, ct);
            var result = await registry.RunAsync(key, normalized, scope, ct);
            var lastPage = Math.Max(1, (int)Math.Ceiling(result.Table.Total / 25d));
            if (normalized.Page > lastPage)
            {
                normalized = normalized with { Page = lastPage };
                result = await registry.RunAsync(key, normalized, scope, ct);
            }

            return View(new ReportPageModel(report, scope, normalized, result, choices,
                messages.Distinct().ToList(), Request.PathBase + "/Reports/" + Uri.EscapeDataString(report.Key)));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or CommitteeAccessDeniedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (SqlException exception) when (MissingAccessTable(exception))
        {
            return PermissionsUnavailable(exception);
        }
    }

    [HttpGet("{key}/export")]
    public async Task<IActionResult> Export(string key, [FromQuery] ReportQuery query, string? format, CancellationToken ct)
    {
        var report = registry.Find(key);
        if (report == null) return NotFound();
        var scope = await GetScopeAsync(ct);
        if (scope == null) return StatusCode(StatusCodes.Status403Forbidden);
        ViewData["WorkspaceController"] = scope.Role;
        try
        {
            if (!await registry.CanAccessAsync(key, scope.Role, ct))
                return StatusCode(StatusCodes.Status403Forbidden);
            format = format?.ToLowerInvariant();
            if (format is not ("pdf" or "xlsx"))
                return BadRequest(new ProblemDetails { Status = 400, Title = "Unsupported report format", Detail = "Choose pdf or xlsx." });

            var (normalized, choices, messages) = await PrepareQueryAsync(report, query, scope, ct);
            if (messages.Count > 0)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return View("Show", new ReportPageModel(report, scope, normalized,
                    new ReportResult([], null, new ReportTable([], [], 0), []), choices,
                    messages.Distinct().ToList(), Request.PathBase + "/Reports/" + Uri.EscapeDataString(report.Key)));
            }

            normalized = normalized with { ForExport = true, Page = 1 };
            var result = await registry.RunAsync(key, normalized, scope, ct);
            var generatedAt = DateTime.UtcNow;
            var name = $"{report.Key}-report-{generatedAt:yyyyMMdd}.{format}";
            if (format == "pdf")
                return ReportPdfDownload.Create(this, () => exporter.Pdf(report, normalized, result, generatedAt), name);
            try
            {
                return File(exporter.Excel(report, normalized, result, generatedAt), ReportExport.ExcelContentType, name);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                logger.LogWarning("Report Excel export failed ({FailureType}).", exception.GetType().Name);
                return StatusCode(503, new ProblemDetails { Status = 503, Title = "The complete report could not be exported.",
                    Detail = "No partial workbook was returned. Check the report and try again." });
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or CommitteeAccessDeniedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (SqlException exception) when (MissingAccessTable(exception))
        {
            return PermissionsUnavailable(exception);
        }
    }

    private async Task<(ReportQuery Query, ReportFilterChoices Choices, List<string> Messages)> PrepareQueryAsync(
        IReport report, ReportQuery query, ReportScope scope, CancellationToken ct)
    {
        var (normalized, messages) = ReportQueryValidation.Normalize(report, query);
        if (!ModelState.IsValid) messages.Add("Some filter values could not be read and were cleared. Check your filters.");
        var choices = await filters.LoadAsync(report, scope, ct);
        int? VisibleId(int? id, IReadOnlyList<ReportFilterOption> options)
        {
            if (id == null || options.Any(option => option.Id == id)) return id;
            messages.Add("A record filter was unavailable in your workspace and has been cleared.");
            return null;
        }
        return (normalized with
        {
            DepartmentId = VisibleId(normalized.DepartmentId, choices.Departments),
            SupplierId = VisibleId(normalized.SupplierId, choices.Suppliers),
            TenderId = VisibleId(normalized.TenderId, choices.Tenders)
        }, choices, messages);
    }

    private async Task<ReportScope?> GetScopeAsync(CancellationToken ct)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id)) return null;
        var account = await db.Users.AsNoTracking().Where(user => user.Id == id && user.IsActive)
            .Select(user => new { user.Id, user.DepartmentId }).SingleOrDefaultAsync(ct);
        if (account == null) return null;
        var assignedRoles = await (from assignment in db.UserRoles
                                   join role in db.Roles on assignment.RoleId equals role.Id
                                   where assignment.UserId == id
                                   select role.Name).ToListAsync(ct);
        var workspace = RoleNames.Primary.FirstOrDefault(role => User.IsInRole(role) && assignedRoles.Contains(role));
        if (workspace == null || workspace == RoleNames.Supplier) return null;
        return new ReportScope(account.Id, workspace, account.DepartmentId, workspace == RoleNames.Admin);
    }

    internal static bool MissingAccessTable(SqlException exception) => exception.Number == 208 &&
        exception.Message.Contains("ReportAccessRules", StringComparison.OrdinalIgnoreCase);

    private IActionResult PermissionsUnavailable(SqlException exception)
    {
        logger.LogWarning(exception, "Reports access schema is not available.");
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View("Unavailable");
    }

    // Admin: who may open each report (/Reports/Access).
    [HttpGet("Access"), Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Access(CancellationToken ct, bool data = false)
    {
        try
        {
            var permissions = await registry.GetPermissionsAsync(ct);
            return data ? Ok(permissions) : View("~/Views/Reports/Access.cshtml", permissions);
        }
        catch (SqlException exception) when (MissingAccessTable(exception))
        {
            logger.LogWarning(exception, "Reports access schema is not available.");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("~/Views/Reports/Unavailable.cshtml");
        }
    }

    [HttpPost("Access"), Authorize(Roles = RoleNames.Admin), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAccess([FromBody] List<ReportAccessChange>? changes, CancellationToken ct)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminId)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var reports = await registry.GetAvailableAsync(RoleNames.Admin, ct);
        if (changes == null || changes.Count == 0 || changes.Count > reports.Count * ReportRegistry.EditableRoles.Count)
            return BadRequest("Submit permission changes for registered reports and eligible roles.");

        var selected = new Dictionary<(string Key, string Role), bool>();
        foreach (var change in changes)
        {
            var report = registry.Find(change?.ReportKey);
            if (report == null || change?.Allowed is not bool allowed ||
                !registry.CanConfigure(report.Key, change.Role))
                return BadRequest("Choose a registered report, a supported editable role, and an allow or deny value. Fixed permissions cannot be changed.");

            if (!selected.TryAdd((report.Key, change.Role), allowed))
                return BadRequest("A report and role can appear only once per submission.");
        }

        // Serialize the read/update/insert batch so concurrent saves cannot create duplicate rules.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var keys = selected.Keys.Select(item => item.Key).Distinct().ToList();
            var rules = await db.ReportAccessRules.Where(rule => keys.Contains(rule.ReportKey)).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var (pair, allowed) in selected)
            {
                var rule = rules.SingleOrDefault(rule => rule.Role == pair.Role &&
                    rule.ReportKey.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (rule == null)
                {
                    rule = new ReportAccessRule { ReportKey = pair.Key, Role = pair.Role };
                    db.ReportAccessRules.Add(rule);
                }

                rule.Allowed = allowed;
                rule.UpdatedByUserId = adminId;
                rule.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new { message = "Report permissions saved successfully." });
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(ct);
            logger.LogWarning(exception, "Report permission changes could not be saved.");
            return Conflict("Report permissions could not be saved. Refresh and try again.");
        }
        catch (SqlException exception) when (MissingAccessTable(exception))
        {
            await transaction.RollbackAsync(ct);
            logger.LogWarning(exception, "Reports access schema is not available.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Report permissions are unavailable until AddReportAccessRules is applied." });
        }
    }
}
