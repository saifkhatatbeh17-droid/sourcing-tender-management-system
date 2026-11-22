using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Ai;

/// <summary>Where <see cref="AiGateway"/> keeps its log. The check harness uses an in-memory version.</summary>
public interface IAiInteractionStore
{
    Task AddAsync(AiInteraction interaction, CancellationToken ct);
    Task<int> CountInteractiveSinceAsync(string userId, DateTime sinceUtc, CancellationToken ct);
}

/// <summary>
/// Writes each entry through its own DbContext. The request's context is never used: Safi's workspace
/// actions change data through it, and saving it here could commit a half-finished action.
/// </summary>
public sealed class EfAiInteractionStore(IServiceScopeFactory scopes) : IAiInteractionStore
{
    public async Task AddAsync(AiInteraction interaction, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AiInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CountInteractiveSinceAsync(string userId, DateTime sinceUtc, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AiInteractions.AsNoTracking()
            .CountAsync(x => x.UserId == userId && x.IsInteractive && x.CreatedAtUtc >= sinceUtc
                             && x.Outcome != AiInteraction.LimitReached, ct);
    }
}

/// <summary>In-memory store for checks.</summary>
public sealed class InMemoryAiInteractionStore : IAiInteractionStore
{
    private readonly List<AiInteraction> entries = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AiInteraction> Entries
    {
        get { lock (gate) return entries.ToArray(); }
    }

    public Task AddAsync(AiInteraction interaction, CancellationToken ct)
    {
        lock (gate) entries.Add(interaction);
        return Task.CompletedTask;
    }

    public Task<int> CountInteractiveSinceAsync(string userId, DateTime sinceUtc, CancellationToken ct)
    {
        lock (gate)
            return Task.FromResult(entries.Count(x => x.UserId == userId && x.IsInteractive && x.CreatedAtUtc >= sinceUtc
                                                      && x.Outcome != AiInteraction.LimitReached));
    }
}
