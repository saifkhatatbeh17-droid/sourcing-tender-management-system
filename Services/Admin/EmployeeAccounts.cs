using SourcingTenderManagement.Services.Reports.Data;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.Admin;

// Creates staff accounts with the right role and committee seat, with checks on every field.
public sealed record EmployeeAccountInput(
    string FullName,
    string Email,
    string? PhoneNumber,
    string Role,
    string? CommitteeRole,
    int? DepartmentId);

public sealed record EmployeeAccountError(string Field, string Message);

public sealed record EmployeeAccountResult(ApplicationUser? User, string? PositionHolder, IReadOnlyList<string> Errors)
{
    public bool Succeeded => User is not null;
}

/// <summary>
/// The single set of rules for creating employee accounts. Used by the Users page form and by the
/// spreadsheet import, so both enforce the same role, department and committee-position checks.
/// </summary>
public sealed class EmployeeAccounts(ApplicationDbContext db, UserManager<ApplicationUser> users, IAuditLog? audit = null)
{
    /// <summary>Claim added to accounts created with a temporary password; cleared when the user changes it.</summary>
    public const string PasswordChangeRequiredClaim = "tiryaq:password_change_required";

    /// <summary>Roles the Users page form may create.</summary>
    public static readonly string[] FormRoles = RoleNames.Primary.Where(r => r != RoleNames.Supplier).ToArray();

    /// <summary>
    /// Roles a spreadsheet import may create. Admin is excluded on purpose: administrator accounts are
    /// created one at a time on the Users page. Suppliers register themselves.
    /// </summary>
    public static readonly string[] ImportRoles = FormRoles.Where(r => r != RoleNames.Admin).ToArray();

    public async Task<IReadOnlyList<EmployeeAccountError>> ValidateAsync(
        EmployeeAccountInput input, IReadOnlyCollection<string> allowedRoles, CancellationToken ct = default)
    {
        var errors = new List<EmployeeAccountError>();
        if (!allowedRoles.Contains(input.Role))
            errors.Add(new(nameof(input.Role), "Choose an employee role."));
        if (input.Role == RoleNames.Department)
        {
            if (!input.DepartmentId.HasValue ||
                !await db.Departments.AnyAsync(d => d.DepartmentId == input.DepartmentId && d.IsActive, ct))
                errors.Add(new("DepartmentId", "Choose an active department."));
        }

        var positions = input.Role == RoleNames.TopCommittee ? RoleNames.TopPositions : RoleNames.EvaluationPositions;
        if (input.CommitteeRole != null && !positions.Contains(input.CommitteeRole))
            errors.Add(new(nameof(input.CommitteeRole), "Choose a matching committee position."));
        if (input.Role == RoleNames.TopCommittee || input.Role == RoleNames.EvaluationCommittee)
        {
            if (input.CommitteeRole == null)
                errors.Add(new(nameof(input.CommitteeRole), "Select this member's position."));
        }
        if (input.CommitteeRole != null && input.Role != RoleNames.TopCommittee &&
            input.Role != RoleNames.EvaluationCommittee && input.Role != RoleNames.SourcingManager)
            errors.Add(new(nameof(input.CommitteeRole), "This account is not a committee member."));
        return errors;
    }

    /// <summary>
    /// Creates the account and its roles in one strict (serializable) database transaction, so all steps succeed or none do. If the committee position is
    /// already held by an active account, nothing is created and <see cref="EmployeeAccountResult.PositionHolder"/> is set.
    /// </summary>
    public async Task<EmployeeAccountResult> CreateAsync(
        EmployeeAccountInput input, string password, bool requirePasswordChange, CancellationToken ct = default)
    {
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (input.CommitteeRole != null && await ActivePositionHolderAsync(input.CommitteeRole, ct) is string holder)
            return new EmployeeAccountResult(null, holder, []);

        var user = new ApplicationUser
        {
            UserName = input.Email,
            Email = input.Email,
            FullName = input.FullName,
            PhoneNumber = input.PhoneNumber?.Trim(),
            DepartmentId = input.Role == RoleNames.Department ? input.DepartmentId : null,
            EmailConfirmed = true
        };
        var result = await users.CreateAsync(user, password);
        if (result.Succeeded)
        {
            var assignedRoles = new List<string> { input.Role };
            if (input.CommitteeRole != null)
            {
                assignedRoles.Add(input.CommitteeRole);
                if (RoleNames.EvaluationPositions.Contains(input.CommitteeRole)) assignedRoles.Add(RoleNames.EvaluationCommittee);
            }
            result = await users.AddToRolesAsync(user, assignedRoles.Distinct());
        }
        if (result.Succeeded && requirePasswordChange)
            result = await users.AddClaimAsync(user, new Claim(PasswordChangeRequiredClaim, "true"));
        if (!result.Succeeded)
            return new EmployeeAccountResult(null, null, result.Errors.Select(e => e.Description).ToArray());

        if (audit is not null) await audit.WriteAsync("Account.Create", "ApplicationUser", user.Id, "Employee account created.", ct: ct);
        await transaction.CommitAsync(ct);
        return new EmployeeAccountResult(user, null, []);
    }

    /// <summary>
    /// Replaces an employee's password with a new temporary one and requires them to change it at next sign-in.
    /// Used when an import's password file was lost or a person forgot their password.
    /// Not for the admin's own account, other administrators or suppliers.
    /// Returns the temporary password (to be shown once) or an error message.
    /// </summary>
    public async Task<(string? Password, string? Error, ApplicationUser? User)> ResetPasswordAsync(string userId, string adminId)
    {
        if (string.Equals(userId, adminId, StringComparison.Ordinal))
            return (null, "Change your own password on your profile page.", null);
        var user = await users.FindByIdAsync(userId);
        if (user == null) return (null, "This account no longer exists. Refresh the page.", null);
        var roles = await users.GetRolesAsync(user);
        if (roles.Contains(RoleNames.Admin))
            return (null, "Administrator passwords can't be reset here. The administrator changes it on their own profile.", user);
        if (roles.Contains(RoleNames.Supplier))
            return (null, "Supplier passwords can't be reset here.", user);

        var password = GenerateTemporaryPassword();
        // ResetPasswordAsync also changes the security stamp, so the person's existing sessions end.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded) return (null, string.Join(" ", result.Errors.Select(e => e.Description)), user);

        if (!(await users.GetClaimsAsync(user)).Any(c => c.Type == PasswordChangeRequiredClaim))
            await users.AddClaimAsync(user, new Claim(PasswordChangeRequiredClaim, "true"));
        // A forgotten password often comes with a lockout; clear it so the new password works straight away.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return (password, null, user);
    }

    public Task<string?> ActivePositionHolderAsync(string position, CancellationToken ct = default) =>
        (from user in db.Users
         join assignment in db.UserRoles on user.Id equals assignment.UserId
         join role in db.Roles on assignment.RoleId equals role.Id
         where user.IsActive && role.Name == position
         select user.FullName).FirstOrDefaultAsync(ct);

    /// <summary>
    /// A 16-character temporary password that satisfies the Identity policy (upper, lower, digit, symbol),
    /// drawn from a cryptographic random source. Ambiguous characters (0/O, 1/l/I) are left out.
    /// </summary>
    public static string GenerateTemporaryPassword(int length = 16)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*?-+=";
        var all = upper + lower + digits + symbols;
        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };
        while (chars.Count < length) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }
}
