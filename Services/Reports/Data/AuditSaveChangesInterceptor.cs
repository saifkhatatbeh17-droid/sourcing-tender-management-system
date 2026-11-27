using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Reports.Data;

// Writes an audit-log entry automatically whenever one of the audited record types (listed in Audited below) is saved.
public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor accessor) : SaveChangesInterceptor
{
    private sealed record Change(EntityEntry Entry, string Action, string? Values, object? OriginalKey, int? OriginalTenderId);

    private static readonly HashSet<Type> Audited =
    [
        typeof(PurchaseRequest), typeof(Tender), typeof(Invitation), typeof(Bid), typeof(EvaluationReport),
        typeof(Models.Entities.Evaluation), typeof(TenderApproval), typeof(CommitteeWeightProposal), typeof(CommitteeTieVote),
        typeof(CommitteeAssignment), typeof(Clarification), typeof(SupplierProfile), typeof(SupplierDocument),
        typeof(Department), typeof(ApplicationUser), typeof(IdentityUserRole<string>), typeof(TenderContract)
    ];

    private List<Change> changes = [];
    private IDbContextTransaction? transaction;

    public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result)
    {
        if (data.Context is { } context && Capture(context) && context.Database.CurrentTransaction is null)
            transaction = context.Database.BeginTransaction();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context is { } context && Capture(context) && context.Database.CurrentTransaction is null)
            transaction = await context.Database.BeginTransactionAsync(ct);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData data, int result)
    {
        try
        {
            foreach (var change in changes)
                data.Context!.Database.ExecuteSqlInterpolated(AuditLog.Insert(Entry(change)));
            transaction?.Commit();
            return result;
        }
        finally { Reset(); }
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
    {
        try
        {
            foreach (var change in changes)
                await data.Context!.Database.ExecuteSqlInterpolatedAsync(AuditLog.Insert(Entry(change)), ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        finally { await ResetAsync(); }
    }

    public override void SaveChangesFailed(DbContextErrorEventData data) => Reset();
    public override void SaveChangesCanceled(DbContextEventData data) => Reset();
    public override Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken ct = default) => ResetAsync().AsTask();
    public override Task SaveChangesCanceledAsync(DbContextEventData data, CancellationToken ct = default) => ResetAsync().AsTask();

    private void Reset()
    {
        transaction?.Dispose();
        transaction = null;
        changes.Clear();
    }

    private async ValueTask ResetAsync()
    {
        if (transaction is not null) await transaction.DisposeAsync();
        transaction = null;
        changes.Clear();
    }

    private bool Capture(DbContext context)
    {
        changes = context.ChangeTracker.Entries().Where(e => Audited.Contains(e.Metadata.ClrType) &&
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e =>
            {
                var modified = e.State == EntityState.Modified;
                var fields = e.Properties.Where(p => Allowed(e, p.Metadata.Name) && (!modified || p.IsModified))
                    .ToDictionary(p => p.Metadata.Name, p => new
                    {
                        Before = e.State == EntityState.Added ? null : Safe(p.OriginalValue),
                        After = e.State == EntityState.Deleted ? null : Safe(p.CurrentValue)
                    });
                var key = e.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name;
                return new Change(e, e.State.ToString(), fields.Count == 0 ? null : JsonSerializer.Serialize(fields),
                    key is null ? null : e.Property(key).OriginalValue, TenderId(e));
            }).Where(c => c.Values is not null).ToList();
        return changes.Count != 0;
    }

    private AuditEntry Entry(Change change)
    {
        var e = change.Entry;
        var key = e.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name;
        var id = (change.Action == "Deleted"
            || key is null ? change.OriginalKey : e.Property(key).CurrentValue)?.ToString()
            ?? "";
        var tender = change.Action == "Deleted" ? change.OriginalTenderId : TenderId(e);
        var roleChange = e.Entity is IdentityUserRole<string>;
        var type = roleChange ? nameof(ApplicationUser) : e.Metadata.ClrType.Name;
        var action = roleChange ? "Account.Role" + change.Action : change.Action;
        return AuditLog.Create(accessor, action, type, id, $"{type} {change.Action.ToLowerInvariant()}", tender, change.Values);
    }

    private static int? TenderId(EntityEntry entry) => entry.Metadata.FindProperty("TenderId") is null
        ? null : entry.Property("TenderId").CurrentValue as int?;

    private static object? Safe(object? value) => value is string text ? AuditLog.Limit(text) : value;

    private static bool Allowed(EntityEntry e, string name)
    {
        if (e.Entity is ApplicationUser) return name is nameof(ApplicationUser.IsActive) or nameof(ApplicationUser.DepartmentId);
        return !e.Property(name).Metadata.IsPrimaryKey() || e.Entity is IdentityUserRole<string>
            ? !name.Contains("StoredFile", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Token", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Hash", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Stamp", StringComparison.OrdinalIgnoreCase) &&
              !name.Contains("Json", StringComparison.OrdinalIgnoreCase) &&
              e.Property(name).Metadata.ClrType != typeof(byte[])
            : false;
    }
}
