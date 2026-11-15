using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

// Pure preflight rules. Future write operations must recheck them under the tender transaction lock.
internal static class TopCommitteeRules
{
    internal static bool HasFourMembers(IReadOnlyList<CommitteeMember> members) =>
        members.Count == 4 && members.All(m => m.IsEligible && m.Position is >= 1 and <= 4) &&
        members.Select(m => m.UserId).Distinct().Count() == 4 &&
        members.Select(m => m.AssignmentId).Distinct().Count() == 4 &&
        members.Select(m => m.Position).Distinct().Count() == 4;

    internal static bool IsEligibleNomination(Bid? bid, int tenderId) =>
        bid != null && bid.TenderId == tenderId && bid.SubmittedAt.HasValue &&
        bid.Status is not ("Draft" or "Withdrawn" or "Rejected" or "NonCompliant") &&
        bid.ComplianceStatus != "NonCompliant";

    internal static bool HasValidVotes(Tender tender, IReadOnlyList<CommitteeMember> members)
    {
        var votes = tender.TenderApprovals.Where(v => v.Stage == "Top").ToList();
        return votes.Count <= 4 && votes.Select(v => v.UserId).Distinct().Count() == votes.Count &&
            votes.Select(v => v.CommitteeAssignmentId).Distinct().Count() == votes.Count &&
            votes.All(v => v.TenderId == tender.TenderId && (v.Decision is "Approved" or "Rejected") &&
                members.Any(m => m.IsEligible && m.UserId == v.UserId && m.AssignmentId == v.CommitteeAssignmentId));
    }

    internal static CommitteeReadiness Inspect(Tender tender, IReadOnlyList<CommitteeMember> members,
        IReadOnlyList<CommitteeManager> managers, Bid? nomination, int openClarifications, string currentUserId)
    {
        var fourMembers = HasFourMembers(members);
        var validNomination = tender.EvaluationReport?.RecommendedBidId == nomination?.BidId &&
            IsEligibleNomination(nomination, tender.TenderId);
        var manager = managers.Count == 1 && managers[0].IsActive && managers[0].HasManagerRole;
        var votes = tender.TenderApprovals.Where(a => a.Stage == "Top").ToList();
        var rejected = votes.Any(a => a.Decision == "Rejected");
        var voted = votes.Any(a => a.UserId == currentUserId);
        var assigned = members.Any(m => m.UserId == currentUserId && m.IsEligible);

        var reason = rejected ? "Committee voting has permanently ended after a rejection."
            : tender.Status != "AwaitingCommitteeDecision" ? "Tender is not awaiting a committee decision."
            : tender.AwardedBidId.HasValue || tender.AwardedAt.HasValue ? "An award has already been recorded."
            : !fourMembers ? "Exactly four distinct, active CEO & Chiefs members must be assigned."
            : !HasValidVotes(tender, members) ? "Recorded votes do not match the current committee assignments. Contact the system administrator."
            : !manager ? "One active referring Sourcing Manager with a recorded approval is required."
            : !validNomination ? "A submitted, eligible bid from this tender must be nominated."
            : string.IsNullOrWhiteSpace(tender.EvaluationReport?.ManagerSummary) ? "The manager recommendation is missing."
            : openClarifications > 0 ? "Voting is paused until all relevant clarifications are resolved."
            : null;

        return new CommitteeReadiness(fourMembers, validNomination, manager, rejected, reason == null,
            voted, reason == null && assigned && !voted,
            reason ?? (!assigned ? "You are not an eligible assigned member." : voted ? "Your final vote is already recorded." : null));
    }
}
