using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.FaceEnrollment;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

// Real MVC/Identity requests in a separate loopback test host; never runs Program.cs or its seeding.
public static class FaceEnrollmentHttpChecks
{
    public static async Task<int> RunAsync(string connection, string contentRoot)
    {
        var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
        if (target.DataSource != "localhost" || !target.InitialCatalog.StartsWith("SourcingTender_FaceMigration_", StringComparison.Ordinal))
            throw new InvalidOperationException("An isolated face test database is required.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FaceEnrollmentHttpChecks).Assembly.GetName().Name,
            ContentRootPath = contentRoot, EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddTopCommitteeFoundation();
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8;
            o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        builder.Services.ConfigureApplicationCookie(o => o.LoginPath = "/Account/Login");
        builder.Services.AddFaceEnrollment();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(new FaceTemplateProtector(new EphemeralDataProtectionProvider()));
        await using var app = builder.Build();
        app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        var count = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("FAIL HTTP: " + label); Console.WriteLine("PASS HTTP: " + label); count++; }
        var secret = "Aa!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var user = new ApplicationUser { UserName = "face-http-" + Guid.NewGuid().ToString("N") + "@example.invalid", FullName = "Synthetic HTTP test", IsActive = true };
        user.Email = user.UserName;
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.OpenConnectionAsync();
                if (db.Database.GetDbConnection().Database != target.InitialCatalog) throw new InvalidOperationException("Target mismatch");
                var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, secret);
                if (!result.Succeeded) throw new InvalidOperationException("Synthetic account creation failed");
            }
            await app.StartAsync();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
            var unauthorized = await client.GetAsync("/FaceEnrollment/Index");
            Check(unauthorized.StatusCode == HttpStatusCode.Redirect && unauthorized.Headers.Location!.OriginalString.Contains("/Account/Login"), "Anonymous enrollment redirects to Identity login");
            var login = await client.GetStringAsync("/Account/Login");
            static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var loggedIn = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = user.Email!, ["Password"] = secret, ["__RequestVerificationToken"] = Token(login) }));
            Check(loggedIn.StatusCode == HttpStatusCode.Redirect && loggedIn.Headers.Location!.OriginalString.Contains("GoToDashboard"), "Existing password login issues real Identity session");
            var pageResponse = await client.GetAsync("/FaceEnrollment/Index");
            var page = await pageResponse.Content.ReadAsStringAsync();
            Check(pageResponse.IsSuccessStatusCode && page.Contains("face-video") && page.Contains("face-consent"), "Authenticated Razor page renders");
            Check(pageResponse.Headers.CacheControl?.NoStore == true, "Enrollment page is no-store");
            var samples = Enumerable.Range(0, 5).Select(i => Enumerable.Repeat(0.01f + i * 0.001f, 128).ToArray()).ToArray();
            var request = new { password = secret, consent = true, descriptors = samples };
            var csrf = await client.PostAsJsonAsync("/FaceEnrollment/Save", request);
            Check(csrf.StatusCode == HttpStatusCode.BadRequest, "POST without anti-forgery token rejected");
            client.DefaultRequestHeaders.Add("RequestVerificationToken", Token(page));
            var consent = await client.PostAsJsonAsync("/FaceEnrollment/Save", new { password = secret, consent = false, descriptors = samples });
            Check(consent.StatusCode == HttpStatusCode.BadRequest, "Missing consent rejected over HTTP");
            var saved = await client.PostAsJsonAsync("/FaceEnrollment/Save", request);
            Check(saved.IsSuccessStatusCode, "Real authenticated enrollment POST succeeds");
            Check(!saved.Headers.Contains("Set-Cookie"), "Enrollment does not issue authentication cookie");
            var after = await client.GetStringAsync("/FaceEnrollment/Index");
            Check(after.Contains("5 samples"), "Enrollment persisted and displayed after refresh");
            var rowVersion = WebUtility.HtmlDecode(Regex.Match(after, "data-row-version=\"([^\"]+)\"").Groups[1].Value);
            var removed = await client.PostAsJsonAsync("/FaceEnrollment/Delete", new { password = secret, rowVersion });
            Check(removed.IsSuccessStatusCode, "Authenticated deletion POST succeeds");
            Check((await client.GetStringAsync("/FaceEnrollment/Index")).Contains("Not enrolled"), "Deletion persisted after refresh");
            Check((await client.GetAsync("/FaceEnrollment/Save")).StatusCode == HttpStatusCode.MethodNotAllowed, "Save cannot be invoked by GET");
            Check((await client.GetAsync("/js/face-enrollment.js")).IsSuccessStatusCode, "Enrollment JavaScript served");
            Check((await client.GetAsync("/lib/face-api/model/face_recognition_model-weights_manifest.json")).IsSuccessStatusCode, "Recognition model manifest served");
            foreach (var model in new[] { "tiny_face_detector", "face_landmark_68", "face_recognition" })
            {
                var weights = await client.GetAsync($"/lib/face-api/model/{model}_model.bin");
                Check(weights.IsSuccessStatusCode && (await weights.Content.ReadAsByteArrayAsync()).Length > 1000, model + " binary weights served");
            }
            HttpResponseMessage? limited = null;
            for (var i = 0; i < 4; i++)
                limited = await client.PostAsJsonAsync("/FaceEnrollment/Save", new { password = secret, consent = false, descriptors = samples });
            Check(limited!.StatusCode == HttpStatusCode.TooManyRequests, "Enrollment rate limit enforced over HTTP");
            Check((await client.GetAsync("/FaceEnrollment/Index")).IsSuccessStatusCode, "Enrollment rate limit does not block page navigation");
        }
        finally
        {
            await app.StopAsync();
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.UserFaceCredentials.SingleOrDefaultAsync(x => x.UserId == user.Id);
            if (row != null) { db.UserFaceCredentials.Remove(row); await db.SaveChangesAsync(); }
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existing = await manager.FindByIdAsync(user.Id);
            if (existing != null && !(await manager.DeleteAsync(existing)).Succeeded) throw new InvalidOperationException("Synthetic test cleanup failed");
        }
        Console.WriteLine($"Face HTTP checks: {count} passed; own synthetic fixtures removed.");
        return count;
    }
}
