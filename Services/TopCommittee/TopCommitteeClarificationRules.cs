using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

internal static class TopCommitteeClarificationRules
{
    private static void RequireActor(CommitteeClarificationContext context, string role)
    {
        if (!context.ActorIsActive || !context.ActorRoles.Contains(role)) throw new CommitteeAccessDeniedException();
    }

    private static Tender RequireOpen(CommitteeClarificationContext context)
    {
        var tender = context.Tender ?? throw new CommitteeTenderNotFoundException();
        if (tender.Status != "AwaitingCommitteeDecision" || tender.AwardedBidId.HasValue || tender.AwardedAt.HasValue ||
            tender.TenderApprovals.Any(v => v.Stage == "Top" && v.Decision == "Rejected"))
            throw new CommitteeStateConflictException("This tender is no longer open for committee clarifications.");
        return tender;
    }

    internal static CommitteeManager ResponsibleManager(CommitteeClarificationContext context)
    {
        if (context.Managers.Count != 1 || !context.Managers[0].IsActive || !context.Managers[0].HasManagerRole)
            throw new CommitteeStateConflictException("A single active referring Sourcing Manager must be recorded before a clarification can be processed.");
        return context.Managers[0];
    }

    internal static CommitteeManager RequireCreate(CommitteeClarificationContext context, string memberId)
    {
        RequireActor(context, RoleNames.TopCommittee);
        if (context.Tender == null || !context.Members.Any(m => m.UserId == memberId && m.IsEligible))
            throw new CommitteeTenderNotFoundException();
        var tender = RequireOpen(context);
        if (tender.TenderApprovals.Any(v => v.Stage == "Top" && v.UserId == memberId))
            throw new CommitteeStateConflictException("You cannot create a clarification after recording your final vote.");
        var manager = ResponsibleManager(context);
        if (manager.UserId == memberId)
            throw new CommitteeStateConflictException("A clarification must be addressed to a different responsible Sourcing Manager.");
        return manager;
    }

    internal static Clarification RequireQuestion(CommitteeClarificationContext context, int clarificationId)
    {
        var question = context.Tender?.Clarifications.SingleOrDefault(c => c.ClarificationId == clarificationId);
        if (question == null || !TopCommitteeClarificationScope.Contains(question, context))
            throw new CommitteeTenderNotFoundException();
        return question;
    }

    internal static Clarification RequireAnswer(CommitteeClarificationContext context, string managerId, int clarificationId)
    {
        RequireActor(context, RoleNames.SourcingManager);
        var question = RequireQuestion(context, clarificationId);
        if (question.RecipientUserId != managerId) throw new CommitteeTenderNotFoundException();
        RequireOpen(context);
        if (ResponsibleManager(context).UserId != managerId) throw new CommitteeTenderNotFoundException();
        if (question.Status != "Pending" || question.ResolvedAt.HasValue || question.ClarificationResponses.Count != 0)
            throw new CommitteeStateConflictException("This clarification has already been answered or resolved. Reload to view its current status.");
        return question;
    }

    internal static Clarification RequireResolve(CommitteeClarificationContext context, string memberId, int clarificationId)
    {
        RequireActor(context, RoleNames.TopCommittee);
        var question = RequireQuestion(context, clarificationId);
        if (question.SenderUserId != memberId || !context.Members.Any(m => m.UserId == memberId && m.IsEligible))
            throw new CommitteeTenderNotFoundException();
        RequireOpen(context);
        var manager = ResponsibleManager(context);
        if (question.Status != "Answered" || question.ResolvedAt.HasValue ||
            !question.ClarificationResponses.Any(r => r.RespondedByUserId == manager.UserId && !string.IsNullOrWhiteSpace(r.Message)))
            throw new CommitteeStateConflictException("Only an answered clarification with a valid Manager response can be resolved, once.");
        return question;
    }
}
