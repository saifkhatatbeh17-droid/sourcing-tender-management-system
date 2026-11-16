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
public sealed class AiChatController(UserManager<ApplicationUser> users, IAuthorizationService authorization,
    AiChatContext context, AiChatClient client) : ControllerBase
{
    [HttpPost("Ask")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Ask(AiChatInput input, CancellationToken ct)
    {
        var workspace = input.Workspace;
        if (!PageNavigation.Menus.TryGetValue(workspace, out var pages) || !User.IsInRole(workspace))
            return Forbid();
        var user = await users.GetUserAsync(User);
        if (user?.IsActive != true || !await users.IsInRoleAsync(user, workspace))
            return Forbid();
        if (workspace == RoleNames.Supplier && !(await authorization.AuthorizeAsync(User, HttpContext, "ApprovedSupplier")).Succeeded)
            return Forbid();

        var question = input.Question.Trim();
        var role = RoleNames.DisplayName(workspace);
        if (AiChatScopeRules.IsRoleNameQuestion(question))
            return Ok(new { answer = role });
        if (AiChatScopeRules.RequestsTopCommitteeProcess(workspace, question))
            return Ok(new { answer = AiChatScopeRules.OutsideReply(role, question) });
        if (workspace == RoleNames.SourcingManager && AiChatScopeRules.IsManagerReferralQuestion(question))
            return Ok(new
            {
                answer = "Open Committee Submissions and select the approved recommendation. Enter a referral reason, then select Forward to CEO & Chiefs.",
                source = Url.Action("Submissions", workspace),
                sourceLabel = "Open Committee Submissions"
            });
        var earlierQuestions = input.EarlierQuestions ?? [];
        if (earlierQuestions.Length > 3 || earlierQuestions.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 750))
            return BadRequest(new { error = "The conversation history is too long or invalid." });

        try
        {
            var canDiscussAllWorkspaces = workspace == RoleNames.Admin;
            var record = input.RecordId.HasValue ? AiChatPage.Record(workspace, input.Page) : null;
            var scope = await client.ClassifyAsync(new
            {
                workspace = role, canDiscussAllWorkspaces, currentRecordType = record?.ToString(),
                allowedWork = canDiscussAllWorkspaces ? "All Tiryaq workflows across every role." : AiChatPage.Guidance(workspace),
                earlierQuestions, question
            }, ct);
            if (scope == AiChatScope.RoleName)
                return Ok(new { answer = role });
            if (scope == AiChatScope.Outside)
                return Ok(new { answer = AiChatScopeRules.OutsideReply(role, question) });

            object? facts = null;
            string? source = null;
            if (scope == AiChatScope.CurrentRecord && input.RecordId is int id)
            {
                if (id <= 0 || record == null)
                    return NotFound(new { error = "This record is unavailable in the current workspace." });
                facts = await context.ReadAsync(record.Value, id, User, user.Id, user.DepartmentId, ct);
                if (facts == null)
                    return NotFound(new { error = "This record is unavailable in the current workspace." });
                source = record == AiChatRecord.OfficerTender
                    ? Url.Action("Offers", workspace, new { tenderId = id })
                    : Url.Action(input.Page, workspace, new { id });
            }
            var answer = await client.AskAsync(new
            {
                workspace = role,
                canDiscussAllWorkspaces,
                guidance = canDiscussAllWorkspaces
                    ? string.Join("\n", RoleNames.Primary.Select(r => $"{RoleNames.DisplayName(r)}: {AiChatPage.Guidance(r)}"))
                    : AiChatPage.Guidance(workspace),
                pages = pages.Select(page => page.Title),
                earlierQuestions,
                question,
                facts
            }, ct);
            return Ok(new { answer, source, sourceLabel = "Open source record" });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or
            KeyNotFoundException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(503, new { error = "The assistant is unavailable. Try again in a moment." });
        }
    }
}

public sealed record AiChatInput(
    [Required, StringLength(750)] string Question,
    [Required, StringLength(40)] string Workspace,
    [StringLength(40)] string? Page,
    int? RecordId,
    string[]? EarlierQuestions);
