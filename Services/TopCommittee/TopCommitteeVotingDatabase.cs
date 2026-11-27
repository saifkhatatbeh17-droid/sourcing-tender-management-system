using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.TopCommittee;

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
