using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

internal sealed class TopCommitteeClarificationService(ITopCommitteeClarificationStore store)
    : ITopCommitteeClarifications, ITopCommitteeManagerAnswers
{
    private static string Actor(ClaimsPrincipal principal, string role)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id) || !principal.IsInRole(role))
            throw new CommitteeAccessDeniedException();
        return id;
    }
    private static string Text(string? value, int maxLength, string label)
    {
        var text = (value ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0 || text.Length > maxLength) throw new ValidationException($"{label} must contain 1–{maxLength} characters.");
        return text;
    }

    public Task<CommitteeClarificationResult> CreateAsync(ClaimsPrincipal actor, CommitteeCreateClarification input,
        CancellationToken cancellationToken = default)
    {
        var memberId = Actor(actor, RoleNames.TopCommittee);
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var subject = Text(input.Subject, 200, "Subject");
        var description = Text(input.Description, 4000, "Description");
        return store.ExecuteAsync(memberId, input.TenderId, async (session, ct) =>
        {
            var context = session.Context;
            var manager = TopCommitteeClarificationRules.RequireCreate(context, memberId);
            if (context.Tender!.Clarifications.Any(c => TopCommitteeClarificationScope.Contains(c, context) &&
                c.SenderUserId == memberId && c.Subject == subject && c.Description == description))
                throw new CommitteeStateConflictException("You have already submitted this question for this tender. Review the existing conversation instead.");
            var question = new Clarification { TenderId = input.TenderId, SenderUserId = memberId,
                RecipientUserId = manager.UserId, Subject = subject, Description = description,
                Status = "Pending", CreatedAt = DateTime.UtcNow };
            session.Add(question);
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(input.TenderId, question.ClarificationId);
        }, cancellationToken);
    }

    public async Task<CommitteeClarificationResult> AnswerAsync(ClaimsPrincipal manager, int clarificationId, string answer,
        CancellationToken cancellationToken = default)
    {
        var managerId = Actor(manager, RoleNames.SourcingManager);
        var message = Text(answer, 4000, "Answer");
        if (message == ClarificationResolution.Confirmation) throw new ValidationException("Enter an answer.");
        var tenderId = await store.FindTenderAsync(managerId, RoleNames.SourcingManager, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(managerId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireAnswer(session.Context, managerId, clarificationId);
            session.Add(new ClarificationResponse { ClarificationId = clarificationId, RespondedByUserId = managerId,
                Message = message, CreatedAt = DateTime.UtcNow });
            question.Status = "Answered";
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }

    public async Task<CommitteeClarificationResult> FollowUpAsync(ClaimsPrincipal actor, int clarificationId, string message,
        CancellationToken cancellationToken = default)
    {
        var memberId = Actor(actor, RoleNames.TopCommittee);
        var reply = Text(message, 4000, "Reply");
        if (reply == ClarificationResolution.Confirmation) throw new ValidationException("Enter a reply.");
        var tenderId = await store.FindTenderAsync(memberId, RoleNames.TopCommittee, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(memberId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireFollowUp(session.Context, memberId, clarificationId);
            session.Add(new ClarificationResponse { ClarificationId = clarificationId, RespondedByUserId = memberId,
                Message = reply, CreatedAt = DateTime.UtcNow });
            question.Status = "Pending";
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }

    public async Task<CommitteeClarificationResult> ConfirmResolutionAsync(ClaimsPrincipal manager, int clarificationId,
        CancellationToken cancellationToken = default)
    {
        var managerId = Actor(manager, RoleNames.SourcingManager);
        var tenderId = await store.FindTenderAsync(managerId, RoleNames.SourcingManager, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(managerId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireManagerConfirmation(session.Context, managerId, clarificationId);
            Confirm(session, question, managerId, question.SenderUserId);
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }

    private static void Confirm(ICommitteeClarificationSession session, Clarification question, string actor, string other)
    {
        var confirmations = ClarificationResolution.ConfirmedUsers(question.ClarificationResponses);
        if (confirmations.Contains(actor))
            throw new CommitteeStateConflictException("You already marked this clarification as resolved.");
        session.Add(ClarificationResolution.CreateConfirmation(question.ClarificationId, actor));
        if (confirmations.Contains(other))
        {
            question.Status = "Resolved";
            question.ResolvedAt = DateTime.UtcNow;
        }
    }

    public async Task<CommitteeClarificationResult> ResolveAsync(ClaimsPrincipal actor, int clarificationId,
        CancellationToken cancellationToken = default)
    {
        var memberId = Actor(actor, RoleNames.TopCommittee);
        var tenderId = await store.FindTenderAsync(memberId, RoleNames.TopCommittee, clarificationId, cancellationToken)
            ?? throw new CommitteeTenderNotFoundException();
        return await store.ExecuteAsync(memberId, tenderId, async (session, ct) =>
        {
            var question = TopCommitteeClarificationRules.RequireResolve(session.Context, memberId, clarificationId);
            Confirm(session, question, memberId, question.RecipientUserId);
            await session.SaveAsync(ct);
            return new CommitteeClarificationResult(tenderId, clarificationId);
        }, cancellationToken);
    }
}
