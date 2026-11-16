using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services;

public sealed class TenderClosureService(IServiceScopeFactory scopes, ILogger<TenderClosureService> logger) : BackgroundService
{
    public static Task<int> CloseExpired(ApplicationDbContext db, DateTime now, CancellationToken ct = default, string? ownerId = null) =>
        db.Tenders.Where(t => (t.Status == "Published" || t.Status == "Open") && t.ClosesAt <= now &&
            (ownerId == null || t.CreatedByUserId == ownerId))
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "Closed"), ct);

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
