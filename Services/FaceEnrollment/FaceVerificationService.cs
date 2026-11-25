using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.FaceEnrollment;

namespace SourcingTenderManagement.Services.FaceEnrollment;

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
