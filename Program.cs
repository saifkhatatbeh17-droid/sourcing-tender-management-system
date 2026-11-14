using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.TopCommittee;
using SourcingTenderManagement.Services.FaceEnrollment;
using SourcingTenderManagement.Services.FaceVerification;
using SourcingTenderManagement.Services.FaceLogin;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
var connection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is missing.");
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<SourcingTenderManagement.Services.Evaluation.BidRecommendationService>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddTopCommitteeFoundation();
builder.Services.AddFaceEnrollment();
builder.Services.AddFaceVerification(builder.Configuration);
builder.Services.AddFaceLogin();
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 8;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<IdentityPasskeyOptions>(o => o.UserVerificationRequirement = "required");
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "SourcingTenderManagement.CodeFirst";
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true) return;
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(context.Principal);
        if (user == null || !user.IsActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
        }
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy("ApprovedSupplier", policy =>
    policy.RequireRole(RoleNames.Supplier).RequireAssertion(async context =>
    {
        if (context.Resource is not HttpContext http) return false;
        var users = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var userId = users.GetUserId(context.User);
        var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();
        return await db.SupplierProfiles.AnyAsync(s => s.UserId == userId && s.Status == "Approved");
    })));

var app = builder.Build();
// This dedicated check path runs before startup role seeding and performs no database writes.
if (args.Contains("--top-committee-foundation-check"))
{
    using var checkScope = app.Services.CreateScope();
    await SourcingTenderManagement.Checks.TopCommitteeFoundationChecks.RunAsync(checkScope.ServiceProvider);
    return;
}
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in RoleNames.All)
        if (!await roles.RoleExistsAsync(role))
        {
            var result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    if (args.Contains("--create-admin"))
    {
        var index = Array.IndexOf(args, "--create-admin");
        if (index + 1 >= args.Length) throw new ArgumentException("Provide an admin email after --create-admin.");
        var password = Environment.GetEnvironmentVariable("TENDER_ADMIN_PASSWORD")
            ?? throw new ArgumentException("Use scripts/create-admin.ps1 to supply the password securely.");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = args[index + 1];
        if (await users.FindByEmailAsync(email) != null) throw new InvalidOperationException("An account with this email already exists. No changes made.");
        var auth = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await auth.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Administrator", EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, RoleNames.Admin);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        await transaction.CommitAsync();
        Console.WriteLine("Administrator created. No default password is stored in this project.");
        return;
    }
    if (args.Contains("--self-test"))
    {
        await SourcingTenderManagement.Checks.DatabaseChecks.RunAsync(scope.ServiceProvider);
        return;
    }
}
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
