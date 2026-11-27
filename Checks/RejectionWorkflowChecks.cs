using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website.

public static class RejectionWorkflowChecks
{
    public static async Task RunAsync(string root, string? restoredCopyConnection = null)
    {
        // This runner owns exactly one newly named database. Never uses application connection settings.
        var database = "SourcingTender_RejectionCheck_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=localhost;Database={database};Trusted_Connection=True;TrustServerCertificate=True;";
        if (restoredCopyConnection != null)
        {
            var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(restoredCopyConnection);
            if (target.DataSource != "localhost"
                || !System.Text.RegularExpressions.Regex.IsMatch(target.InitialCatalog, "^SourcingTender_RejectionQA_[a-f0-9]{32}$"))
                throw new InvalidOperationException("Only a specifically named local disposable QA copy is allowed.");
            database = target.InitialCatalog; connection = restoredCopyConnection;
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(RejectionWorkflowChecks).Assembly.GetName().Name,
            ContentRootPath = root,
            EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddTopCommitteeFoundation();
        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ActorHandler>("RejectionTest", _ => {});
        builder.Services.Configure<AuthenticationOptions>(o => { o.DefaultAuthenticateScheme="RejectionTest";o.DefaultChallengeScheme="RejectionTest";o.DefaultForbidScheme="RejectionTest"; });
        await using var app = builder.Build();
        app.UseRouting();app.UseAuthentication();app.UseAuthorization();
        app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}");
        app.MapGet("/__rejection/token",(HttpContext http,IAntiforgery anti) => anti.GetAndStoreTokens(http).RequestToken!);
        var owned = false; var passed = 0;
        void Check(bool condition,string label) { if(!condition)throw new InvalidOperationException(label);passed++;Console.WriteLine("PASS rejection: "+label); }
        HttpClient Client(string? actor) { var c = new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(app.Urls.Single())};if(actor!=null)c.DefaultRequestHeaders.Add("X-Rejection-Actor",actor);return c; }
        async Task<HttpResponseMessage> Post(HttpClient client,string route,Dictionary<string,string> values,bool csrf=true) { if(csrf)values["__RequestVerificationToken"]=await client.GetStringAsync("/__rejection/token");return await client
            .PostAsync(route,new FormUrlEncodedContent(values)); }
        async Task<T> Read<T>(Func<ApplicationDbContext,Task<T>> read) { using var scope=app.Services.CreateScope();return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()); }
        Dictionary<string,string> Vote(int tender,int bid,string decision,string reason="") => new(){["Vote.TenderId"]=tender.ToString(),["Vote.ExpectedNominatedBidId"]=bid
            .ToString(),["Vote.Decision"]=decision,["Vote.Notes"]=reason,["Vote.Confirmed"]="true"};
        try
        {
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (restoredCopyConnection == null)
                { if(await db.Database.CanConnectAsync())throw new InvalidOperationException("Test database must not already exist."); owned=true;await db.Database.MigrateAsync(); }
                else
                {
                    if(!await db.Database.CanConnectAsync()
                        || (await db.Database.GetPendingMigrationsAsync())
                        .Any()) throw new InvalidOperationException("QA copy must be restored and migrated first.");
                    if(await db.Users.AnyAsync(u => u.Id == "officer"
                        || u.Id == "manager"
                        || u.Id == "supplier"
                        || u.UserName!.EndsWith("@example.invalid"))) throw new InvalidOperationException("Fixture namespace collision.");
                    owned=true;
                }
                var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();foreach(var role in RoleNames.All)await roles
                    .CreateAsync(new IdentityRole(role));
                var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach(var pair in new[]{("officer",RoleNames.SourcingOfficer),
                    ("outsider",RoleNames.SourcingOfficer),
                    ("manager",RoleNames.SourcingManager),
                    ("othermanager",RoleNames.SourcingManager),
                    ("supplier",RoleNames.Supplier),
                    ("department",RoleNames.Department),
                    ("top1",RoleNames.TopCommittee),
                    ("top2",RoleNames.TopCommittee),
                    ("top3",RoleNames.TopCommittee),
                    ("top4",RoleNames.TopCommittee)})
                {var u=new ApplicationUser{Id=pair.Item1,UserName=pair.Item1+"@example.invalid",Email=pair.Item1+"@example.invalid",FullName=pair.Item1,IsActive=true};var result=await users.CreateAsync(u);if(!result.Succeeded)throw new Exception("Fixture user failed");await users
                    .AddToRoleAsync(u,pair.Item2);}
            }
            await app.StartAsync();
            async Task<(int Tender,int Bid,int Request)> Fixture()
            {
                using var scope=app.Services.CreateScope();var db=scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>();
                var dep=new Department{Name="Test "+Guid.NewGuid(),IsActive=true};db.Departments.Add(dep);await db
                    .SaveChangesAsync();
                var req=new PurchaseRequest{DepartmentId=dep.DepartmentId,
                    RequestedByUserId="department",
                    Title="Rejected cycle test",
                    Reason="Synthetic",
                    Priority="Normal",
                    Status="ConvertedToTender",
                    TargetDate=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14))};db.PurchaseRequests.Add(req);await db
                    .SaveChangesAsync();
                var t=new Tender{PurchaseRequestId=req.PurchaseRequestId,
                    CreatedByUserId="officer",
                    Title="Test tender",
                    Category="Laboratory instruments & supplies",
                    Description="Original description",
                    Terms="Original terms",
                    OpensAt=DateTime.UtcNow,
                    ClosesAt=DateTime.UtcNow.AddDays(7),
                    Status="AwaitingCommitteeDecision"};db.Tenders.Add(t);await db
                    .SaveChangesAsync();
                var supplier=new SupplierProfile{UserId="supplier",CompanyName="Supplier",Category="Laboratory instruments & supplies",RegistrationNumber=Guid.NewGuid()
                    .ToString(),ContactName="Supplier",Status="Approved"};
                // Reuse the same supplier account/profile across fixtures.
                supplier=await db.SupplierProfiles.FirstOrDefaultAsync(p => p.UserId == "supplier")
                    ??supplier;if(supplier.SupplierProfileId==0) {db.SupplierProfiles.Add(supplier);await db
                        .SaveChangesAsync();}
                var invite=new Invitation{TenderId=t.TenderId,
                    SupplierProfileId=supplier.SupplierProfileId,
                    InvitedByUserId="officer",
                    Status="Accepted"};db.Invitations.Add(invite);await db
                    .SaveChangesAsync();
                var bid=new Bid{InvitationId=invite.InvitationId,
                    TenderId=t.TenderId,
                    Currency="JOD",
                    Status="Compliant",
                    ComplianceStatus="Compliant",
                    SubmittedAt=DateTime.UtcNow,
                    DeliveryDays=7};db.Bids.Add(bid);await db
                    .SaveChangesAsync();
                db.EvaluationReports.Add(new(){TenderId=t.TenderId,
                    RecommendedBidId=bid.BidId,
                    Status="ManagerApproved",
                    ManagerSummary="Original nomination",
                    CommitteeSummary="Original report"});
                db.TenderApprovals.Add(new(){TenderId=t.TenderId,Stage="Manager",Decision="Approved",UserId="manager",Notes="Original manager approval"});
                for(var i=1;i<=4;i++)db.CommitteeAssignments.Add(new(){TenderId=t.TenderId,CommitteeType="Top",Position=i,UserId="top"+i});
                await db.SaveChangesAsync();return(t.TenderId,bid.BidId,req.PurchaseRequestId);
            }
            using var m1=Client("top1");using var m2=Client("top2");using var m3=Client("top3");using var m4=Client("top4");using var manager=Client("manager");using var officer=Client("officer");using var stranger=Client("outsider");using var otherManager=Client("othermanager");using var anon=Client(null);
            var f=await Fixture();
            foreach(var reason in new[]{"",
                "   ",
                new string('x',4001)})Check((await Post(m1,"/TopCommittee/SubmitVote",Vote(f.Tender,f.Bid,"Rejected",reason))).StatusCode==HttpStatusCode.BadRequest,"Rejection reason validated");
            Check((await Post(m1,"/TopCommittee/SubmitVote",Vote(f.Tender,f.Bid,"Approved"),false)).StatusCode==HttpStatusCode.BadRequest,"CSRF required");
            Check((await Post(anon,
                "/TopCommittee/SubmitVote",
                Vote(f.Tender,f.Bid,"Rejected","No access"))).StatusCode==HttpStatusCode.Unauthorized,"Anonymous vote blocked");
            Check((await Post(stranger,
                "/TopCommittee/SubmitVote",
                Vote(f.Tender,f.Bid,"Rejected","No role"))).StatusCode==HttpStatusCode.Forbidden,"Wrong role blocked");
            Check((await Post(m1,"/TopCommittee/SubmitVote",Vote(f.Tender,f.Bid,"Approved"))).StatusCode==HttpStatusCode.Redirect,"Prior approval recorded");
            const string exact="  Exact rejection reason.\r\nPlease re-tender.  ";
            Check((await Post(m2,"/TopCommittee/SubmitVote",Vote(f.Tender,f.Bid,"Rejected",exact))).StatusCode==HttpStatusCode.Redirect,"One reject accepted");
            Check(await Read(db=>db.Tenders.AnyAsync(t=>t.TenderId==f.Tender
                &&t.Status=="AwaitingManagerReview")),"Returns immediately to manager");
            Check(await Read(db=>db.TenderApprovals.AnyAsync(a=>a.TenderId==f.Tender
                &&a.UserId=="top2"
                &&a.Decision=="Rejected"
                &&a.Notes==exact
                &&a.DecidedAt!=default)),"Exact reason, member and date preserved");
            Check((await Post(m3,"/TopCommittee/SubmitVote",Vote(f.Tender,f.Bid,"Approved"))).StatusCode==HttpStatusCode.BadRequest,"Further voting stopped");
            Check((await Post(m2,
                "/TopCommittee/SubmitVote",
                Vote(f.Tender,f.Bid,"Rejected",exact))).StatusCode==HttpStatusCode.BadRequest,"Duplicate reject blocked");
            var view=await manager.GetStringAsync("/SourcingManager/Submissions?rejectedTenderId="+f.Tender);Check(view.Contains("manager-rejections")
                &&view
                .Contains("top2")
                &&view
                .Contains("Exact rejection reason.")
                &&view
                .Contains("Forward to Sourcing Officer"),"Manager sees rejection and forward action");
            Check((await otherManager.GetAsync("/SourcingManager/Submissions?rejectedTenderId="+f.Tender)).StatusCode==HttpStatusCode.NotFound,"Other manager cannot view rejection");
            Check((await Post(otherManager,
                "/SourcingManager/ForwardRejection",
                new(){["id"]=f.Tender.ToString()})).StatusCode==HttpStatusCode.Forbidden,"Other manager cannot forward");
            Check((await Post(manager,"/SourcingManager/Forward",new(){["ReportId"]=await Read(db=>db.EvaluationReports.Where(r=>r.TenderId==f.Tender).Select(r=>r.EvaluationReportId.ToString()).SingleAsync()),["Reason"]="Cannot reopen"})).StatusCode==HttpStatusCode.Redirect
                &&await Read(db=>db.Tenders.AnyAsync(t=>t.TenderId==f.Tender&&t.Status=="AwaitingManagerReview")),"Rejected round cannot be referred back for voting");
            var forward=await Task.WhenAll(Post(manager,"/SourcingManager/ForwardRejection",new(){["id"]=f.Tender.ToString()}),Post(manager,"/SourcingManager/ForwardRejection",new(){["id"]=f.Tender
                .ToString()}));
            Check(forward.Count(r=>r.StatusCode==HttpStatusCode.Redirect)==1
                &&forward
                .Count(r=>r.StatusCode==HttpStatusCode.Conflict)==1,"Concurrent forwarding only once");
            Check(await Read(db=>db.Notifications.CountAsync(n=>n.UserId=="officer"
                &&n.Title=="Rejected tender forwarded to you"))==1,"Officer notified exactly once");
            Check((await officer.GetStringAsync("/SourcingOfficer/Offers?tenderId="+f.Tender)).Contains("Exact rejection reason."),"Officer receives original rejection");
            Check((await stranger.GetAsync("/SourcingOfficer/Create?replacesTenderId="+f.Tender)).StatusCode==HttpStatusCode.NotFound,"Other officer cannot replace");
            var createHtml=await officer.GetStringAsync("/SourcingOfficer/Create?replacesTenderId="+f.Tender);
            Check(createHtml.Contains("ReplacesTenderId")
                && createHtml
                .Contains("name=\"PurchaseRequestId\"")
                && createHtml
                .Contains("name=\"Title\" value=\"\"")
                && !createHtml
                .Contains("Original description"),"Normal creation form opens with blank tender details and purchase request selector");
            Dictionary<string,string> Replacement(bool launch=false, bool includeSupplier=true)=>new(){["supplierProfileIds[0]"]=includeSupplier?Read(db=>db.SupplierProfiles.Where(p=>p.UserId=="supplier").Select(p=>p.SupplierProfileId.ToString()).SingleAsync()).GetAwaiter()
                .GetResult():"0",["ReplacesTenderId"]=f.Tender
                .ToString(),["PurchaseRequestId"]=f.Request
                .ToString(),["Title"]="Replacement",["Category"]="Laboratory instruments & supplies",["Description"]="New description",["OpensAt"]=DateTime.UtcNow
                .ToString("s"),["ClosesAt"]=DateTime.UtcNow
                .AddDays(7)
                .ToString("s"),["launch"]=launch
                .ToString()};
            Check((await Post(officer,"/SourcingOfficer/Create",Replacement(true, false))).StatusCode==HttpStatusCode.OK
                && !await Read(db=>db.Tenders.AnyAsync(t=>t.ReplacesTenderId==f.Tender)),"Publishing requires approved suppliers");
            using var officer2=Client("officer");var replacements=await Task.WhenAll(Post(officer,
                "/SourcingOfficer/Create",
                Replacement()),Post(officer2,
                "/SourcingOfficer/Create",
                Replacement()));
            Check(replacements.Count(r=>r.StatusCode==HttpStatusCode.Redirect)==1
                &&replacements
                .Count(r=>r.StatusCode==HttpStatusCode.Conflict)==1,"Concurrent replacements only once");
            Check(await Read(db=>db.Tenders.CountAsync(t=>t.ReplacesTenderId==f.Tender
                &&t.Status=="Published"
                &&t.Invitations.Count==1
                &&t.PurchaseRequestId==f.Request))==1,"Separate linked replacement published with supplier invitation");
            Check((await Post(officer,
                "/SourcingOfficer/ReviewAndSend",
                new(){["tenderId"]=f.Tender.ToString()})).StatusCode==HttpStatusCode.Conflict,"Rejected original cannot re-enter evaluation");
            Check(await Read(db=>db.TenderApprovals.CountAsync(a=>a.TenderId==f.Tender))==3
                &&await Read(db=>db.Bids.AnyAsync(b=>b.BidId==f.Bid))
                &&await Read(db=>db.EvaluationReports.AnyAsync(r=>r.TenderId==f.Tender&&r.CommitteeSummary=="Original report")),"Original votes, bids and report intact");
            var launchSource=await Fixture();
            await Post(m2,"/TopCommittee/SubmitVote",Vote(launchSource.Tender,launchSource.Bid,"Rejected","Reissue for supplier invitations"));
            await Post(manager,"/SourcingManager/ForwardRejection",new(){["id"]=launchSource.Tender.ToString()});
            var publish=Replacement(true);publish["ReplacesTenderId"]=launchSource.Tender.ToString();publish["PurchaseRequestId"]=launchSource.Request
                .ToString();publish["supplierProfileIds[0]"]=await Read(db=>db.SupplierProfiles.Where(p=>p.UserId=="supplier")
                    .Select(p=>p.SupplierProfileId.ToString())
                    .SingleAsync());
            Check((await Post(officer,
                "/SourcingOfficer/Create",
                publish)).StatusCode==HttpStatusCode.Redirect,"Replacement can publish using normal creation action");
            Check(await Read(db=>db.Tenders.AnyAsync(t=>t.ReplacesTenderId==launchSource.Tender
                &&t.Status=="Published"
                &&t.Invitations.Count==1)),"Replacement is published with a fresh supplier invitation");
            Check((await Post(officer,"/SourcingOfficer/Create",publish)).StatusCode==HttpStatusCode.Conflict,"Published replacement cannot be duplicated");
            var award=await Fixture();foreach(var c in new[]{m1,
                m2,
                m3,
                m4})Check((await Post(c,"/TopCommittee/SubmitVote",Vote(award.Tender,award.Bid,"Approved"))).StatusCode==HttpStatusCode.Redirect,"Approval accepted");
            Check(await Read(db=>db.Tenders.AnyAsync(t=>t.TenderId==award.Tender
                &&t.Status=="Awarded"
                &&t.AwardedBidId==award.Bid)),"Four approvals still award");
            var race=await Fixture();await Task.WhenAll(Post(m1,
                "/TopCommittee/SubmitVote",
                Vote(race.Tender,race.Bid,"Rejected","Concurrent rejection")),Post(m2,
                "/TopCommittee/SubmitVote",
                Vote(race.Tender,race.Bid,"Approved")));
            Check(await Read(db=>db.Tenders.AnyAsync(t=>t.TenderId==race.Tender
                &&t.Status=="AwaitingManagerReview")),"Concurrent approve/reject ends rejected");
            Check((await Post(m4,
                "/TopCommittee/SubmitVote",
                Vote(race.Tender,race.Bid,"Approved"))).StatusCode==HttpStatusCode.BadRequest,"Race leaves voting closed");
            // Regression checks: exercise existing normal paths without touching application data.
            foreach(var publishNormally in new[]{false,true})
            {
                int normalRequest;
                using(var scope=app.Services.CreateScope())
                {
                    var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var request=new PurchaseRequest{DepartmentId=await db.Departments.Where(d=>d.Name.StartsWith("Test "))
                        .Select(d=>d.DepartmentId)
                        .FirstAsync(),RequestedByUserId="department",ReviewedByUserId="officer",Title="Regular request",Reason="Normal requirements",Priority="Normal",Status="Approved",TargetDate=DateOnly
                        .FromDateTime(DateTime.UtcNow.AddDays(14))};
                    db.PurchaseRequests.Add(request);await db
                        .SaveChangesAsync();normalRequest=request.PurchaseRequestId;
                }
                var normalHtml=await officer.GetStringAsync("/SourcingOfficer/Create?purchaseRequestId="+normalRequest);
                Check(normalHtml.Contains("Regular request")
                    &&normalHtml
                    .Contains("Normal requirements"),"Normal creation retains purchase-request prefill");
                var normal=Replacement(publishNormally);normal.Remove("ReplacesTenderId");normal["PurchaseRequestId"]=normalRequest
                    .ToString();normal["supplierProfileIds[0]"]=await Read(db=>db.SupplierProfiles.Where(p=>p.UserId=="supplier")
                        .Select(p=>p.SupplierProfileId.ToString())
                        .SingleAsync());
                var invalid=new Dictionary<string,string>(normal);invalid["Description"]="";
                Check((await Post(officer,"/SourcingOfficer/Create",invalid)).StatusCode==HttpStatusCode.OK
                    &&!await Read(db=>db.Tenders.AnyAsync(t=>t.PurchaseRequestId==normalRequest)),"Normal creation validation still blocks missing description");
                Check((await Post(officer,"/SourcingOfficer/Create",normal)).StatusCode==HttpStatusCode.Redirect,"Normal tender creation succeeds");
                Check(await Read(db=>db.Tenders.AnyAsync(t=>t.PurchaseRequestId==normalRequest
                    &&t.ReplacesTenderId==null
                    &&t.Status=="Published"
                    &&t.Invitations.Count==1)),"Tender publishes directly even when legacy launch=false is posted");
            }
            var regularManager=await Fixture();int regularReport;
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var t=await db.Tenders
                    .SingleAsync(t=>t.TenderId==regularManager.Tender);t.Status="AwaitingManagerReview";
                var r=await db.EvaluationReports.SingleAsync(r=>r.TenderId==regularManager.Tender);r.Status="OfficerSubmitted";regularReport=r.EvaluationReportId;await db
                    .SaveChangesAsync();
            }
            Check((await manager.GetAsync("/SourcingManager/Submissions?id="+regularReport)).StatusCode==HttpStatusCode.OK,"Normal manager submission still renders");
            Check((await Post(manager,"/SourcingManager/Decide",new(){["ReportId"]=regularReport.ToString(),["Decision"]="Approved",["Notes"]="Normal manager approval"})).StatusCode==HttpStatusCode.Redirect
                &&await Read(db=>db.EvaluationReports.AnyAsync(r=>r.EvaluationReportId==regularReport&&r.Status=="ManagerApproved")),"Normal manager approval unchanged");
            Check((await Post(manager,"/SourcingManager/Forward",new(){["ReportId"]=regularReport.ToString(),["Reason"]="Normal referral"})).StatusCode==HttpStatusCode.Redirect
                &&await Read(db=>db.Tenders.AnyAsync(t=>t.TenderId==regularManager.Tender&&t.Status=="AwaitingCommitteeDecision")),"Normal manager referral unchanged");
            Console.WriteLine($"Rejection workflow: {passed} focused checks passed. Migration tested only in {database}.");
        }
        finally
        {
            await app.StopAsync();
            if(owned){using var scope=app.Services.CreateScope();var db=scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();if(db.Database.GetDbConnection().Database!=database
                    ||!(database.StartsWith("SourcingTender_RejectionCheck_") || database.StartsWith("SourcingTender_RejectionQA_")))throw new InvalidOperationException("Unsafe cleanup target");await db.Database
                .EnsureDeletedAsync();Console
                .WriteLine("Removed owned isolated test database.");}
        }
    }
    private sealed class ActorHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext db):AuthenticationHandler<AuthenticationSchemeOptions>(options,
        logger,
        encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id=Request.Headers["X-Rejection-Actor"].ToString();if(string.IsNullOrEmpty(id)
                ||!await db.Users
                .AnyAsync(u=>u.Id==id&&u.IsActive))return AuthenticateResult
                .NoResult();
            var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==id select r.Name!).ToListAsync();
            var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,
                id),new(ClaimTypes.Name,
                id)};claims.AddRange(roles.Select(r=>new Claim(ClaimTypes.Role,r)));
            return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name));
        }
    }
}
