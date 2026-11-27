using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

// Checks that a user is an active CEO & Chiefs member and is assigned to the tender.
public sealed class TopCommitteeAccessService(ApplicationDbContext db)
{
    public async Task<string> RequireRoleAsync(ClaimsPrincipal actor, string role,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actor.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId) || !actor.IsInRole(role))
            throw new CommitteeAccessDeniedException();

        // Check the database as well as the cookie: disabled accounts and revoked roles cannot use services.
        var permitted = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive &&
            db.UserRoles.Any(ur => ur.UserId == u.Id &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)), cancellationToken);
        if (!permitted) throw new CommitteeAccessDeniedException();
        return userId;
    }

    public async Task<string> RequireAssignmentAsync(ClaimsPrincipal actor, int tenderId,
        CancellationToken cancellationToken = default)
    {
        var userId = await RequireRoleAsync(actor, RoleNames.TopCommittee, cancellationToken);
        if (tenderId <= 0 || !await db.CommitteeAssignments.AsNoTracking().AnyAsync(a =>
                a.TenderId == tenderId && a.UserId == userId && a.CommitteeType == "Top", cancellationToken))
            throw new CommitteeTenderNotFoundException();
        return userId;
    }

    internal async Task<IReadOnlyList<CommitteeMember>> GetMembersAsync(int tenderId,
        CancellationToken cancellationToken = default) =>
        await db.CommitteeAssignments.AsNoTracking().Where(a => a.TenderId == tenderId && a.CommitteeType == "Top")
            .OrderBy(a => a.Position).Select(a => new CommitteeMember(a.CommitteeAssignmentId, a.UserId,
                a.User.FullName, a.Position, a.User.IsActive,
                db.UserRoles.Any(ur => ur.UserId == a.UserId &&
                    db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))))
            .ToListAsync(cancellationToken);

    internal async Task<IReadOnlyList<CommitteeManager>> GetManagersAsync(int tenderId,
        CancellationToken cancellationToken) =>
        await db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Stage == "Manager" && a.Decision == "Approved")
            .Select(a => new CommitteeManager(a.UserId, a.User.FullName, a.User.IsActive,
                db.UserRoles.Any(ur => ur.UserId == a.UserId &&
                    db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingManager)), a.DecidedAt))
            .ToListAsync(cancellationToken);
}
