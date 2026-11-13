using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.TopCommittee;

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
