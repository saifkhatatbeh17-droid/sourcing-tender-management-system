using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Controllers;

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
    [HttpPost("Ask")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(65536)]
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
            return StatusCode(503, new { error = "The assistant could not complete this request right now. Check your AI endpoint/API key or try again in a moment." });
        }
    }

    private static List<AiChatHistoryTurn>? NormalizeHistory(AiChatInput input)
    {
        var list = new List<AiChatHistoryTurn>();
        if (input.History is { Length: > 0 })
        {
            if (input.History.Length > 12) return null;
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

public sealed record AiChatInput(
    [Required, StringLength(1500)] string Question,
    [Required, StringLength(40)] string Workspace,
    [StringLength(40)] string? Page,
    int? RecordId,
    string[]? EarlierQuestions,
    AiChatTurnInput[]? History);
