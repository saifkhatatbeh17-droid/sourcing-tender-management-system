using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.TopCommittee;

// Database access for CEO & Chiefs voting.
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
    Task<T> ExecuteAsync<T>(string memberId,
        int tenderId,
        Func<ICommitteeVotingSession, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

internal sealed class TopCommitteeVotingStore(ITopCommitteeTenderTransaction transaction, ITopCommitteeVotingPause pause) : ITopCommitteeVotingStore
{
    public Task<T> ExecuteAsync<T>(string memberId,
        int tenderId,
        Func<ICommitteeVotingSession, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken) =>
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

    private sealed class Session(Data.ApplicationDbContext db,
        ITopCommitteeVotingPause pause,
        int tenderId,
        CommitteeVotingContext context) : ICommitteeVotingSession
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
