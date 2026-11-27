using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services;

// Background job that closes tenders automatically when their offer deadline passes.
public sealed class TenderClosureService(IServiceScopeFactory scopes, ILogger<TenderClosureService> logger) : BackgroundService
{
    public static Task<int> CloseExpired(ApplicationDbContext db, DateTime now, CancellationToken ct = default, string? ownerId = null) =>
        db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE dbo.Tenders SET Status = N'Closed'
            OUTPUT {{now}}, N'System', N'Modified', N'Tender', CONVERT(nvarchar(450), inserted.TenderId),
                inserted.TenderId, N'Tender closed at its deadline.',
                CONCAT(N'{"Status":{"Before":"', deleted.Status, N'","After":"Closed"}' + N'}')
            INTO dbo.AuditEntries (OccurredAt, UserName, Action, EntityType, EntityId, TenderId, Summary, ChangesJson)
            WHERE Status IN (N'Published',N'Open') AND ClosesAt <= {{now}}
                AND ({{ownerId}} IS NULL OR CreatedByUserId = {{ownerId}});
            """, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await CloseExpired(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Could not close expired tenders; the next scheduled attempt will retry."); }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        } while (!stoppingToken.IsCancellationRequested);
    }
}
