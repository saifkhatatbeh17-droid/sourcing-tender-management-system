using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.Reports;
using SourcingTenderManagement.Services.Reports.Hub;

namespace SourcingTenderManagement.Controllers;

[Authorize(Roles = RoleNames.Admin)]
[Route("Reports/Access")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ReportAccessController(ApplicationDbContext db, ReportRegistry registry,
    ILogger<ReportAccessController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct, bool data = false)
    {
        try
        {
            var permissions = await registry.GetPermissionsAsync(ct);
            return data ? Ok(permissions) : View("~/Views/Reports/Access.cshtml", permissions);
        }
        catch (SqlException exception) when (ReportsController.MissingAccessTable(exception))
        {
            logger.LogWarning(exception, "Reports access schema is not available.");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("~/Views/Reports/Unavailable.cshtml");
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] List<ReportAccessChange>? changes, CancellationToken ct)
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
                !ReportRegistry.EditableRoles.Contains(change.Role, StringComparer.Ordinal))
                return BadRequest("Choose a registered report, an eligible role, and an allow or deny value.");

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
        catch (SqlException exception) when (ReportsController.MissingAccessTable(exception))
        {
            await transaction.RollbackAsync(ct);
            logger.LogWarning(exception, "Reports access schema is not available.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Report permissions are unavailable until AddReportAccessRules is applied." });
        }
    }
}
