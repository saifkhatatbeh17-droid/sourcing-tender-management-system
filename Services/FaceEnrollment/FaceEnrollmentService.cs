using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.FaceEnrollment;

namespace SourcingTenderManagement.Services.FaceEnrollment;

// Saves, loads and deletes a user's registered face data (stored encrypted).
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
