using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Controllers;

// Back end for Safi, the chat assistant shown inside every workspace.
// Receives questions, voice (speech to text and text to speech) and attachments, and returns the assistant's reply.
[ApiController]
[Authorize]
[Route("AiChat")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AiChatController(
    UserManager<ApplicationUser> users,
    IAuthorizationService authorization,
    AiChatContext context,
    AiChatWorkspaceActions actions,
    AiChatClient client,
    AiChatDownloadStore downloadStore,
    ILogger<AiChatController> logger) : ControllerBase
{
    [HttpGet("Download/{token}")]
    public async Task<IActionResult> Download(string token)
    {
        var user = await users.GetUserAsync(User);
        if (user?.IsActive != true)
            return Forbid();

        var file = downloadStore.TryGet(token, user.Id);
        if (file == null)
            return NotFound("This generated download has expired or does not exist. Ask Safi to export it again.");

        if (!await users.IsInRoleAsync(user, file.Workspace))
            return Forbid();

        if (file.Workspace == RoleNames.Supplier && !(await authorization.AuthorizeAsync(User, HttpContext, "ApprovedSupplier")).Succeeded)
            return Forbid();

        return File(file.Bytes, file.ContentType, file.FileName);
    }
    [HttpPost("Speak")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(32 * 1024)]
    public async Task<IActionResult> Speak(AiChatSpeechInput input, [FromServices] AiChatSpeechClient speech, CancellationToken ct)
    {
        if (!await CanUseWorkspaceAsync(input.Workspace))
            return Forbid();

        try
        {
            var audio = await speech.SpeakAsync(input.Text.Trim(), ct);
            if (audio is null)
                return StatusCode(503);
            Response.Headers["X-Safi-Speech-Provider"] = audio.Provider;
            return File(audio.Bytes, audio.ContentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Safi speech unavailable ({ErrorType}).", ex.GetType().Name);
            return StatusCode(503);
        }
    }

    [HttpPost("Transcribe")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Transcribe([FromForm] string workspace, [FromForm] IFormFile audio,
        [FromServices] AiChatSpeechClient speech, CancellationToken ct)
    {
        if (!await CanUseWorkspaceAsync(workspace))
            return Forbid();

        var extension = Path.GetExtension(audio.FileName).ToLowerInvariant();
        if (audio.Length is <= 0 or > 1024 * 1024 || extension is not (".webm" or ".ogg" or ".mp4"))
            return BadRequest();

        try
        {
            await using var stream = audio.OpenReadStream();
            var text = await speech.TranscribeAsync(stream, "turn" + extension, ct);
            return text is null ? StatusCode(503) : Ok(new { text });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Safi transcription unavailable ({ErrorType}).", ex.GetType().Name);
            return StatusCode(503);
        }
    }

    [HttpPost("AnalyzeAttachment")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> AnalyzeAttachment([FromForm] AiChatAttachmentInput input,
        [FromServices] AiChatAttachmentAnalysis analysis, CancellationToken ct)
    {
        if (!await CanUseWorkspaceAsync(input.Workspace))
            return Forbid();
        if (input.File.Length is <= 0 or > AiChatAttachmentAnalysis.MaxFileBytes)
            return BadRequest(new { error = "Attach a file up to 5 MB." });

        try
        {
            await using var stream = new MemoryStream((int)input.File.Length);
            await input.File.CopyToAsync(stream, ct);
            stream.Position = 0;
            var extension = Path.GetExtension(input.File.FileName).ToLowerInvariant();
            var answer = await analysis.AnalyzeAsync(stream, extension, input.Question.Trim(), input.Workspace, ct);
            return Ok(new { answer });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is InvalidDataException or ClosedXML.Excel.Exceptions.ClosedXMLException or ArgumentException)
        {
            return BadRequest(new { error = "The attachment could not be read. Check its file type and try again." });
        }
        catch (AiLimitReachedException ex)
        {
            return StatusCode(429, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Safi attachment analysis unavailable ({ErrorType}).", ex.GetType().Name);
            return StatusCode(503, new { error = "File analysis is temporarily unavailable. Your attachment was not saved." });
        }
    }

    private async Task<bool> CanUseWorkspaceAsync(string workspace)
    {
        if (!PageNavigation.Menus.ContainsKey(workspace) || !User.IsInRole(workspace))
            return false;
        var user = await users.GetUserAsync(User);
        if (user?.IsActive != true || !await users.IsInRoleAsync(user, workspace))
            return false;
        return workspace != RoleNames.Supplier ||
            (await authorization.AuthorizeAsync(User, HttpContext, "ApprovedSupplier")).Succeeded;
    }

    [HttpPost("Ask")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(384 * 1024)] // Allow 12 full history messages even when Unicode is JSON-escaped.
    public async Task<IActionResult> Ask(AiChatInput input, CancellationToken ct)
    {
        var workspace = input.Workspace?.Trim() ?? "";
        if (!PageNavigation.Menus.TryGetValue(workspace, out var pages) || !User.IsInRole(workspace))
            return Forbid();

        var user = await users.GetUserAsync(User);
        if (user?.IsActive != true || !await users.IsInRoleAsync(user, workspace))
            return Forbid();

        if (workspace == RoleNames.Supplier && !(await authorization.AuthorizeAsync(User, HttpContext, "ApprovedSupplier")).Succeeded)
            return Forbid();

        var question = input.Question.Trim();
        if (question.Length == 0)
            return BadRequest(new { error = "Enter a message." });

        var history = NormalizeHistory(input);
        if (history == null)
            return BadRequest(new { error = "The conversation history is too long or invalid." });

        try
        {
            var initialSnapshot = await context.BuildSnapshotAsync(workspace, input.Page, input.RecordId, User, user, ct);
            var authorizedTools = AiChatWorkspaceActions.GetAuthorizedTools(workspace);

            var result = await client.RunAsync(
                workspace,
                pages.Select(p => p.Title).ToArray(),
                question,
                history,
                initialSnapshot,
                authorizedTools,
                (toolCall, token) => actions.ExecuteAsync(workspace, User, user, toolCall.ToolName, toolCall.ArgumentsJson, token),
                token => context.BuildSnapshotAsync(workspace, input.Page, input.RecordId, User, user, token),
                ct);

            return Ok(new
            {
                answer = result.Answer,
                workspaceOnly = result.WorkspaceOnly,
                mutated = result.Mutated,
                actionSummaries = result.ActionSummaries,
                navigateUrl = result.NavigateUrl,
                source = result.SourceUrl,
                sourceLabel = result.SourceLabel ?? "Open record",
                downloads = result.Downloads ?? []
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or
            KeyNotFoundException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "AiChat request failed for workspace {Workspace} (page={Page}, recordId={RecordId}).", workspace, input.Page, input.RecordId);
            return StatusCode(503, new { error = "Safi could not confirm the result. Check your workspace before resending any action." });
        }
    }

    private static List<AiChatHistoryTurn>? NormalizeHistory(AiChatInput input)
    {
        var list = new List<AiChatHistoryTurn>();
        if (input.History is { Length: > 0 })
        {
            if (input.History.Length > AiChatClient.HistoryMessageLimit) return null;
            foreach (var item in input.History)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Content) || item.Content.Length > 4500)
                    return null;
                var role = item.Role?.Trim().ToLowerInvariant() == "assistant" ? "assistant" : "user";
                list.Add(new AiChatHistoryTurn(role, item.Content.Trim()));
            }
            return list;
        }

        if (input.EarlierQuestions is { Length: > 0 })
        {
            if (input.EarlierQuestions.Length > 6 || input.EarlierQuestions.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 1500))
                return null;
            foreach (var q in input.EarlierQuestions)
                list.Add(new AiChatHistoryTurn("user", q.Trim()));
        }

        return list;
    }
}

public sealed record AiChatTurnInput(
    [Required, StringLength(16)] string Role,
    [Required, StringLength(4500)] string Content);

public sealed record AiChatAttachmentInput(
    [Required, StringLength(40)] string Workspace,
    [Required, StringLength(1500)] string Question,
    [Required] IFormFile File);

public sealed record AiChatSpeechInput(
    [Required, StringLength(40)] string Workspace,
    [Required, StringLength(AiChatSpeechClient.InputLimit)] string Text);

public sealed record AiChatInput(
    [Required, StringLength(1500)] string Question,
    [Required, StringLength(40)] string Workspace,
    [StringLength(40)] string? Page,
    int? RecordId,
    string[]? EarlierQuestions,
    AiChatTurnInput[]? History);
