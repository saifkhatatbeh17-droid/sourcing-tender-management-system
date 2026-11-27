using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.FaceEnrollment;
using SourcingTenderManagement.Services.FaceVerification;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

public static class FaceVerificationHttpChecks
{
    public static async Task<int> RunAsync(string connection, string contentRoot)
    {
        var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
        if (target.DataSource != "localhost" || !target.InitialCatalog.StartsWith("SourcingTender_FaceMigration_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the dedicated face test database is allowed.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(FaceVerificationHttpChecks).Assembly.GetName().Name, ContentRootPath = contentRoot, EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddTopCommitteeFoundation();
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        builder.Services.ConfigureApplicationCookie(o => o.LoginPath = "/Account/Login");
        builder.Services.AddFaceEnrollment(); builder.Services.AddFaceVerification(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(new FaceTemplateProtector(new EphemeralDataProtectionProvider()));
        await using var app = builder.Build();
        app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL HTTP: " + label); Console.WriteLine("PASS VERIFICATION HTTP: " + label); count++; }
        var user = new ApplicationUser { UserName = "verification-http-" + Guid.NewGuid().ToString("N") + "@example.invalid", FullName = "Synthetic verification HTTP", IsActive = true };
        user.Email = user.UserName;
        var secret = "Aa!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var samples = Enumerable.Range(0, 5).Select(i => Enumerable.Repeat(0.01f + i * 0.001f, 128).ToArray()).ToArray();
        static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> StateAsync()
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.UserFaceCredentials.AsNoTracking().SingleAsync(x => x.UserId == user.Id);
            var identity = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
            var roles = await db.UserRoles.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.RoleId).Select(x => x.RoleId).ToListAsync();
            return JsonSerializer.Serialize(new { row.UserId, Payload = Convert.ToBase64String(row.ProtectedTemplates!), row.SampleCount, row.ModelIdentifier, row.TemplateFormatVersion,
                row.EnrollmentPromptedAt, row.ConsentVersion, row.ConsentGrantedAt, row.EnrolledAt, row.UpdatedAt, Version = Convert.ToBase64String(row.RowVersion), identity.IsActive, identity.SecurityStamp, identity.AccessFailedCount, identity.LockoutEnd, Roles = roles });
        }
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.OpenConnectionAsync();
                if (db.Database.GetDbConnection().Database != target.InitialCatalog) throw new InvalidOperationException("Target mismatch");
                if (!(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, secret)).Succeeded) throw new InvalidOperationException("Test user creation failed");
            }
            await app.StartAsync();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
            Check((await client.GetAsync("/FaceVerification/Index")).StatusCode == HttpStatusCode.Redirect, "Anonymous page access rejected");
            var anonymousPost = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(anonymousPost.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, "Anonymous verification POST rejected");
            var login = await client.GetStringAsync("/Account/Login");
            var signedIn = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = user.Email!, ["Password"] = secret, ["__RequestVerificationToken"] = Token(login) }));
            Check(signedIn.StatusCode == HttpStatusCode.Redirect, "Existing Identity password login succeeds");
            var emptyPage = await client.GetStringAsync("/FaceVerification/Index");
            Check(emptyPage.Contains("/FaceEnrollment") && !emptyPage.Contains("id=\"verification-form\""), "Missing enrollment shows link without camera controls");
            // GET enrollment supplies a valid authenticated anti-forgery token even without enrollment.
            var enrollmentPage = await client.GetStringAsync("/FaceEnrollment/Index");
            client.DefaultRequestHeaders.Add("RequestVerificationToken", Token(enrollmentPage));
            var missing = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier, userId = "another-user" });
            Check(missing.StatusCode == HttpStatusCode.BadRequest && (await missing.Content.ReadAsStringAsync()).Contains("Verification Failed"), "Missing own enrollment cannot be bypassed with browser UserId");
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var protector = scope.ServiceProvider.GetRequiredService<FaceTemplateProtector>();
                db.UserFaceCredentials.Add(new UserFaceCredential { UserId = user.Id, ProtectedTemplates = protector.Protect(user.Id, samples), SampleCount = 5, TemplateFormatVersion = 1, ModelIdentifier = FaceEnrollmentService.ModelIdentifier,
                    ConsentVersion = FaceEnrollmentService.ConsentVersion, ConsentGrantedAt = DateTime.UtcNow, EnrolledAt = DateTime.UtcNow, EnrollmentPromptedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var page = await client.GetAsync("/FaceVerification/Index"); var html = await page.Content.ReadAsStringAsync();
            Check(page.IsSuccessStatusCode && html.Contains("verification-video") && html.Contains("Informational similarity"), "Authenticated verification Razor page rendered");
            Check(page.Headers.CacheControl?.NoStore == true, "Page response cannot be cached");
            client.DefaultRequestHeaders.Remove("RequestVerificationToken");
            var csrf = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(csrf.StatusCode == HttpStatusCode.BadRequest, "Anti-forgery enforced");
            client.DefaultRequestHeaders.Add("RequestVerificationToken", Token(html));
            var before = await StateAsync();
            var protectedBefore = await client.GetAsync("/Admin/Dashboard");
            var matched = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier, userId = "ignored-client-id" });
            var result = await matched.Content.ReadAsStringAsync();
            Check(matched.IsSuccessStatusCode && result.Contains("Matched"), "Authenticated synthetic matching POST succeeds");
            Check(!matched.Headers.Contains("Set-Cookie"), "Matching creates no authentication cookie");
            using (var json = JsonDocument.Parse(result))
                Check(json.RootElement.EnumerateObject().All(x => x.Name is "status" or "message"), "No templates, descriptors or distances returned");
            var mismatch = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = Enumerable.Repeat(0.2f, 128).ToArray(), modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(mismatch.IsSuccessStatusCode && (await mismatch.Content.ReadAsStringAsync()).Contains("Not Matched"), "Nonmatching POST returns Not Matched");
            var bad = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = new float[127], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(bad.StatusCode == HttpStatusCode.BadRequest, "Invalid dimensions rejected over HTTP");
            var model = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = "wrong-model" });
            Check(model.StatusCode == HttpStatusCode.BadRequest, "Model mismatch rejected over HTTP");
            using var nanBody = new StringContent("{\"descriptor\":[NaN],\"modelIdentifier\":\"x\"}", System.Text.Encoding.UTF8, "application/json");
            Check((await client.PostAsync("/FaceVerification/Verify", nanBody)).StatusCode == HttpStatusCode.BadRequest, "Nonfinite JSON rejected");
            var protectedAfter = await client.GetAsync("/Admin/Dashboard");
            Check(protectedBefore.StatusCode == protectedAfter.StatusCode && protectedAfter.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, "Matching does not unlock Admin access");
            Check(await StateAsync() == before, "Enrollment, account state and role assignments unchanged");
            Check((await client.GetAsync("/FaceVerification/Verify")).StatusCode == HttpStatusCode.MethodNotAllowed, "Verify cannot run through GET");
            Check((await client.GetAsync("/js/face-verification.js")).IsSuccessStatusCode, "Verification JavaScript served");
            HttpResponseMessage? limited = null;
            for (var i = 0; i < 11; i++) limited = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(limited!.StatusCode == HttpStatusCode.TooManyRequests, "Verification rate limit enforced");
            Check((await client.GetAsync("/FaceEnrollment/Index")).IsSuccessStatusCode, "Verification limiter does not block enrollment navigation");
        }
        finally
        {
            await app.StopAsync();
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.UserFaceCredentials.SingleOrDefaultAsync(x => x.UserId == user.Id);
            if (row != null) { db.UserFaceCredentials.Remove(row); await db.SaveChangesAsync(); }
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var existing = await manager.FindByIdAsync(user.Id);
            if (existing != null && !(await manager.DeleteAsync(existing)).Succeeded) throw new InvalidOperationException("Synthetic fixture cleanup failed");
        }
        Console.WriteLine($"Face verification HTTP checks: {count} passed; own fixtures removed.");
        return count;
    }
}
