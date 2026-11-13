using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SourcingTenderManagement.Services.FaceLogin;

public static class FaceLoginExtensions
{
    public static IServiceCollection AddFaceLogin(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("FaceLogin", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}
