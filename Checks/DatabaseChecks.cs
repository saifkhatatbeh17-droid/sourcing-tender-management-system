using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Checks;

// Run with: dotnet run -- --self-test. All test account changes are rolled back.
public static class DatabaseChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Departments.AsNoTracking().OrderBy(d => d.DepartmentId).Take(1).ToListAsync();
        await db.SupplierProfiles.AsNoTracking().OrderBy(s => s.SupplierProfileId).Take(1).ToListAsync();
        await db.CommitteeAssignments.AsNoTracking().OrderBy(c => c.CommitteeAssignmentId).Take(1).ToListAsync();

        await db.SpecMatchAnalyses.AsNoTracking().Take(1).ToListAsync();

        Require(!db.Database.HasPendingModelChanges(), "Model changes are missing a migration.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = $"check-{Guid.NewGuid():N}@example.invalid";
        var password = $"Check!{Guid.NewGuid():N}A1";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Temporary verification account" };
        var result = await users.CreateAsync(user, password);
        Require(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        Require(await users.CheckPasswordAsync(user, password), "Correct password failed.");
        Require(!await users.CheckPasswordAsync(user, "Wrong!Password1"), "Incorrect password was accepted.");
        result = await users.AddToRolesAsync(user, [RoleNames.TopCommittee, RoleNames.TopCommitteeMember1]);
        Require(result.Succeeded, "Committee role assignment failed.");
        Require(await users.IsInRoleAsync(user, RoleNames.TopCommittee), "Shared committee role missing.");
        Require(await users.IsInRoleAsync(user, RoleNames.TopCommitteeMember1), "Individual approval role missing.");
        Require(!await users.IsInRoleAsync(user, RoleNames.Admin), "Unexpected administrator role.");
        await transaction.RollbackAsync();
        Console.WriteLine("PASS: mapped schema matches migrations; live database reads including saved spec matches; Identity password validation; multiple committee roles. Test account rolled back.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
