using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.FaceEnrollment;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using SourcingTenderManagement.Models.Entities;
using Microsoft.Extensions.Options;

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

public sealed class FaceEnrollmentService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, FaceTemplateProtector protector)
{
    public const string ModelIdentifier = "vladmandic-face-api-1.7.13/faceRecognitionNet-128";
    public const string ConsentVersion = "face-enrollment-v1";

    public async Task<FaceEnrollmentPage> GetAsync(string userId, CancellationToken ct)
    {
        await ActiveUserAsync(userId);
        var row = await db.UserFaceCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return new(row?.ProtectedTemplates != null, row?.SampleCount ?? 0, row?.EnrolledAt,
            row == null ? null : Convert.ToBase64String(row.RowVersion));
    }

    public async Task SaveAsync(string userId, FaceEnrollmentRequest input, bool delete, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024)
            throw new ValidationException("Enter your current password.");
        if (!delete)
        {
            if (!input.Consent) throw new ValidationException("Explicit biometric consent is required.");
            ValidateDescriptors(input.Descriptors);
        }
        var user = await ActiveUserAsync(userId);
        // Uses Identity pre-sign-in/lockout checks, counts failures, and never issues a cookie.
        var password = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
        if (!password.Succeeded) throw new ValidationException("Password verification failed or the account is unavailable.");

        var row = await db.UserFaceCredentials.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row != null)
        {
            if (input.RowVersion != Convert.ToBase64String(row.RowVersion))
                throw new DbUpdateConcurrencyException("Enrollment changed. Reload this page.");
            if (!delete && row.ProtectedTemplates != null && !input.Replace)
                throw new ValidationException("Choose Replace to overwrite your existing enrollment.");
        }
        else
        {
            if (delete || input.Replace || input.RowVersion != null)
                throw new DbUpdateConcurrencyException("Enrollment changed. Reload this page.");
            row = new UserFaceCredential { UserId = userId, EnrollmentPromptedAt = DateTime.UtcNow };
            db.UserFaceCredentials.Add(row);
        }
        var now = DateTime.UtcNow;
        row.ProtectedTemplates = delete ? null : protector.Protect(userId, input.Descriptors!);
        row.SampleCount = delete ? (short)0 : (short)input.Descriptors!.Length;
        row.TemplateFormatVersion = delete ? null : (short)1;
        row.ModelIdentifier = delete ? null : ModelIdentifier;
        row.ConsentVersion = delete ? null : ConsentVersion;
        row.ConsentGrantedAt = delete ? null : now;
        row.EnrolledAt = delete ? null : now;
        row.UpdatedAt = now;
        // Single SaveChanges is atomic; RowVersion protects updates and PK protects first-enrollment races.
        await db.SaveChangesAsync(ct);
    }

    private async Task<ApplicationUser> ActiveUserAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user == null || !user.IsActive) throw new UnauthorizedAccessException();
        return user;
    }

    public static void ValidateDescriptors(float[][]? samples)
    {
        if (samples == null || samples.Length is < 3 or > 5)
            throw new ValidationException("Capture between three and five samples.");
        foreach (var sample in samples)
            if (sample == null || sample.Length != 128 || sample.Any(x => !float.IsFinite(x) || Math.Abs(x) > 3))
                throw new ValidationException("Invalid face samples. Capture them again.");
        double largest = 0;
        for (var i = 0; i < samples.Length; i++)
            for (var j = i + 1; j < samples.Length; j++)
            {
                var distance = Math.Sqrt(samples[i].Zip(samples[j], (a, b) => (double)(a - b) * (a - b)).Sum());
                if (distance > 0.5) throw new ValidationException("Samples are inconsistent. Keep one face in view.");
                largest = Math.Max(largest, distance);
            }
        if (largest < 0.00001) throw new ValidationException("Samples must come from separate captures.");
    }
}

// Informational, read-only comparison. No authentication, authorization or account mutation.
public sealed class FaceVerificationService(ApplicationDbContext db, FaceTemplateProtector protector,
    IOptions<FaceVerificationOptions> options)
{
    public async Task<FaceVerificationResult> VerifyAsync(string userId, FaceVerificationRequest input, CancellationToken ct)
    {
        await RequireActiveAccountAsync(userId, ct);
        if (input == null || !ValidDescriptor(input.Descriptor) ||
            input.ModelIdentifier != FaceEnrollmentService.ModelIdentifier)
            return FaceVerificationResult.Failed();

        var row = await db.UserFaceCredentials.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.ProtectedTemplates, x.SampleCount, x.ModelIdentifier, x.TemplateFormatVersion })
            .SingleOrDefaultAsync(ct);
        if (row?.ProtectedTemplates == null || row.ProtectedTemplates.Length is 0 or > 64_000 ||
            row.ModelIdentifier != FaceEnrollmentService.ModelIdentifier || row.TemplateFormatVersion != 1)
            return FaceVerificationResult.Failed();

        float[][] templates;
        try
        {
            templates = protector.Unprotect(userId, row.ProtectedTemplates);
            FaceEnrollmentService.ValidateDescriptors(templates);
            if (templates.Length != row.SampleCount) return FaceVerificationResult.Failed();
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ValidationException)
        { return FaceVerificationResult.Failed(); }

        // Compare every enrolled sample; only the minimum distance determines the result.
        var nearest = templates.Min(sample => Math.Sqrt(sample.Zip(input.Descriptor!,
            (a, b) => ((double)a - b) * ((double)a - b)).Sum()));
        return new(nearest <= options.Value.MatchThreshold ? "Matched" : "Not Matched");
    }

    public static bool ValidDescriptor(float[]? descriptor) => descriptor is { Length: 128 } &&
        descriptor.All(x => float.IsFinite(x) && Math.Abs(x) <= 3);

    private async Task RequireActiveAccountAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(userId) || !await db.Users.AsNoTracking().AnyAsync(x =>
                x.Id == userId && x.IsActive && (!x.LockoutEnabled || x.LockoutEnd == null || x.LockoutEnd <= now), ct))
            throw new UnauthorizedAccessException();
    }
}
