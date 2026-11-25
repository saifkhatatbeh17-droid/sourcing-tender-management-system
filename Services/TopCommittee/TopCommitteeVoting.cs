using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

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

internal sealed record CommitteeVotingContext(CommitteeClarificationContext Access, Bid? Nomination);

internal interface ICommitteeVotingSession
{
    CommitteeVotingContext Context { get; }
    Task AssertNotPausedAsync(CancellationToken cancellationToken);
    Task<bool> IsDatabaseReadyAsync(CancellationToken cancellationToken);
    void Add(TenderApproval vote);
    void ClearTopVotes() { }
    Task SaveAsync(CancellationToken cancellationToken);
}

internal interface ITopCommitteeVotingStore
{
    Task<T> ExecuteAsync<T>(string memberId, int tenderId, Func<ICommitteeVotingSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

internal sealed class TopCommitteeVotingStore(ITopCommitteeTenderTransaction transaction, ITopCommitteeVotingPause pause) : ITopCommitteeVotingStore
{
    public Task<T> ExecuteAsync<T>(string memberId, int tenderId, Func<ICommitteeVotingSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(tenderId, async (db, ct) =>
        {
            // Every identity/state read and write uses the coordinator's locked context.
            var access = await TopCommitteeClarificationStore.LoadAsync(db, memberId, tenderId, ct);
            Bid? nomination = null;
            if (access.Tender is { } tender)
            {
                await db.Entry(tender).Reference(t => t.EvaluationReport).LoadAsync(ct);
                if (tender.EvaluationReport?.RecommendedBidId is { } bidId)
                    nomination = await db.Bids.AsNoTracking().SingleOrDefaultAsync(b => b.BidId == bidId, ct);
            }
            return await action(new Session(db, pause, tenderId, new(access, nomination)), ct);
        }, cancellationToken);

    private sealed class Session(Data.ApplicationDbContext db, ITopCommitteeVotingPause pause, int tenderId, CommitteeVotingContext context) : ICommitteeVotingSession
    {
        public CommitteeVotingContext Context => context;
        public Task AssertNotPausedAsync(CancellationToken cancellationToken) => pause.AssertNotPausedAsync(db, tenderId, cancellationToken);
        public Task<bool> IsDatabaseReadyAsync(CancellationToken cancellationToken) => TopCommitteeVotingDatabase.IsReadyAsync(db, cancellationToken);
        public void Add(TenderApproval vote) => db.TenderApprovals.Add(vote);
        public void ClearTopVotes()
        {
            if (context.Access.Tender is not { } tender) return;
            var existingTop = tender.TenderApprovals.Where(v => v.Stage == "Top" && v.Decision != "Rejected").ToList();
            if (existingTop.Count > 0)
            {
                db.TenderApprovals.RemoveRange(existingTop);
                foreach (var v in existingTop) tender.TenderApprovals.Remove(v);
            }
        }
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await TopCommitteeNotificationEvents.DecisionAsync(db,context,cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class TopCommitteeVotingDatabase(ApplicationDbContext db, TopCommitteeAccessService access) : ITopCommitteeVotingReadiness
{
    internal const string Unavailable = "Voting is temporarily unavailable. Contact the system administrator, then reload this page.";
    internal const string OldConstraint = "([Stage]='Manager' OR [Decision]='Approved')";
    internal const string NewConstraint = "([Stage]='Manager' OR [Decision]='Approved' OR ([Stage]='Top' AND [Decision]='Rejected'))";

    public async Task<bool> IsDatabaseReadyAsync(ClaimsPrincipal member, int tenderId, CancellationToken cancellationToken = default)
    {
        await access.RequireAssignmentAsync(member, tenderId, cancellationToken);
        return await IsReadyAsync(db, cancellationToken);
    }

    internal static bool AcceptsDefinition(string? definition)
    {
        if (definition == null) return false;
        var normalized = string.Concat(definition.Where(c => !char.IsWhiteSpace(c) && c is not ('[' or ']'))).ToLowerInvariant();
        // SQL Server can omit the redundant AND-group parentheses. Keep all other grouping strict.
        return normalized is "(stage='manager'ordecision='approved')"
            or "(stage='manager'ordecision='approved'or(stage='top'anddecision='rejected'))"
            or "(stage='manager'ordecision='approved'orstage='top'anddecision='rejected')";
    }

    internal static async Task<bool> IsReadyAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT [definition] FROM sys.check_constraints WHERE [parent_object_id]=OBJECT_ID(N'dbo.TenderApprovals') AND [name]=N'CK_TenderApprovals_3' AND [is_disabled]=0 AND [is_not_trusted]=0;";
            var definition = await command.ExecuteScalarAsync(cancellationToken) as string;
            return AcceptsDefinition(definition) && definition!.Contains("Rejected", StringComparison.OrdinalIgnoreCase);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }
}

public sealed class TopCommitteeVotingPause : ITopCommitteeVotingPause
{
    public async Task AssertNotPausedAsync(ApplicationDbContext lockedContext, int tenderId, CancellationToken cancellationToken = default)
    {
        await TopCommitteeTenderTransaction.RequireLockAsync(lockedContext, tenderId, cancellationToken);
        if ((await ReadAsync(lockedContext, tenderId, cancellationToken)).IsPaused)
            throw new CommitteeStateConflictException("Voting is paused until all relevant clarifications are resolved.");
    }

    internal static async Task<CommitteeVotingPause> ReadAsync(ApplicationDbContext db, int tenderId, CancellationToken cancellationToken) =>
        new(await TopCommitteeClarificationScope.Query(db).CountAsync(c => c.TenderId == tenderId &&
            (c.Status == "Pending" || c.Status == "Answered"), cancellationToken));
}

public sealed class TopCommitteeTenderTransaction(DbContextOptions<ApplicationDbContext> options) : ITopCommitteeTenderTransaction
{
    internal static string Resource(int tenderId) => $"TopCommittee:Tender:{tenderId}";

    public async Task<T> ExecuteAsync<T>(int tenderId, Func<ApplicationDbContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (tenderId <= 0) throw new CommitteeTenderNotFoundException();
        // A fresh context avoids committing or clearing changes belonging to another caller.
        await using var db = new ApplicationDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await using (var command = Command(db, "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;"))
            {
                Add(command, "@resource", Resource(tenderId));
                var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                if (result < 0) throw new CommitteeStateConflictException("This tender is being updated. Reload and try again.");
            }
            // Also protect the tender row against ordinary SQL updates for the duration of the command.
            await using (var command = Command(db, "SELECT [TenderId] FROM [dbo].[Tenders] WITH (UPDLOCK, ROWLOCK) WHERE [TenderId]=@id;"))
            {
                Add(command, "@id", tenderId);
                await command.ExecuteScalarAsync(cancellationToken);
            }
            var value = await action(db, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return value;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal static async Task RequireLockAsync(ApplicationDbContext db, int tenderId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("A committee tender transaction is required.");
        await using var command = Command(db, "SELECT APPLOCK_MODE('public', @resource, 'Transaction');");
        Add(command, "@resource", Resource(tenderId));
        if (!string.Equals(await command.ExecuteScalarAsync(cancellationToken) as string, "Exclusive", StringComparison.Ordinal))
            throw new InvalidOperationException("The exclusive committee tender lock is required.");
    }

    private static DbCommand Command(ApplicationDbContext db, string text)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = text;
        return command;
    }
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

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

public sealed class TopCommitteeAccessService(ApplicationDbContext db)
{
    public async Task<string> RequireRoleAsync(ClaimsPrincipal actor, string role,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actor.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId) || !actor.IsInRole(role))
            throw new CommitteeAccessDeniedException();

        // Check the database as well as the cookie: disabled accounts and revoked roles cannot use services.
        var permitted = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive &&
            db.UserRoles.Any(ur => ur.UserId == u.Id &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)), cancellationToken);
        if (!permitted) throw new CommitteeAccessDeniedException();
        return userId;
    }

    public async Task<string> RequireAssignmentAsync(ClaimsPrincipal actor, int tenderId,
        CancellationToken cancellationToken = default)
    {
        var userId = await RequireRoleAsync(actor, RoleNames.TopCommittee, cancellationToken);
        if (tenderId <= 0 || !await db.CommitteeAssignments.AsNoTracking().AnyAsync(a =>
                a.TenderId == tenderId && a.UserId == userId && a.CommitteeType == "Top", cancellationToken))
            throw new CommitteeTenderNotFoundException();
        return userId;
    }

    internal async Task<IReadOnlyList<CommitteeMember>> GetMembersAsync(int tenderId,
        CancellationToken cancellationToken = default) =>
        await db.CommitteeAssignments.AsNoTracking().Where(a => a.TenderId == tenderId && a.CommitteeType == "Top")
            .OrderBy(a => a.Position).Select(a => new CommitteeMember(a.CommitteeAssignmentId, a.UserId,
                a.User.FullName, a.Position, a.User.IsActive,
                db.UserRoles.Any(ur => ur.UserId == a.UserId &&
                    db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))))
            .ToListAsync(cancellationToken);

    internal async Task<IReadOnlyList<CommitteeManager>> GetManagersAsync(int tenderId,
        CancellationToken cancellationToken) =>
        await db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Stage == "Manager" && a.Decision == "Approved")
            .Select(a => new CommitteeManager(a.UserId, a.User.FullName, a.User.IsActive,
                db.UserRoles.Any(ur => ur.UserId == a.UserId &&
                    db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingManager)), a.DecidedAt))
            .ToListAsync(cancellationToken);
}
