using SourcingTenderManagement.Models.Entities;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services;

internal static class ProcurementVisibilityQueries
{
    public static IQueryable<PurchaseRequest> InDepartment(this IQueryable<PurchaseRequest> requests, int? departmentId) =>
        requests.Where(r => r.DepartmentId == departmentId);

    public static IQueryable<PurchaseRequest> ReviewableBy(this IQueryable<PurchaseRequest> requests, string userId) =>
        requests.Where(r => r.Status != "Draft" &&
            (r.ReviewedByUserId == userId || (r.ReviewedByUserId == null && r.Status == "Submitted")));

    public static IQueryable<Tender> InEvaluationWorkspace(this IQueryable<Tender> tenders, string userId) =>
        tenders.Where(t => t.Status == "UnderEvaluation" ||
            t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == userId));

    public static IQueryable<EvaluationReport> InManagerReview(this IQueryable<EvaluationReport> reports) =>
        reports.Where(r => r.Tender.Status == "AwaitingManagerReview" &&
            r.Tender.TopEditState == null &&
            !r.Tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected") &&
            (r.Status == "OfficerSubmitted" || r.Status == "CommitteeApproved" || r.Status == "ManagerApproved"));
}

internal static class ClarificationVisibility
{
    internal static IQueryable<Clarification> ForRecord(this IQueryable<Clarification> threads, string? kind, int id) =>
        kind switch
        {
            "request" => threads.Where(c => c.PurchaseRequestId == id),
            "tender" => threads.Where(c => c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)),
            "bid" => threads.Where(c => c.BidId == id),
            _ => threads.Where(c => false)
        };

    internal static IQueryable<Clarification> VisibleTo(this IQueryable<Clarification> threads, string userId, bool officer) =>
        threads.Where(c => c.SenderUserId == userId || c.RecipientUserId == userId ||
            (officer && c.PurchaseRequest != null &&
                c.SenderUserId == c.PurchaseRequest.RequestedByUserId &&
                (c.PurchaseRequest.ReviewedByUserId == userId ||
                 (c.PurchaseRequest.ReviewedByUserId == null && c.PurchaseRequest.Status == "Submitted"))));
}

internal static class ClarificationResolution
{
    internal const string Confirmation = "[Portal: resolution confirmed]";

    internal static HashSet<string> ConfirmedUsers(IEnumerable<ClarificationResponse> responses) =>
        responses.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ClarificationResponseId)
            .TakeWhile(r => r.Message == Confirmation)
            .Select(r => r.RespondedByUserId).ToHashSet();

    internal static ClarificationResponse CreateConfirmation(int id, string actorId) => new()
    {
        ClarificationId = id,
        RespondedByUserId = actorId,
        Message = Confirmation,
        CreatedAt = DateTime.UtcNow
    };
}

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
