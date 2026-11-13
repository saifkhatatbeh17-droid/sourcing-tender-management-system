using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

internal sealed record CommitteeClarificationContext(Tender? Tender, bool ActorIsActive,
    IReadOnlyList<string> ActorRoles, IReadOnlyList<CommitteeMember> Members, IReadOnlyList<CommitteeManager> Managers);

internal interface ICommitteeClarificationSession
{
    CommitteeClarificationContext Context { get; }
    void Add(Clarification question);
    void Add(ClarificationResponse response);
    Task SaveAsync(CancellationToken cancellationToken);
}

internal interface ITopCommitteeClarificationStore
{
    Task<int?> FindTenderAsync(string actorId, string role, int clarificationId, CancellationToken cancellationToken);
    Task<T> ExecuteAsync<T>(string actorId, int tenderId, Func<ICommitteeClarificationSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

internal sealed class TopCommitteeClarificationStore(ApplicationDbContext readContext, ITopCommitteeTenderTransaction transaction)
    : ITopCommitteeClarificationStore
{
    public async Task<int?> FindTenderAsync(string actorId, string role, int clarificationId, CancellationToken cancellationToken) =>
        await TopCommitteeClarificationScope.Query(readContext).Where(c => c.ClarificationId == clarificationId &&
            (role == RoleNames.TopCommittee ? c.SenderUserId == actorId : c.RecipientUserId == actorId))
            .Select(c => c.TenderId).SingleOrDefaultAsync(cancellationToken);

    public Task<T> ExecuteAsync<T>(string actorId, int tenderId,
        Func<ICommitteeClarificationSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(tenderId, async (db, ct) =>
        {
            var context = await LoadAsync(db, actorId, tenderId, ct);
            return await action(new Session(db, context), ct);
        }, cancellationToken);

    internal static async Task<CommitteeClarificationContext> LoadAsync(ApplicationDbContext db, string actorId, int tenderId, CancellationToken cancellationToken, bool tracking = true)
    {
        var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && u.IsActive, cancellationToken);
        var roles = await (from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == actorId select role.Name!).ToListAsync(cancellationToken);
        IQueryable<Tender> tenderQuery = db.Tenders.Include(t => t.TenderApprovals)
            .Include(t => t.Clarifications).ThenInclude(c => c.ClarificationResponses)
            .AsSplitQuery();
        if (!tracking) tenderQuery = tenderQuery.AsNoTracking();
        var tender = await tenderQuery.SingleOrDefaultAsync(t => t.TenderId == tenderId, cancellationToken);
        var access = new TopCommitteeAccessService(db);
        return new CommitteeClarificationContext(tender, active, roles,
            await access.GetMembersAsync(tenderId, cancellationToken), await access.GetManagersAsync(tenderId, cancellationToken));
    }

    private sealed class Session(ApplicationDbContext db, CommitteeClarificationContext context) : ICommitteeClarificationSession
    {
        public CommitteeClarificationContext Context => context;
        public void Add(Clarification question) => db.Clarifications.Add(question);
        public void Add(ClarificationResponse response) => db.ClarificationResponses.Add(response);
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await TopCommitteeNotificationEvents.ClarificationsAsync(db,context,cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
