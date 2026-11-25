using System.Globalization;
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

namespace SourcingTenderManagement.Checks;

// Real HTTP/cookie tests, restricted to the existing isolated biometric test database.
public static class FaceLoginChecks
{
    public static async Task<int> RunAsync(string connection, string contentRoot)
    {
        var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
        if (target.DataSource != "localhost" || !target.InitialCatalog.StartsWith("SourcingTender_FaceMigration_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the isolated face test database is allowed.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(FaceLoginChecks).Assembly.GetName().Name, ContentRootPath = contentRoot, EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddFaceEnrollment(builder.Configuration);
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o => { o.User.RequireUniqueEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        builder.Services.ConfigureApplicationCookie(o => { o.LoginPath = "/Account/Login"; o.Cookie.Name = "FaceGateTest"; });
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var app = builder.Build();
        app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapGet("/__face-gate/protected", () => Results.Ok("authenticated")).RequireAuthorization();
        var fixtures = new List<string>(); var createdRoles = new List<string>();
        var password = "Aa!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        var samples = Enumerable.Range(0, 3).Select(i => Enumerable.Repeat(.01f + i * .001f, 128).ToArray()).ToArray();
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("FAIL: " + label); count++; Console.WriteLine("PASS: " + label); }
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(app.Urls.Single()) };
        static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<ApplicationUser> Create(bool enrolled)
        {
            using var scope = app.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "gate-" + Guid.NewGuid().ToString("N") + "@example.invalid", FullName = "Synthetic gate user", IsActive = true, LockoutEnabled = true, EmailConfirmed = true }; user.Email = user.UserName;
            if (!(await manager.CreateAsync(user, password)).Succeeded) throw new InvalidOperationException("Fixture creation failed");
            fixtures.Add(user.Id);
            if (enrolled)
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.UserFaceCredentials.Add(new UserFaceCredential { UserId = user.Id, ProtectedTemplates = scope.ServiceProvider.GetRequiredService<FaceTemplateProtector>().Protect(user.Id, samples), SampleCount = 3, TemplateFormatVersion = 1, ModelIdentifier = FaceEnrollmentService.ModelIdentifier, ConsentVersion = FaceEnrollmentService.ConsentVersion, ConsentGrantedAt = DateTime.UtcNow, EnrolledAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            return user;
        }
        async Task<HttpResponseMessage> Login(HttpClient client, ApplicationUser user, string secret, float[]? descriptor, bool csrf = true, bool face = true, string? returnUrl = null)
        {
            var html = await client.GetStringAsync("/Account/Login");
            var body = new Dictionary<string,string> { ["Email"] = user.Email!, ["Password"] = secret, ["ModelIdentifier"] = FaceEnrollmentService.ModelIdentifier, ["ReturnUrl"] = returnUrl ?? "" };
            if (csrf) body["__RequestVerificationToken"] = Token(html);
            if (descriptor != null) for (var i = 0; i < descriptor.Length; i++) body[$"Descriptor[{i}]"] = descriptor[i].ToString("R", CultureInfo.InvariantCulture);
            using var multipart = new MultipartFormDataContent();
            foreach (var field in body) multipart.Add(new StringContent(field.Value), field.Key);
            return await client.PostAsync(face ? "/Account/LoginWithFaceId" : "/Account/Login", multipart);
        }
        async Task Denied(ApplicationUser user, string secret, float[]? descriptor, string label, bool csrf = true)
        {
            using var client = Client(); using var result = await Login(client, user, secret, descriptor, csrf);
            Check(result.StatusCode == HttpStatusCode.BadRequest && !result.Headers.Contains("Set-Cookie"), label + ": no authentication cookie");
            Check((await client.GetAsync("/__face-gate/protected")).StatusCode == HttpStatusCode.Redirect, label + ": protected pages inaccessible");
        }
        try
        {
            var owner = await Create(true); var missing = await Create(false);
            await app.StartAsync();
            await Denied(owner, password, null, "Password alone");
            await Denied(owner, password, new float[127], "Malformed descriptor");
            await Denied(owner, password, samples[0], "Missing CSRF", false);
            await Denied(owner, "Wrong!Password1", samples[0], "Wrong password with matching face");
            await Denied(owner, password, Enumerable.Repeat(.8f,128).ToArray(), "Wrong face with correct password");
            await Denied(missing, password, samples[0], "No enrollment cannot use another account face");
            using (var scope = app.Services.CreateScope())
            {
                var saved = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(owner.Id);
                Check(saved!.AccessFailedCount == 2, "Correct password does not clear failed face attempts");
            }
            using (var client = Client())
            {
                using var result = await Login(client, owner, password, samples[0], returnUrl:"https://example.invalid/escape");
                var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                Check(result.IsSuccessStatusCode && result.Headers.Contains("Set-Cookie"), "Both factors issue cookie");
                Check(json.RootElement.GetProperty("redirectUrl").GetString() == "/Account/GoToDashboard", "External return URL discarded");
                Check((await client.GetAsync("/__face-gate/protected")).IsSuccessStatusCode, "Matched session accesses protected pages");
            }
            foreach (var role in RoleNames.Primary)
            {
                using (var scope = app.Services.CreateScope())
                {
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    if (!await roles.RoleExistsAsync(role)) { await roles.CreateAsync(new IdentityRole(role)); createdRoles.Add(role); }
                    var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(owner.Id))!;
                    await manager.RemoveFromRolesAsync(user, await manager.GetRolesAsync(user)); await manager.AddToRoleAsync(user,role);
                }
                using var client = Client(); using var result = await Login(client,owner,password,samples[0]);
                var route = await client.GetAsync("/Account/GoToDashboard");
                Check(result.IsSuccessStatusCode && route.Headers.Location?.OriginalString == (role == RoleNames.Supplier ? "/Account/RegistrationStatus" : "/" + role + "/Dashboard"), role + " routing preserved");
            }
            var locked = await Create(true);
            for (var i=0;i<5;i++) { using var client=Client(); await Login(client,locked,password,Enumerable.Repeat(.8f,128).ToArray()); }
            await Denied(locked,password,samples[0],"Locked account even with correct face/password");
            using (var scope=app.Services.CreateScope())
            {
                var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user=(await manager.FindByIdAsync(locked.Id))!;
                Check(await manager.IsLockedOutAsync(user),"Repeated face mismatches trigger Identity lockout");
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var saved=await db.UserFaceCredentials.SingleAsync(x=>x.UserId==owner.Id);
                Check(scope.ServiceProvider.GetRequiredService<FaceTemplateProtector>().Unprotect(owner.Id,saved.ProtectedTemplates!).SelectMany(x=>x).SequenceEqual(samples.SelectMany(x=>x)),"Enrollment unchanged by authentication");
            }
            using (var client=Client())
            {
                var normal=await Login(client,missing,password,null,face:false);
                Check(normal.StatusCode==HttpStatusCode.Redirect && (await client.GetAsync("/__face-gate/protected")).IsSuccessStatusCode,"Normal password sign-in remains available");
            }
        }
        finally
        {
            await app.StopAsync(); using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserFaceCredentials.RemoveRange(await db.UserFaceCredentials.Where(x=>fixtures.Contains(x.UserId)).ToListAsync()); await db.SaveChangesAsync();
            var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach(var id in fixtures) { var user=await manager.FindByIdAsync(id); if(user!=null && !(await manager.DeleteAsync(user)).Succeeded) throw new InvalidOperationException("Synthetic user cleanup failed"); }
            var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach(var name in createdRoles) { var role=await roles.FindByNameAsync(name); if(role!=null) await roles.DeleteAsync(role); }
        }
        Console.WriteLine($"Face login gate: {count} checks passed; isolated fixtures removed."); return count;
    }
}
