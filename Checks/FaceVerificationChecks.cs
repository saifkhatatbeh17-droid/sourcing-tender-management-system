using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.FaceVerification;
using SourcingTenderManagement.Services.FaceEnrollment;
using SourcingTenderManagement.Services.FaceVerification;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website. Tests face verification. Not run from Program.cs: see docs/FACE-VERIFICATION-PHASE3.md for how it is run.

public static class FaceVerificationChecks
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.DataSource != "localhost"
            || !connection.Database.StartsWith("SourcingTender_FaceMigration_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the isolated face migration test database is allowed.");
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); Console.WriteLine("PASS VERIFICATION: " + label); count++; }
        var crypto = services.GetRequiredService<FaceTemplateProtector>();
        var service = new FaceVerificationService(db, crypto, Options.Create(new FaceVerificationOptions()));
        var samples = Enumerable.Range(0, 5)
            .Select(i => Enumerable.Repeat(0.01f + i * 0.001f, 128).ToArray())
            .ToArray();
        FaceVerificationRequest Request(float[]? descriptor = null) => new() { Descriptor = descriptor
            ?? samples[0], ModelIdentifier = FaceEnrollmentService.ModelIdentifier };
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var user = new ApplicationUser { Id = Guid.NewGuid()
                .ToString(), UserName = "verification-" + Guid
                .NewGuid()
                .ToString("N"), FullName = "Synthetic verification user", IsActive = true };
            var other = new ApplicationUser { Id = Guid.NewGuid()
                .ToString(), UserName = "verification-" + Guid
                .NewGuid()
                .ToString("N"), FullName = "Other synthetic user", IsActive = true };
            db.Users.AddRange(user, other); await db.SaveChangesAsync();
            Check(!(await service.GetAsync(user.Id, default)).Enrolled, "No enrollment page state");
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Missing enrollment safe error");
            var row = new UserFaceCredential { UserId = user.Id, ProtectedTemplates = crypto.Protect(user.Id, samples), SampleCount = 5, TemplateFormatVersion = 1,
                ModelIdentifier = FaceEnrollmentService.ModelIdentifier, ConsentVersion = FaceEnrollmentService.ConsentVersion,
                ConsentGrantedAt = DateTime.UtcNow, EnrolledAt = DateTime.UtcNow, EnrollmentPromptedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.UserFaceCredentials.Add(row); await db.SaveChangesAsync();
            var originalPayload = row.ProtectedTemplates.ToArray(); var originalVersion = row.RowVersion.ToArray();
            Check((await service.GetAsync(user.Id, default)).Enrolled, "Enrolled page state");
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Matched", "Synthetic match");
            Check((await service.VerifyAsync(user.Id, Request(Enumerable.Repeat(0.2f, 128)
                .ToArray()), default)).Status == "Not Matched", "Synthetic mismatch");
            var strict = new FaceVerificationService(db, crypto, Options.Create(new FaceVerificationOptions { MatchThreshold = 0.005 }));
            Check((await strict.VerifyAsync(user.Id,
                Request(samples[^1]),
                default)).Status == "Matched", "All enrolled templates compared, including last sample");
            var probe = Request(Enumerable.Repeat(0.06f, 128).ToArray());
            Check((await service.VerifyAsync(user.Id, probe, default)).Status == "Not Matched", "Default threshold boundary behavior");
            var loose = new FaceVerificationService(db, crypto, Options.Create(new FaceVerificationOptions { MatchThreshold = 0.6 }));
            Check((await loose.VerifyAsync(user.Id, probe, default)).Status == "Matched", "Configured threshold applied");
            foreach (var bad in new float[]?[] { null, [], new float[127], new float[129] })
                Check((await service.VerifyAsync(user.Id,
                    new() { Descriptor = bad, ModelIdentifier = FaceEnrollmentService.ModelIdentifier },
                    default)).Status == "Verification Failed", "Invalid dimensions rejected");
            foreach (var number in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 4f })
            {
                var bad = samples[0].ToArray(); bad[0] = number;
                Check((await service.VerifyAsync(user.Id, Request(bad), default)).Status == "Verification Failed", "Nonfinite/out-of-range input rejected");
            }
            var wrongModel = Request(); wrongModel.ModelIdentifier = "another-model";
            Check((await service.VerifyAsync(user.Id, wrongModel, default)).Status == "Verification Failed", "Request model mismatch rejected");
            Check((await service.VerifyAsync(other.Id, Request(), default)).Status == "Verification Failed", "Other user cannot access owner's templates");
            var unchanged = await db.UserFaceCredentials.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
            Check(unchanged.ProtectedTemplates!.SequenceEqual(originalPayload)
                && unchanged.RowVersion.SequenceEqual(originalVersion), "Verification did not modify enrollment");
            Check(!db.ChangeTracker.HasChanges(), "Verification leaves change tracker unchanged");
            row.ModelIdentifier = "incompatible-model"; await db.SaveChangesAsync();
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Stored model mismatch rejected");
            row.ModelIdentifier = FaceEnrollmentService.ModelIdentifier; row.TemplateFormatVersion = 2; await db.SaveChangesAsync();
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Stored format mismatch rejected");
            row.TemplateFormatVersion = 1; row.ProtectedTemplates = [1, 2, 3]; await db.SaveChangesAsync();
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Corrupted ciphertext safe error");
            row.ProtectedTemplates = crypto.Protect(other.Id, samples); await db.SaveChangesAsync();
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Copied other-user ciphertext rejected");
            var provider = new EphemeralDataProtectionProvider();
            var invalidCrypto = new FaceTemplateProtector(provider);
            var invalidService = new FaceVerificationService(db, invalidCrypto, Options.Create(new FaceVerificationOptions()));
            row.ProtectedTemplates = provider.CreateProtector("SourcingTender.FaceEnrollment", "v1", user.Id)
                .Protect(Encoding.UTF8.GetBytes("not-json")); await db
                .SaveChangesAsync();
            Check((await invalidService.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Decrypted invalid JSON safe error");
            row.ProtectedTemplates = crypto.Protect(user.Id, [new float[127], samples[1], samples[2]]); await db
                .SaveChangesAsync();
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Invalid stored descriptor shape rejected");
            row.ProtectedTemplates = crypto.Protect(user.Id, samples[..3]); await db.SaveChangesAsync();
            Check((await service.VerifyAsync(user.Id, Request(), default)).Status == "Verification Failed", "Stored count mismatch rejected");
            user.IsActive = false; await db.SaveChangesAsync();
            try { await service.VerifyAsync(user.Id, Request(), default); throw new InvalidOperationException("Inactive account accepted"); }
            catch (UnauthorizedAccessException) { Check(true, "Inactive account rejected"); }
            user.IsActive = true; user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5); await db
                .SaveChangesAsync();
            try { await service.GetAsync(user.Id, default); throw new InvalidOperationException("Locked account accepted"); }
            catch (UnauthorizedAccessException) { Check(true, "Locked account rejected"); }
        }
        finally { await transaction.RollbackAsync(); db.ChangeTracker.Clear(); }
        Console.WriteLine($"Face verification checks: {count} passed; synthetic SQL fixtures rolled back.");
        return count;
    }
}
