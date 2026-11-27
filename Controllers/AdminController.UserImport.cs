using SourcingTenderManagement.Services.Reports.Data;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.Admin;

namespace SourcingTenderManagement.Controllers;

// Account import from an Excel sheet, used from Safi's panel in the Admin workspace.
// No AI is involved: the sheet is read and validated here and never sent to the model.
public partial class AdminController
{
    // GET /Admin/UserImportTemplate: User import template: sends back a file download.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UserImportTemplate([FromServices] EmployeeImport import, CancellationToken ct) =>
        File(await import.TemplateAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "tiryaq-account-import-template.xlsx");

    // POST /Admin/UserImportPreview: User import preview: sends back data (JSON) for the page's script.
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(EmployeeImport.MaxFileBytes + 64 * 1024)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UserImportPreview(IFormFile? file, [FromServices] EmployeeImport import, CancellationToken ct)
    {
        var adminId = users.GetUserId(User);
        if (adminId == null) return Forbid();
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Choose an Excel (.xlsx) file with the accounts to create." });
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only Excel .xlsx files can be imported." });
        if (file.Length > EmployeeImport.MaxFileBytes)
            return BadRequest(new { error = "The file is larger than 1 MB. Split it into smaller files." });

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await import.PreviewAsync(stream, adminId, ct);
            return Ok(new
            {
                token = preview.Token,
                readyCount = preview.ReadyCount,
                ready = preview.Ready,
                problems = preview.Problems,
                notices = preview.Notices
            });
        }
        catch (EmployeeImportFileException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Resets an employee's password to a temporary one, returned once in this response and never stored or logged.
    [HttpPost, ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResetUserPassword(string? id, [FromServices] EmployeeAccounts accounts,
        [FromServices] ILogger<AdminController> logger)
    {
        var adminId = users.GetUserId(User);
        if (adminId == null) return Forbid();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "Choose an account." });

        var (password, error, account) = await accounts.ResetPasswordAsync(id, adminId);
        if (error != null) return BadRequest(new { error });

        await HttpContext.RequestServices.GetRequiredService<IAuditLog>().WriteAsync("Account.PasswordReset",
            "ApplicationUser", account!.Id, "Administrator reset an employee password.");
        logger.LogInformation("Administrator {AdminId} reset the password for account {UserId}.", adminId, account!.Id);
        return Ok(new { name = account.FullName, email = account.Email, password });
    }

    // POST /Admin/UserImportConfirm: User import confirm: sends back data (JSON) for the page's script.
    [HttpPost, ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UserImportConfirm(string? token, [FromServices] EmployeeImport import, CancellationToken ct)
    {
        var adminId = users.GetUserId(User);
        if (adminId == null) return Forbid();
        var result = await import.ConfirmAsync(token ?? "", adminId, ct);
        if (result == null)
            return BadRequest(new { error = "This preview has expired or was already used. Upload the file again." });

        await HttpContext.RequestServices.GetRequiredService<IAuditLog>().WriteAsync("Account.Import",
            "ApplicationUser", "", $"Employee import created {result.CreatedCount} accounts.", ct: ct);
        // Passwords are only in the one-time credentials file, never in this response.
        return Ok(new
        {
            createdCount = result.CreatedCount,
            failed = result.Failed,
            credentials = result.Credentials
        });
    }
}
