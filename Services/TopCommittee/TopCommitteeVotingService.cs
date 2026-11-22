using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

internal sealed class TopCommitteeVotingService(ITopCommitteeVotingStore store) : ITopCommitteeVoting
{
    public async Task<CommitteeVoteResult> SubmitAsync(ClaimsPrincipal member, CommitteeVoteInput input, CancellationToken cancellationToken = default)
    {
        var memberId = member.FindFirstValue(ClaimTypes.NameIdentifier);
        if (member.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(memberId) || !member.IsInRole(RoleNames.TopCommittee))
            throw new CommitteeAccessDeniedException();
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var notes = input.Decision is "Rejected" or "RequestEdit" ? input.Notes?.Replace("\r\n", "\n").Trim() : input.Notes?.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(notes)) notes = null;
        try
        {
            return await store.ExecuteAsync(memberId, input.TenderId, async (session, ct) =>
            {
                var context = session.Context;
                var access = context.Access;
                if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.TopCommittee)) throw new CommitteeAccessDeniedException();
                var assignment = access.Members.FirstOrDefault(m => m.UserId == memberId && m.IsEligible);
                if (access.Tender == null || assignment == null) throw new CommitteeTenderNotFoundException();
                var tender = access.Tender;
                var readiness = TopCommitteeRules.Inspect(tender, access.Members, access.Managers, context.Nomination, 0, memberId);
                if (!readiness.CanCurrentMemberVote)
                    throw new CommitteeStateConflictException(readiness.BlockingReason ?? "A final vote cannot be recorded for this tender.");
                if (context.Nomination!.BidId != input.ExpectedNominatedBidId)
                    throw new CommitteeStateConflictException("The Manager nomination changed. Reload the tender and review it before voting.");
                await session.AssertNotPausedAsync(ct);
                // Validate the existing database constraint before recording an approval.
                if (!await session.IsDatabaseReadyAsync(ct)) throw new CommitteeStateConflictException(TopCommitteeVotingDatabase.Unavailable);
                var previous = tender.TenderApprovals.Where(v => v.Stage == "Top").ToList();
                var now = DateTime.UtcNow;

                if (input.Decision == "RequestEdit")
                {
                    session.ClearTopVotes();
                    tender.Status = "AwaitingManagerReview";
                    tender.TopEditState = "WithManager";
                    tender.TopEditRequestedByUserId = memberId;
                    tender.TopEditRequestedAt = now;
                    tender.TopEditRequestNotes = notes;
                    tender.TopEditManagerNotes = null;
                    tender.TopEditOfficerNotes = null;
                    tender.TopEditOfficerRespondedAt = null;
                    await session.SaveAsync(ct);
                    return new CommitteeVoteResult(tender.TenderId, "RequestEdit", false);
                }

                session.Add(new TenderApproval { TenderId = tender.TenderId, UserId = memberId,
                    CommitteeAssignmentId = assignment.AssignmentId, Stage = "Top", Decision = input.Decision,
                    Notes = notes, DecidedAt = now });
                var award = input.Decision == "Approved" && previous.Count == 3 && previous.All(v => v.Decision == "Approved");
                if (input.Decision == "Rejected")
                {
                    tender.Status = "AwaitingManagerReview";
                    tender.TopEditState = null;
                }
                if (award)
                {
                    tender.Status = "Awarded";
                    tender.AwardedBidId = context.Nomination.BidId;
                    tender.AwardedAt = now;
                    tender.TopEditState = null;
                    tender.AwardNotes = $"Automatically awarded to nominated bid #{context.Nomination.BidId} after four valid CEO & Chiefs approvals.";
                }
                await session.SaveAsync(ct);
                return new CommitteeVoteResult(tender.TenderId, input.Decision, award);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { throw new CommitteeStateConflictException("The tender changed. Reload to see its recorded decision."); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new CommitteeStateConflictException("A final vote is already recorded. Reload to view it."); }
    }
}
