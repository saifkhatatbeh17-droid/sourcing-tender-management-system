using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

namespace SourcingTenderManagement.Services.FaceEnrollment;

public static class FaceEnrollmentExtensions
{
    public static IServiceCollection AddFaceEnrollment(this IServiceCollection services)
    {
        services.AddScoped<FaceEnrollmentService>();
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
        });
        return services;
    }
}
