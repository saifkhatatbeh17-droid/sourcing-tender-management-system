using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.FaceEnrollment;
using SourcingTenderManagement.Services.FaceLogin;
using SourcingTenderManagement.Services.FaceVerification;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;

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
        builder.Services.AddTopCommitteeFoundation(); builder.Services.AddFaceEnrollment();
        builder.Services.AddFaceVerification(builder.Configuration); builder.Services.AddFaceLogin();
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5); }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        builder.Services.ConfigureApplicationCookie(o => { o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/AccessDenied"; });
        builder.Services.AddAuthorization(o => o.AddPolicy("ApprovedSupplier", p => p.RequireRole(RoleNames.Supplier).RequireAssertion(async context =>
        {
            if (context.Resource is not HttpContext http) return false;
            var id = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>().GetUserId(context.User);
            return await http.RequestServices.GetRequiredService<ApplicationDbContext>().SupplierProfiles.AnyAsync(x => x.UserId == id && x.Status == "Approved");
        })));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(new FaceTemplateProtector(new EphemeralDataProtectionProvider()));
        await using var app = builder.Build();
        app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapGet("/__face-checks/approved-supplier", () => Results.Ok()).RequireAuthorization("ApprovedSupplier");
        var count = 0;
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("FAIL FACE LOGIN: " + label); Console.WriteLine("PASS FACE LOGIN: " + label); count++; }
        var usersToDelete = new List<string>(); var createdRoles = new List<string>();
        var secret = "Aa!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var samples = Enumerable.Range(0, 5).Select(i => Enumerable.Repeat(0.01f + i * 0.001f, 128).ToArray()).ToArray();
        var owner = new ApplicationUser { UserName = "face-login-" + Guid.NewGuid().ToString("N") + "@example.invalid", FullName = "Synthetic Face Login", IsActive = true }; owner.Email = owner.UserName;
        var empty = new ApplicationUser { UserName = "face-login-" + Guid.NewGuid().ToString("N") + "@example.invalid", FullName = "Synthetic unenrolled user", IsActive = true }; empty.Email = empty.UserName;
        static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(app.Urls.Single()) };
        async Task<HttpResponseMessage> Login(HttpClient client, string email, string password, bool face = true)
        {
            var html = await client.GetStringAsync("/Account/Login");
            return await client.PostAsync(face ? "/Account/LoginWithFaceId" : "/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = Token(html) }));
        }
        async Task<UserFaceCredential> Credential()
        { using var scope = app.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserFaceCredentials.AsNoTracking().SingleAsync(x => x.UserId == owner.Id); }
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.OpenConnectionAsync();
                if (db.Database.GetDbConnection().Database != target.InitialCatalog) throw new InvalidOperationException("Target mismatch");
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var user in new[] { owner, empty }) { if (!(await manager.CreateAsync(user, secret)).Succeeded) throw new InvalidOperationException("Fixture creation failed"); usersToDelete.Add(user.Id); }
                db.UserFaceCredentials.Add(new UserFaceCredential { UserId = owner.Id, ProtectedTemplates = scope.ServiceProvider.GetRequiredService<FaceTemplateProtector>().Protect(owner.Id, samples), SampleCount = 5,
                    TemplateFormatVersion = 1, ModelIdentifier = FaceEnrollmentService.ModelIdentifier, ConsentVersion = FaceEnrollmentService.ConsentVersion, ConsentGrantedAt = DateTime.UtcNow, EnrolledAt = DateTime.UtcNow, EnrollmentPromptedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                foreach (var role in RoleNames.Primary.Where(x => x != RoleNames.Supplier))
                {
                    var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                        new(System.Security.Claims.ClaimTypes.NameIdentifier, empty.Id), new(System.Security.Claims.ClaimTypes.Role, role)], "SyntheticCheck"));
                    Check(await FaceSetupFlow.ShouldPromptAsync(db, principal, new DefaultHttpContext().Request), role + " new-account setup eligibility");
                }
                var supplier = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                    new(System.Security.Claims.ClaimTypes.NameIdentifier, empty.Id), new(System.Security.Claims.ClaimTypes.Role, RoleNames.Supplier)], "SyntheticCheck"));
                var profile = new SupplierProfile { UserId = empty.Id, CompanyName = "Synthetic setup eligibility", RegistrationNumber = Guid.NewGuid().ToString("N"), ContactName = "Synthetic", Status = "Pending", CreatedAt = DateTime.UtcNow };
                db.SupplierProfiles.Add(profile); await db.SaveChangesAsync();
                Check(!await FaceSetupFlow.ShouldPromptAsync(db, supplier, new DefaultHttpContext().Request), "Pending Supplier setup does not interrupt approval routing");
                profile.Status = "Approved"; await db.SaveChangesAsync();
                Check(await FaceSetupFlow.ShouldPromptAsync(db, supplier, new DefaultHttpContext().Request), "Approved Supplier new-account setup eligibility");
                db.SupplierProfiles.Remove(profile); await db.SaveChangesAsync();
            }
            using var normal = Client(); var loginPage = await normal.GetStringAsync("/Account/Login");
            Check(loginPage.Contains("Login with Face ID + Password") && loginPage.Contains("formaction=\"/Account/LoginWithFaceId\""), "New button uses dedicated action and existing password form");
            Check(!loginPage.Contains("<video") && !Regex.IsMatch(loginPage, "<script[^>]+src=\"[^\"]*face-api\\.js"), "Public login does not open camera or load biometrics");
            using var anonymousModal = Client();
            var protectedModal = await anonymousModal.GetAsync("/FaceLogin/Index");
            Check(protectedModal.StatusCode == HttpStatusCode.Redirect && protectedModal.Headers.Location!.OriginalString.Contains("/Account/Login"), "Post-login modal view rejects anonymous access");
            Check((await anonymousModal.GetAsync("/FaceSetup")).StatusCode == HttpStatusCode.Redirect, "Anonymous setup page rejected");
            Check((await anonymousModal.PostAsync("/FaceSetup/Decline", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode == HttpStatusCode.Redirect, "Anonymous decline cannot create setup session preference");
            var normalLogin = await Login(normal, owner.Email!, secret, false);
            Check(normalLogin.StatusCode == HttpStatusCode.Redirect && normalLogin.Headers.Location!.OriginalString.Contains("GoToDashboard"), "Original password login routing unchanged");
            using var client = Client(); var signedIn = await Login(client, owner.Email!, secret);
            Check(signedIn.StatusCode == HttpStatusCode.Redirect && signedIn.Headers.Location!.OriginalString == "/FaceLogin", "Face option redirects to authenticated Login-styled modal view");
            Check(signedIn.Headers.Contains("Set-Cookie"), "Password pipeline issues Identity cookie before comparison");
            var modalPage = await client.GetAsync(signedIn.Headers.Location);
            var page = await modalPage.Content.ReadAsStringAsync();
            Check(modalPage.StatusCode == HttpStatusCode.OK && modalPage.Headers.Location == null, "Authenticated modal landing is rendered without a login redirect loop");
            Check(modalPage.Headers.CacheControl?.NoStore == true && !modalPage.Headers.Contains("Set-Cookie"), "Modal view is no-store and creates no additional authentication cookie");
            Check(page.Contains("id=\"face-login-modal\"") && page.Contains("modal-fullscreen-sm-down") && page.Contains("data-bs-dismiss=\"modal\""), "Bootstrap modal includes responsive layout and dismiss controls");
            Check(page.Contains("id=\"verification-start\"") && page.Contains("Start camera"), "Prominent manual camera fallback provided");
            Check(!page.Contains("type=\"password\"") && !page.Contains(secret), "Authenticated Login-styled view never re-renders submitted password");
            using var inline = Client(); inline.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            var deniedFragment = await inline.GetAsync("/FaceLogin/Modal");
            Check(deniedFragment.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, "Anonymous inline modal fragment rejected");
            var badInline = await Login(inline, owner.Email!, secret + "wrong");
            Check(badInline.StatusCode == HttpStatusCode.BadRequest && !badInline.Headers.Contains("Set-Cookie") && !(await badInline.Content.ReadAsStringAsync()).Contains("modalUrl"), "Inline wrong password cannot reveal or open biometric modal");
            var inlineLogin = await Login(inline, owner.Email!, secret);
            Check(inlineLogin.StatusCode == HttpStatusCode.OK && inlineLogin.Headers.Location == null && inlineLogin.Headers.Contains("Set-Cookie"), "Inline password login returns JSON without page navigation");
            var inlineResult = await inlineLogin.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var inlineModalUrl = inlineResult.GetProperty("modalUrl").GetString();
            Check(inlineModalUrl == "/FaceSetup/Modal", "Inline login returns authenticated setup-or-verification fragment URL");
            var fragment = await inline.GetAsync(inlineModalUrl); var fragmentHtml = await fragment.Content.ReadAsStringAsync();
            Check(fragment.IsSuccessStatusCode && fragmentHtml.Contains("id=\"face-login-modal\"") && !fragmentHtml.Contains("<html") && !fragmentHtml.Contains("type=\"password\""), "Modal HTML is loaded into existing Login document, not a new page");
            Check(fragmentHtml.Contains("data-success-redirect=\"/Account/GoToDashboard\""), "Successful modal comparison targets existing role dispatcher");
            inline.DefaultRequestHeaders.Add("RequestVerificationToken", Token(fragmentHtml));
            var inlineMatch = await inline.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(inlineMatch.IsSuccessStatusCode && !inlineMatch.Headers.Contains("Set-Cookie") && (await inlineMatch.Content.ReadAsStringAsync()).Contains("Matched"), "Fresh post-login anti-forgery token allows comparison without another authentication cookie");
            Check(page.Contains("Password sign-in is complete") && page.Contains("Continue to workspace"), "Post-login page explains optional check and provides dashboard continuation");
            Check((await client.GetAsync("/js/face-login-modal.js")).IsSuccessStatusCode && (await client.GetAsync("/css/face-login-modal.css")).IsSuccessStatusCode, "Modal assets are served");
            var standalone = await client.GetStringAsync("/FaceVerification/Index");
            Check(standalone.Contains("verification-form") && !standalone.Contains("id=\"face-login-modal\""), "Standalone verification page remains available");
            var before = await Credential();
            client.DefaultRequestHeaders.Add("RequestVerificationToken", Token(page));
            var match = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(match.IsSuccessStatusCode && (await match.Content.ReadAsStringAsync()).Contains("Matched"), "Similarity succeeds within authenticated session");
            Check(!match.Headers.Contains("Set-Cookie"), "Face match does not issue a cookie");
            var mismatch = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = Enumerable.Repeat(0.2f, 128).ToArray(), modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(mismatch.IsSuccessStatusCode && (await mismatch.Content.ReadAsStringAsync()).Contains("Not Matched"), "Failed similarity returns Not Matched");
            Check((await client.GetAsync("/Account/GoToDashboard")).StatusCode == HttpStatusCode.Redirect, "Optional mismatch does not replace normal dashboard routing");
            var after = await Credential();
            Check(before.RowVersion.SequenceEqual(after.RowVersion) && before.ProtectedTemplates!.SequenceEqual(after.ProtectedTemplates!), "Original synthetic enrollment unchanged by login/comparison");
            using var missing = Client(); var emptyLogin = await Login(missing, empty.Email!, secret);
            var missingPage = await missing.GetStringAsync(emptyLogin.Headers.Location);
            Check(missingPage.Contains("You have no face enrollment"), "Missing enrollment disclosed only after successful password login");
            Check(missingPage.Contains("id=\"face-login-modal\"") && missingPage.Contains("/js/face-login-modal.js") && !missingPage.Contains("face-api.js"), "Missing enrollment still opens helpful modal without initializing camera models");
            var enrollment = await missing.GetStringAsync("/FaceEnrollment/Index"); missing.DefaultRequestHeaders.Add("RequestVerificationToken", Token(enrollment));
            var cross = await missing.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier, userId = owner.Id });
            Check(cross.StatusCode == HttpStatusCode.BadRequest, "Another user's enrolled face cannot be selected by request UserId");
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                if (!await roleManager.RoleExistsAsync(RoleNames.TopCommittee)) { await roleManager.CreateAsync(new IdentityRole(RoleNames.TopCommittee)); createdRoles.Add(RoleNames.TopCommittee); }
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                await userManager.AddToRoleAsync((await userManager.FindByIdAsync(empty.Id))!, RoleNames.TopCommittee);
            }
            // Login again to load the synthetic employee's newly assigned role into the real cookie.
            using var setupClient = Client(); await Login(setupClient, empty.Email!, secret, false);
            var setupRoute = await setupClient.GetAsync("/Account/GoToDashboard");
            Check(setupRoute.Headers.Location?.OriginalString == "/FaceSetup", "Unenrolled employee receives automatic setup before dashboard");
            var setupPage = await setupClient.GetStringAsync("/FaceSetup");
            Check(setupPage.Contains("Set Up Face ID") && setupPage.Contains("Register My Face") && setupPage.Contains("Use password only"), "Voluntary setup modal renders");
            Check(setupPage.Contains("face-consent") && setupPage.Contains("face-password"), "Setup preserves consent and password confirmation controls");
            Check((await setupClient.GetAsync("/FaceSetup/Modal")).IsSuccessStatusCode, "Authenticated inline setup fragment available");
            setupClient.DefaultRequestHeaders.Add("RequestVerificationToken", Token(setupPage));
            var noConsent = await setupClient.PostAsJsonAsync("/FaceEnrollment/Save", new { password = secret, consent = false, descriptors = samples });
            Check(noConsent.StatusCode == HttpStatusCode.BadRequest, "Setup cannot save without explicit consent");
            var setupSave = await setupClient.PostAsJsonAsync("/FaceEnrollment/Save", new { password = secret, consent = true, descriptors = samples });
            Check(setupSave.IsSuccessStatusCode, "Five synthetic setup samples save through existing enrollment endpoint");
            Check((await setupClient.GetAsync("/Account/GoToDashboard")).Headers.Location?.OriginalString == "/TopCommittee/Dashboard", "Enrolled employee bypasses setup and retains role routing");
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.UserFaceCredentials.Remove(await db.UserFaceCredentials.SingleAsync(x => x.UserId == empty.Id)); await db.SaveChangesAsync();
            }
            using var skipRequest = new HttpRequestMessage(HttpMethod.Post, "/FaceSetup/Decline") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(setupPage) }) };
            setupClient.DefaultRequestHeaders.Remove("RequestVerificationToken");
            Check((await setupClient.PostAsync("/FaceSetup/Decline", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode == HttpStatusCode.BadRequest, "Decline requires anti-forgery validation");
            setupClient.DefaultRequestHeaders.Add("RequestVerificationToken", Token(setupPage));
            skipRequest.Headers.Accept.ParseAdd("application/json");
            Check((await setupClient.SendAsync(skipRequest)).IsSuccessStatusCode, "Password-only choice is persisted as session UI preference");
            Check((await setupClient.GetAsync("/Account/GoToDashboard")).Headers.Location?.OriginalString == "/TopCommittee/Dashboard", "Declined setup does not repeat in current authenticated session");
            Check((await setupClient.GetAsync("/FaceSetup")).Headers.Location?.OriginalString == "/Account/GoToDashboard", "Declined setup cannot produce a redirect loop");
            Check((await setupClient.GetAsync("/FaceEnrollment/Index")).IsSuccessStatusCode, "Later manual enrollment remains available after decline");
            await setupClient.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(setupPage) }));
            setupClient.DefaultRequestHeaders.Remove("RequestVerificationToken"); await Login(setupClient, empty.Email!, secret, false);
            Check((await setupClient.GetAsync("/Account/GoToDashboard")).Headers.Location?.OriginalString == "/FaceSetup", "New password login resets prior session's decline preference");
            using var anonymous = Client();
            var anonymousMatch = await anonymous.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(anonymousMatch.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized && !anonymousMatch.Headers.Contains("Set-Cookie"), "Face-only anonymous request cannot authenticate");
            var noCsrf = await anonymous.PostAsync("/Account/LoginWithFaceId", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = owner.Email!, ["Password"] = secret }));
            Check(noCsrf.StatusCode == HttpStatusCode.BadRequest && !noCsrf.Headers.Contains("Set-Cookie"), "Face option requires anti-forgery before password processing");
            Check((await anonymous.GetAsync("/Account/LoginWithFaceId")).StatusCode == HttpStatusCode.MethodNotAllowed, "New login action rejects GET");
            var validationPage = await anonymous.GetStringAsync("/Account/Login");
            var missingFields = await anonymous.PostAsync("/Account/LoginWithFaceId", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(validationPage) }));
            Check(missingFields.StatusCode == HttpStatusCode.OK && (await missingFields.Content.ReadAsStringAsync()).Contains("Enter your email address.") && !missingFields.Headers.Contains("Set-Cookie"), "Existing required email/password validation preserved");
            var invalid = await Login(anonymous, owner.Email!, secret + "wrong"); var invalidHtml = await invalid.Content.ReadAsStringAsync();
            Check(invalid.StatusCode == HttpStatusCode.OK && invalidHtml.Contains("Unable to sign in.") && !invalid.Headers.Contains("Set-Cookie"), "Incorrect password keeps existing generic login error");
            var unknown = await Login(anonymous, "absent-" + Guid.NewGuid().ToString("N") + "@example.invalid", secret);
            Check(unknown.StatusCode == invalid.StatusCode && (await unknown.Content.ReadAsStringAsync()).Contains("Unable to sign in."), "Unknown account gets same generic error without enrollment lookup");
            using (var scope = app.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(empty.Id))!;
                user.IsActive = false; await manager.UpdateAsync(user);
            }
            using var inactiveClient = Client(); var inactive = await Login(inactiveClient, empty.Email!, secret);
            Check(inactive.StatusCode == HttpStatusCode.OK && !inactive.Headers.Contains("Set-Cookie"), "Inactive account cannot authenticate through face option");
            using (var scope = app.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(empty.Id))!;
                user.IsActive = true; await manager.UpdateAsync(user); await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
            }
            using var lockedClient = Client(); var locked = await Login(lockedClient, empty.Email!, secret);
            Check(locked.StatusCode == HttpStatusCode.OK && !locked.Headers.Contains("Set-Cookie"), "Locked account cannot authenticate through face option");
            using (var scope = app.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(empty.Id))!;
                await manager.SetLockoutEndDateAsync(user, null); await manager.ResetAccessFailedCountAsync(user);
            }
            using var bruteForce = Client();
            for (var i = 0; i < 5; i++) await Login(bruteForce, empty.Email!, secret + "wrong");
            using (var scope = app.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                Check(await manager.IsLockedOutAsync((await manager.FindByIdAsync(empty.Id))!), "Five incorrect face-option passwords trigger existing Identity lockout");
            }
            var correctButLocked = await Login(bruteForce, empty.Email!, secret);
            Check(correctButLocked.StatusCode == HttpStatusCode.OK && !correctButLocked.Headers.Contains("Set-Cookie"), "Correct password cannot bypass triggered lockout");
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var row = await db.UserFaceCredentials.SingleAsync(x => x.UserId == owner.Id);
                row.ProtectedTemplates = [1, 2, 3]; await db.SaveChangesAsync();
            }
            var corrupt = await client.PostAsJsonAsync("/FaceVerification/Verify", new { descriptor = samples[0], modelIdentifier = FaceEnrollmentService.ModelIdentifier });
            Check(corrupt.StatusCode == HttpStatusCode.BadRequest && !corrupt.Headers.Contains("Set-Cookie"), "Corrupt synthetic enrollment produces safe failure without authentication changes");
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var row = await db.UserFaceCredentials.SingleAsync(x => x.UserId == owner.Id);
                row.ProtectedTemplates = before.ProtectedTemplates; await db.SaveChangesAsync();
            }
            // Own synthetic role/profile fixtures only; no team users or assignments are changed.
            foreach (var role in RoleNames.Primary)
            {
                using (var scope = app.Services.CreateScope())
                {
                    var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(); var user = (await manager.FindByIdAsync(owner.Id))!;
                    if (!await roles.RoleExistsAsync(role)) { await roles.CreateAsync(new IdentityRole(role)); createdRoles.Add(role); }
                    await manager.RemoveFromRolesAsync(user, await manager.GetRolesAsync(user));
                    if (!(await manager.AddToRoleAsync(user, role)).Succeeded) throw new InvalidOperationException("Role fixture failed");
                    if (role == RoleNames.Supplier)
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        db.SupplierProfiles.Add(new SupplierProfile { UserId = user.Id, CompanyName = "Synthetic Face Login Supplier", RegistrationNumber = Guid.NewGuid().ToString("N"), ContactName = "Synthetic", Status = "Pending", CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
                    }
                }
                using var roleClient = Client(); var roleLogin = await Login(roleClient, owner.Email!, secret);
                Check(roleLogin.StatusCode == HttpStatusCode.Redirect && roleLogin.Headers.Location!.OriginalString == "/FaceLogin", role + " uses face-option password pipeline");
                var route = await roleClient.GetAsync("/Account/GoToDashboard");
                var expected = role == RoleNames.Supplier ? "/Account/RegistrationStatus" : "/" + role + "/Dashboard";
                Check(route.StatusCode == HttpStatusCode.Redirect && route.Headers.Location!.OriginalString == expected, role + " retains correct dashboard/status routing");
                if (role == RoleNames.Supplier)
                {
                    // Current team Dashboard is role-only; approval is checked by routing/policies and business actions.
                    var dashboard = await roleClient.GetAsync("/Supplier/Dashboard");
                    Check(dashboard.IsSuccessStatusCode, "Existing role-only Supplier dashboard behavior preserved");
                    var blocked = await roleClient.GetAsync("/__face-checks/approved-supplier");
                    Check(blocked.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, "Pending Supplier denied by existing ApprovedSupplier policy");
                    using (var scope = app.Services.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var profile = await db.SupplierProfiles.SingleAsync(x => x.UserId == owner.Id); profile.Status = "Approved"; await db.SaveChangesAsync();
                    }
                    var approved = await roleClient.GetAsync("/Account/GoToDashboard");
                    Check(approved.Headers.Location?.OriginalString == "/Supplier/Dashboard", "Approved Supplier retains supplier dashboard routing");
                    Check((await roleClient.GetAsync("/__face-checks/approved-supplier")).IsSuccessStatusCode, "Approved Supplier passes existing approval policy");
                }
            }
            // Fill only the new action's limiter; normal Password Login must remain independent.
            HttpResponseMessage? limited = null;
            for (var i = 0; i < 31; i++) limited = await anonymous.PostAsync("/Account/LoginWithFaceId", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Check(limited!.StatusCode == HttpStatusCode.TooManyRequests, "New login option is rate limited");
            using var unaffected = Client(); var unaffectedLogin = await Login(unaffected, owner.Email!, secret, false);
            Check(unaffectedLogin.StatusCode == HttpStatusCode.Redirect && unaffectedLogin.Headers.Location!.OriginalString.Contains("GoToDashboard"), "Face-option rate limit does not block original password login");
        }
        finally
        {
            await app.StopAsync(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserFaceCredentials.RemoveRange(await db.UserFaceCredentials.Where(x => usersToDelete.Contains(x.UserId)).ToListAsync());
            db.SupplierProfiles.RemoveRange(await db.SupplierProfiles.Where(x => usersToDelete.Contains(x.UserId)).ToListAsync()); await db.SaveChangesAsync();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var id in usersToDelete) { var user = await manager.FindByIdAsync(id); if (user != null && !(await manager.DeleteAsync(user)).Succeeded) throw new InvalidOperationException("Own synthetic user cleanup failed"); }
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var name in createdRoles) { var role = await roles.FindByNameAsync(name); if (role != null && !(await roles.DeleteAsync(role)).Succeeded) throw new InvalidOperationException("Own synthetic role cleanup failed"); }
        }
        Console.WriteLine($"Face login checks: {count} passed; own isolated fixtures removed."); return count;
    }
}
