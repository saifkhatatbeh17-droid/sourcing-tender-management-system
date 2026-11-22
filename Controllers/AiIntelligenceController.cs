using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Integrity;
using SourcingTenderManagement.Services.Intelligence;

namespace SourcingTenderManagement.Controllers;

[Authorize(Roles = "Admin,Department,SourcingOfficer,EvaluationCommittee,SourcingManager,TopCommittee")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AiIntelligenceController(
    UserManager<ApplicationUser> users,
    AiIntelligenceService intelligence,
    ILogger<AiIntelligenceController> logger) : Controller
{
    [HttpGet("/AiIntelligence")]
    [HttpGet("/Admin/AiIntelligence")]
    [HttpGet("/Department/AiIntelligence")]
    [HttpGet("/SourcingOfficer/AiIntelligence")]
    [HttpGet("/EvaluationCommittee/AiIntelligence")]
    [HttpGet("/SourcingManager/AiIntelligence")]
    [HttpGet("/TopCommittee/AiIntelligence")]
    public async Task<IActionResult> Index(string? workspace, int? tenderId, CancellationToken ct)
    {
        var pathSegment = HttpContext.Request.Path.Value?.Trim('/').Split('/').FirstOrDefault();
        var inferredWorkspace = !string.IsNullOrWhiteSpace(workspace)
            ? workspace
            : (AiIntelligenceService.AllowedWorkspaces.Contains(pathSegment ?? "", StringComparer.OrdinalIgnoreCase)
                ? AiIntelligenceService.AllowedWorkspaces.First(w => string.Equals(w, pathSegment, StringComparison.OrdinalIgnoreCase))
                : null);

        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(inferredWorkspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        var model = await intelligence.BuildDashboardAsync(resolvedWorkspace, User, user, tenderId, ct);
        if (resolvedWorkspace == RoleNames.Admin)
            ViewData["SupplierLinks"] = await HttpContext.RequestServices.GetRequiredService<SupplierLinks>().MapAsync(ct);

        ViewData["Title"] = "AI Intelligence";
        ViewData["ActivePage"] = "AiIntelligence";
        ViewData["WorkspaceController"] = resolvedWorkspace;
        if (model.SelectedTenderId.HasValue)
            ViewData["AiChatRecordId"] = model.SelectedTenderId.Value;

        return View(model);
    }

    [HttpGet("/AiIntelligence/TenderReport")]
    public async Task<IActionResult> TenderReport(int tenderId, string? workspace, CancellationToken ct)
    {
        if (tenderId <= 0)
            return BadRequest(new { error = "Select a valid tender." });

        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(workspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        var report = await intelligence.BuildTenderReportAsync(resolvedWorkspace, User, user, tenderId, ct);
        if (report == null)
            return NotFound(new { error = "Tender not found in your authorized workspace." });

        return Ok(report);
    }

    [HttpPost("/AiIntelligence/PortfolioInsights")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    public async Task<IActionResult> PortfolioInsights([FromBody] AiPortfolioPromptInput input, CancellationToken ct)
    {
        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(input?.Workspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        try
        {
            var result = await intelligence.GeneratePortfolioInsightsAsync(resolvedWorkspace, User, user, input?.Question, ct);
            return Ok(result);
        }
        catch (AiLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "AI Intelligence portfolio insights failed for workspace {Workspace}.", resolvedWorkspace);
            return StatusCode(503, new { error = "Could not generate live AI insights right now. Please try again in a moment." });
        }
    }

    [HttpPost("/AiIntelligence/TenderInsights")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    public async Task<IActionResult> TenderInsights([FromBody] AiTenderPromptInput input, CancellationToken ct)
    {
        if (input == null || input.TenderId <= 0)
            return BadRequest(new { error = "Select a tender to analyze." });

        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(input.Workspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        try
        {
            var result = await intelligence.GenerateTenderInsightsAsync(resolvedWorkspace, User, user, input.TenderId, ct);
            if (result == null)
                return NotFound(new { error = "Tender not found in your authorized workspace." });
            return Ok(result);
        }
        catch (AiLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "AI Intelligence tender insights failed for tender {TenderId}.", input.TenderId);
            return StatusCode(503, new { error = "Could not generate AI tender analysis right now. Please try again in a moment." });
        }
    }

    [HttpPost("/AiIntelligence/SupplierLinkBrief")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    public async Task<IActionResult> SupplierLinkBrief([FromBody] SupplierLinkBriefInput input,
        [FromServices] SupplierLinks links, [FromServices] SupplierLinkExplainer explainer, CancellationToken ct)
    {
        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(RoleNames.Admin);
        if (user == null || resolvedWorkspace != RoleNames.Admin)
            return Forbid();

        var link = await links.PairAsync(input.SupplierA, input.SupplierB, ct);
        if (link == null)
            return NotFound(new { error = "These suppliers are no longer linked." });

        try
        {
            return Ok(await explainer.ExplainAsync(link, ct));
        }
        catch (AiLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Supplier link brief failed for suppliers {A} and {B}.", input.SupplierA, input.SupplierB);
            return StatusCode(503, new { error = "The AI brief is not available right now. The evidence above is complete without it." });
        }
    }

    [HttpPost("/AiIntelligence/AnalyzeUpload")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> AnalyzeUpload(
        [FromForm] IFormFile? file,
        [FromForm, StringLength(40)] string? workspace,
        [FromForm, StringLength(1200)] string? prompt,
        CancellationToken ct)
    {
        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(workspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Choose an Excel (.xlsx) or PDF (.pdf) file to upload." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".pdf"))
            return BadRequest(new { error = "Only Excel (.xlsx) and PDF (.pdf) files are supported." });

        try
        {
            await using var stream = file.OpenReadStream();
            var analysis = await intelligence.AnalyzeUploadedFileAsync(
                resolvedWorkspace,
                User,
                user,
                Path.GetFileName(file.FileName),
                stream,
                prompt,
                ct);
            return Ok(analysis);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "AI Intelligence file upload analysis failed for workspace {Workspace}.", resolvedWorkspace);
            return StatusCode(503, new { error = "Could not complete AI analysis on this document right now. Check the file or try again." });
        }
    }

    [HttpGet("/AiIntelligence/ExportDashboard")]
    public async Task<IActionResult> ExportDashboard(string? workspace, string format = "pdf", CancellationToken ct = default)
    {
        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(workspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        var (bytes, contentType, fileName) = await intelligence.ExportDashboardAsync(resolvedWorkspace, User, user, format, ct);
        return File(bytes, contentType, fileName);
    }

    [HttpGet("/AiIntelligence/ExportTender")]
    public async Task<IActionResult> ExportTender(int tenderId, string? workspace, string format = "pdf", CancellationToken ct = default)
    {
        if (tenderId <= 0)
            return BadRequest("Select a valid tender.");

        var (user, resolvedWorkspace) = await AuthorizeWorkspaceUserAsync(workspace);
        if (user == null || resolvedWorkspace == null)
            return Forbid();

        var exported = await intelligence.ExportTenderAsync(resolvedWorkspace, User, user, tenderId, format, ct);
        if (exported == null)
            return NotFound();

        return File(exported.Value.Bytes, exported.Value.ContentType, exported.Value.FileName);
    }

    private async Task<(ApplicationUser? User, string? Workspace)> AuthorizeWorkspaceUserAsync(string? requestedWorkspace)
    {
        var resolved = AiIntelligenceService.ResolveAuthorizedWorkspace(User, requestedWorkspace);
        if (resolved == null)
            return (null, null);

        var user = await users.GetUserAsync(User);
        if (user?.IsActive != true || !await users.IsInRoleAsync(user, resolved))
            return (null, null);

        return (user, resolved);
    }
}

public sealed record AiPortfolioPromptInput(
    [StringLength(40)] string? Workspace,
    [StringLength(1200)] string? Question);

public sealed record SupplierLinkBriefInput(int SupplierA, int SupplierB);

public sealed record AiTenderPromptInput(
    int TenderId,
    [StringLength(40)] string? Workspace);
