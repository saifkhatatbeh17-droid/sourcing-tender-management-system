using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.FaceEnrollment;
using SourcingTenderManagement.Services.FaceEnrollment;

namespace SourcingTenderManagement.Checks;

// Invoke from an external test host, never normal application startup. All fixtures are rolled back.
public static class FaceEnrollmentChecks
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + label);
            Console.WriteLine("PASS: " + label); count++;
        }
        async Task Rejects<T>(Func<Task> operation, string label) where T : Exception
        {
            try { await operation(); } catch (T) { Check(true, label); return; }
            throw new InvalidOperationException("FAIL: " + label);
        }
        var valid = Enumerable.Range(0, 5).Select(i => Enumerable.Repeat(0.01f + i * 0.001f, 128).ToArray()).ToArray();
        void RejectSamples(float[][]? samples, string label) =>
            Rejects<ValidationException>(() => { FaceEnrollmentService.ValidateDescriptors(samples); return Task.CompletedTask; }, label).GetAwaiter()
                .GetResult();
        FaceEnrollmentService.ValidateDescriptors(valid); Check(true, "Five synthetic samples valid");
        FaceEnrollmentService.ValidateDescriptors(valid[..3]); Check(true, "Three synthetic samples valid");
        RejectSamples(null, "Missing samples"); RejectSamples(valid[..2], "Too few samples");
        RejectSamples([..valid, valid[0]], "Too many samples");
        RejectSamples([new float[127], valid[1], valid[2]], "Invalid dimensions");
        RejectSamples([null!, valid[1], valid[2]], "Null sample");
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 4f })
        {
            var bad = valid.Select(x => x.ToArray()).ToArray(); bad[0][0] = value;
            RejectSamples(bad, "Non-finite/out-of-range sample rejected");
        }
        RejectSamples([valid[0], valid[0], valid[0]], "Repeated descriptors rejected");
        RejectSamples([valid[0], Enumerable.Repeat(1f, 128).ToArray(), valid[2]], "Inconsistent samples rejected");
        var crypto = new FaceTemplateProtector(new EphemeralDataProtectionProvider());
        var payload = crypto.Protect("owner", valid);
        Check(crypto.Unprotect("owner", payload)[1].SequenceEqual(valid[1]), "Encrypted payload round trip");
        await Rejects<CryptographicException>(() => { crypto.Unprotect("other", payload); return Task.CompletedTask; }, "Cross-user decryption blocked");
        payload[^1] ^= 1;
        await Rejects<CryptographicException>(() => { crypto.Unprotect("owner", payload); return Task.CompletedTask; }, "Tampering blocked");
        if (OperatingSystem.IsWindows())
        {
            var keyFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "face-key-check-" + Guid
                .NewGuid()
                .ToString("N")));
            IDataProtectionProvider PersistentProvider() => DataProtectionProvider.Create(keyFolder, options =>
            {
                options.SetApplicationName("FaceEnrollment.KeyPersistenceCheck");
                if (OperatingSystem.IsWindows()) options.ProtectKeysWithDpapi();
            });
            var first = new FaceTemplateProtector(PersistentProvider()).Protect("synthetic-user", valid);
            var recovered = new FaceTemplateProtector(PersistentProvider()).Unprotect("synthetic-user", first);
            Check(recovered[0].SequenceEqual(valid[0]), "DPAPI-protected persistent keys survive provider recreation");
        }

        var type = typeof(FaceEnrollmentController);
        Check(type.IsDefined(typeof(AuthorizeAttribute), true), "All endpoints require authorization (metadata)");
        foreach (var name in new[] { "Save", "Delete" })
        {
            var action = type.GetMethod(name)!;
            Check(action.IsDefined(typeof(HttpPostAttribute), true)
                && action
                .IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), name + " POST/CSRF metadata");
        }
        Check(!typeof(FaceEnrollmentRequest).GetProperties().Any(x => x.Name == "UserId"), "No client-owned UserId");
        var db = services.GetRequiredService<ApplicationDbContext>();
        var target = db.Database.GetDbConnection();
        if (target.DataSource != "localhost"
            || !target.Database.StartsWith("SourcingTender_FaceMigration_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the dedicated isolated face migration test database is allowed.");
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var signIn = services.GetRequiredService<SignInManager<ApplicationUser>>();
            var service = services.GetRequiredService<FaceEnrollmentService>();
            var secret = "Aa!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var user = new ApplicationUser { UserName = "face-" + Guid.NewGuid()
                .ToString("N"), FullName = "Synthetic enrollment test", IsActive = true };
            Check((await users.CreateAsync(user, secret)).Succeeded, "Synthetic Identity user created inside rollback transaction");
            FaceEnrollmentRequest Request(string? version = null, bool replace = false) => new()
            { Password = secret, Consent = true, Descriptors = valid, RowVersion = version, Replace = replace };
            Check(!(await service.GetAsync(user.Id, default)).Enrolled, "No enrollment state");
            var missingConsent = Request(); missingConsent.Consent = false;
            await Rejects<ValidationException>(() => service.SaveAsync(user.Id, missingConsent, false, default), "Consent required");
            var wrong = Request(); wrong.Password = secret + "wrong";
            await Rejects<ValidationException>(() => service.SaveAsync(user.Id, wrong, false, default), "Wrong password rejected");
            Check(await users.GetAccessFailedCountAsync(user) == 1, "Identity failure counter preserved");
            await service.SaveAsync(user.Id, Request(), false, default);
            var page = await service.GetAsync(user.Id, default);
            Check(page.Enrolled && page.SampleCount == 5 && page.RowVersion != null, "Authenticated enrollment saved");
            var row = await db.UserFaceCredentials.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
            Check(row.ModelIdentifier == FaceEnrollmentService.ModelIdentifier
                && row.TemplateFormatVersion == 1
                && row.ConsentVersion == FaceEnrollmentService.ConsentVersion, "Correct metadata stored");
            var registeredProtector = services.GetRequiredService<FaceTemplateProtector>();
            Check(registeredProtector.Unprotect(user.Id, row.ProtectedTemplates!)[0]
                .SequenceEqual(valid[0]), "Persisted encrypted samples decrypt correctly");
            await Rejects<ValidationException>(() => service.SaveAsync(user.Id,
                Request(page.RowVersion),
                false,
                default), "Duplicate enrollment requires explicit replace");
            await Rejects<DbUpdateConcurrencyException>(() => service.SaveAsync(user.Id, Request("stale", true), false, default), "Stale version rejected");
            await service.SaveAsync(user.Id, Request(page.RowVersion, true), false, default);
            var replaced = await service.GetAsync(user.Id, default);
            Check(replaced.RowVersion != page.RowVersion, "Replacement updates rowversion");
            // Simulate a concurrent writer after the tracked entity was loaded.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.UserFaceCredentials SET UpdatedAt=SYSUTCDATETIME() WHERE UserId={user.Id}");
            await Rejects<DbUpdateConcurrencyException>(() => service.SaveAsync(user.Id,
                Request(replaced.RowVersion, true),
                false,
                default), "Real SQL rowversion conflict rejected");
            db.ChangeTracker.Clear();
            var other = new ApplicationUser { UserName = "face-" + Guid.NewGuid()
                .ToString("N"), FullName = "Other synthetic user", IsActive = true };
            Check((await users.CreateAsync(other, secret)).Succeeded, "Other synthetic user created");
            await service.SaveAsync(other.Id, Request(), false, default);
            var otherPayload = (await db.UserFaceCredentials.AsNoTracking()
                .SingleAsync(x => x.UserId == other.Id)).ProtectedTemplates!.ToArray();
            var current = await service.GetAsync(user.Id, default);
            await service.SaveAsync(user.Id, Request(current.RowVersion), true, default);
            var deleted = await db.UserFaceCredentials.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
            Check(deleted.ProtectedTemplates == null
                && deleted.SampleCount == 0
                && deleted.ModelIdentifier == null
                && deleted.ConsentVersion == null
                && deleted.EnrolledAt == null, "Deletion clears biometric data");
            Check((await db.UserFaceCredentials.AsNoTracking()
                .SingleAsync(x => x.UserId == other.Id)).ProtectedTemplates!.SequenceEqual(otherPayload), "Other user credential unchanged");
            await Rejects<DbUpdateConcurrencyException>(() => service.SaveAsync(user.Id,
                Request(current.RowVersion),
                true,
                default), "Duplicate stale deletion rejected");
            db.ChangeTracker.Clear();
            var inactive = (await users.FindByIdAsync(user.Id))!; inactive.IsActive = false; await users.UpdateAsync(inactive);
            await Rejects<UnauthorizedAccessException>(() => service.GetAsync(user.Id, default), "Inactive account blocked");
            inactive.IsActive = true; await users.UpdateAsync(inactive);
            Check((await signIn.CheckPasswordSignInAsync(inactive, secret, true)).Succeeded, "Password authentication still works");
            await users.SetLockoutEndDateAsync(inactive, DateTimeOffset.UtcNow.AddMinutes(5));
            await Rejects<ValidationException>(() => service.SaveAsync(user.Id,
                Request(deleted.RowVersion == null ? null : Convert.ToBase64String(deleted.RowVersion)),
                false,
                default), "Locked account cannot enroll");
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            other = (await users.FindByIdAsync(other.Id))!;
            foreach (var role in RoleNames.Primary)
            {
                if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
                Check((await users.AddToRoleAsync(other, role)).Succeeded
                    && await users.IsInRoleAsync(other, role), "Identity role preserved: " + role);
            }
        }
        finally { await transaction.RollbackAsync(); db.ChangeTracker.Clear(); }
        Console.WriteLine($"Face enrollment checks: {count} passed; SQL fixtures rolled back.");
        return count;
    }
}
