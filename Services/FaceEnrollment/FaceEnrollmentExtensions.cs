using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.FaceEnrollment;

namespace SourcingTenderManagement.Services.FaceEnrollment;

public static class FaceEnrollmentExtensions
{
    public static IServiceCollection AddFaceEnrollment(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FaceEnrollmentService>();
        services.AddScoped<FaceVerificationService>();
        services.AddOptions<FaceVerificationOptions>().Bind(configuration.GetSection("FaceVerification"))
            .Validate(x => double.IsFinite(x.MatchThreshold) && x.MatchThreshold > 0 && x.MatchThreshold <= 1,
                "FaceVerification:MatchThreshold must be finite and greater than 0 and at most 1.")
            .ValidateOnStart();
        // Separate provider: do not change Identity's key ring or invalidate existing login cookies.
        services.AddSingleton(_ =>
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Face enrollment requires a protected key store configured for this host.");
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SourcingTenderManagement", "FaceEnrollmentKeys");
            if (!Path.IsPathFullyQualified(folder)) throw new InvalidOperationException("A persistent key directory is required.");
            var provider = DataProtectionProvider.Create(new DirectoryInfo(folder), options =>
            {
                options.SetApplicationName("SourcingTenderManagement.FaceEnrollment");
                if (OperatingSystem.IsWindows()) options.ProtectKeysWithDpapi();
            });
            return new FaceTemplateProtector(provider);
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("FaceEnrollment", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Face and passkey sign-in on the login page.
            options.AddPolicy("FaceLogin", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}

public sealed class FaceTemplateProtector(IDataProtectionProvider provider)
{
    public byte[] Protect(string userId, float[][] descriptors) =>
        Protector(userId).Protect(JsonSerializer.SerializeToUtf8Bytes(descriptors));

    public float[][] Unprotect(string userId, byte[] payload) =>
        JsonSerializer.Deserialize<float[][]>(Protector(userId).Unprotect(payload))
        ?? throw new CryptographicException("Invalid face template payload.");

    private IDataProtector Protector(string userId) =>
        provider.CreateProtector("SourcingTender.FaceEnrollment", "v1", userId);
}

// Decides whether to show the optional Face ID setup after sign-in.
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
        if (user.Identity?.IsAuthenticated != true || id == null || !RoleNames.Primary.Any(user.IsInRole) || request.Cookies[CookieName] == "1") return false;
        if (user.IsInRole(RoleNames.Supplier) && !await db.SupplierProfiles.AsNoTracking().AnyAsync(x => x.UserId == id && x.Status == "Approved", ct)) return false;
        return await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive &&
            (!x.LockoutEnabled || x.LockoutEnd == null || x.LockoutEnd <= DateTimeOffset.UtcNow) &&
            !db.UserFaceCredentials.Any(f => f.UserId == x.Id && f.ProtectedTemplates != null), ct);
    }
}
