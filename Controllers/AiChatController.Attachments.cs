using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Controllers;

public sealed partial class AiChatController
{
    [HttpGet("AccountTemplate"), Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> AccountTemplate()
    {
        if (!await CanUseSpeechAsync(RoleNames.Admin)) return Forbid();
        return File(AiChatAccountImport.Template(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Tiryaq-Accounts-Template.xlsx");
    }

    [HttpPost("Attachment"), ValidateAntiForgeryToken, EnableRateLimiting("AiChat")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Attachment([FromForm] AiChatAttachmentInput input,
        [FromServices] AiChatAccountImport importer, [FromServices] AiChatImageClient images,
        [FromServices] AiChatPdfClient pdfs, [FromServices] AiChatSpreadsheetClient spreadsheets, CancellationToken ct)
    {
        if (!PageNavigation.Menus.ContainsKey(input.Workspace) || !User.IsInRole(input.Workspace)) return Forbid();
        var account = await users.GetUserAsync(User);
        if (account?.IsActive != true || !await users.IsInRoleAsync(account, input.Workspace)) return Forbid();
        if (input.File.Length is <= 0 or > 5 * 1024 * 1024) return BadRequest(new { error = "Attach a file up to 5 MB." });
        var extension = Path.GetExtension(input.File.FileName).ToLowerInvariant();
        var arabic = input.Question?.Any(c => c is >= '\u0600' and <= '\u06ff') ?? false;
        try
        {
            await using var memory = new MemoryStream();
            await input.File.CopyToAsync(memory, ct); memory.Position = 0;
            if (extension == ".xlsx")
            {
                var accountRequest = input.Intent == "accounts" || input.ConfirmImport ||
                    System.Text.RegularExpressions.Regex.IsMatch(input.Question ?? "", @"\b(?:create|register|import|add)\b.{0,70}\b(?:accounts?|users?)\b|(?:انش|أنش|إنش|كريت|كرييت|سجل|تسجيل|اضف|أضف).{0,70}(?:حساب|يوزر|مستخدم)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!accountRequest)
                {
                    var analysisQuestion = string.IsNullOrWhiteSpace(input.Question) ? "حلّل ملف الإكسل واشرح أهم النتائج والملاحظات." : input.Question.Trim();
                    var analysis = await spreadsheets.AnalyzeAsync(memory, analysisQuestion, input.Workspace, ct);
                    return Ok(new { answer = analysis, mutated = false, previewReady = false, downloads = Array.Empty<object>() });
                }
                if (input.Workspace != RoleNames.Admin || !User.IsInRole(RoleNames.Admin))
                    return StatusCode(403, new { error = "إنشاء الحسابات مسموح للأدمن فقط. يمكنك رفع Excel لتحليل بياناته، لكن لا يمكنك إنشاء مستخدمين من هذا الحساب." });
                var rows = AiChatAccountImport.Parse(memory, input.DefaultRole);
                var result = await importer.ProcessAsync(rows, input.ConfirmImport, ct);
                var valid = result.Rows.All(r => r.Errors.Length == 0);
                var heading = result.Created > 0
                    ? (arabic ? $"تم إنشاء {result.Created} حسابًا وحفظها. يمكن تسجيل الدخول بالإيميل وكلمة المرور الموجودة بالملف. حسابات الموردين بانتظار اعتماد الأدمن." : $"Created and saved {result.Created} accounts. They can sign in with the workbook email and password. Supplier profiles await admin approval.")
                    : valid ? (arabic ? "الملف صالح. راجعي الحسابات ثم اضغطي «تأكيد إنشاء الحسابات». لم يتم إنشاء أي حساب بعد." : "Workbook validated. Review the accounts, then click Confirm account creation. No accounts have been created yet.")
                    : (arabic ? "لم يتم إنشاء أي حساب. صححي الأخطاء التالية وأعيدي رفع الملف:" : "No accounts were created. Correct these errors and upload the workbook again:");
                var existingCount = result.Rows.Where(r => r.AlreadyExists).Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (existingCount > 0)
                    heading = result.Rows.All(r => r.AlreadyExists)
                        ? (arabic ? "الحسابات الموجودة في الملف موجودة مسبقًا في قاعدة البيانات. لم يتم إنشاء حسابات مكررة أو تغيير كلمات المرور أو بيانات الحسابات الحالية." : "All accounts in this workbook already exist in the database. No duplicate accounts were created, and existing passwords and account details were not changed.")
                        : (arabic ? $"يوجد {existingCount} حساب موجود مسبقًا في قاعدة البيانات، ومبيّن في الجدول. لم يتم إنشاء أي حساب. أزيلي الحسابات الموجودة من الملف وصححي أي أخطاء أخرى، ثم أعيدي رفعه." : $"{existingCount} account(s) already exist in the database and are marked in the table. No accounts were created. Remove existing accounts from the workbook, correct any other errors, and upload it again.");
                return Ok(new { answer = heading, existingCount, importRows = result.Rows.Select(r => new { r.Row, r.Email, r.FullName, r.Role, r.AlreadyExists,
                    Errors = r.Errors.Select(e => arabic && e == "An account with this email already exists." ? "هذا الحساب موجود مسبقًا في قاعدة البيانات." : e).ToArray() }), mutated = result.Created > 0,
                    previewReady = valid && result.Created == 0, createdCount = result.Created,
                    source = "/Admin/Users", sourceLabel = "View users", downloads = Array.Empty<object>() });
            }
            if (extension == ".pdf")
            {
                var pdfQuestion = string.IsNullOrWhiteSpace(input.Question) ? "لخّص هذا الملف واشرح أهم المعلومات فيه." : input.Question.Trim();
                var pdfAnswer = await pdfs.AnalyzeAsync(memory, pdfQuestion, input.Workspace, ct);
                return Ok(new { answer = pdfAnswer, mutated = false, previewReady = false, downloads = Array.Empty<object>() });
            }
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                return BadRequest(new { error = "Attach Excel (.xlsx), PDF, PNG, JPEG or WebP. Account creation is restricted to Admin." });
            var question = string.IsNullOrWhiteSpace(input.Question) ? "اشرح الصورة وساعدني أفهم المشكلة الظاهرة." : input.Question.Trim();
            var answer = await images.AnalyzeAsync(memory, question, input.Workspace, ct);
            return Ok(new { answer, mutated = false, previewReady = false, downloads = Array.Empty<object>() });
        }
        catch (ValidationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) when (ex is InvalidDataException or ClosedXML.Excel.Exceptions.ClosedXMLException)
        { return BadRequest(new { error = "This workbook is unreadable. Use an unencrypted .xlsx file with plain values." }); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Chat attachment provider failed with status {StatusCode}", ex.StatusCode);
            var delay = ex.Data["RetrySeconds"] as int?;
            return StatusCode(503, new { error = ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? delay.HasValue ? $"The AI provider's token quota is currently exhausted. Try again in approximately {delay.Value} seconds. Your attachment was not saved." : "The AI service is rate-limited. Wait a minute and retry."
                : "Attachment analysis is unavailable. Check the configured model and AI connection." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or DbUpdateException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Chat attachment failed ({ErrorType})", ex.GetType().Name);
            return StatusCode(503, new { error = "The attachment could not be processed. No partial account import was saved. Review the file and try again." });
        }
    }
}

public sealed class AiChatAttachmentInput
{
    [Required, StringLength(40)] public string Workspace { get; set; } = "";
    [StringLength(1500)] public string? Question { get; set; }
    [Required] public IFormFile File { get; set; } = null!;
    [StringLength(40)] public string DefaultRole { get; set; } = RoleNames.SourcingOfficer;
    public bool ConfirmImport { get; set; }
    [RegularExpression("^(auto|analyze|accounts)$")] public string Intent { get; set; } = "auto";
}
