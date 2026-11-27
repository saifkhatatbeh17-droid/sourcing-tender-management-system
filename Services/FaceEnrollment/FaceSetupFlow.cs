using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.FaceEnrollment;

// Decides when to show the 'set up face sign-in' prompt and remembers if the user declined it.
public static class FaceSetupFlow
{
    // UI preference only, never an authorization/verification token. Reset on every password login.
    public const string CookieName = "SourcingTender.FaceSetupDeclined";
    public static CookieOptions CookieOptions(HttpRequest request) => new()
    { HttpOnly = true, Secure = request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true,
        Path = request.PathBase.HasValue ? request.PathBase.Value : "/" };

    public static async Task<bool> ShouldPromptAsync(ApplicationDbContext db, ClaimsPrincipal user, HttpRequest request, CancellationToken ct = default)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true
            || id == null
            || !RoleNames.Primary.Any(user.IsInRole)
            || request.Cookies[CookieName] == "1") return false;
        if (user.IsInRole(RoleNames.Supplier)
            && !await db.SupplierProfiles
            .AsNoTracking()
            .AnyAsync(x => x.UserId == id && x.Status == "Approved", ct)) return false;
        return await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive &&
            (!x.LockoutEnabled || x.LockoutEnd == null || x.LockoutEnd <= DateTimeOffset.UtcNow) &&
            !db.UserFaceCredentials.Any(f => f.UserId == x.Id && f.ProtectedTemplates != null), ct);
    }
}
