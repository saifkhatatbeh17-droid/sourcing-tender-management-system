using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Models.FaceVerification;

namespace SourcingTenderManagement.Services.FaceVerification;

public static class FaceVerificationExtensions
{
    public static IServiceCollection AddFaceVerification(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FaceVerificationService>();
        services.AddOptions<FaceVerificationOptions>().Bind(configuration.GetSection("FaceVerification"))
            .Validate(x => double.IsFinite(x.MatchThreshold) && x.MatchThreshold > 0 && x.MatchThreshold <= 1,
                "FaceVerification:MatchThreshold must be finite and greater than 0 and at most 1.")
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("FaceVerification", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}
