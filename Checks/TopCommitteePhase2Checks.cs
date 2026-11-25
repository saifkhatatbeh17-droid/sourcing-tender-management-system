using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Checks;

// Called only by the explicit read-only foundation check. Test models are never saved or registered
// as production services. No website, login, role seed, schema change or database fixture is created.
public static class TopCommitteePhase2Checks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed++;
        }
        var actor = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, "phase2-in-memory-member"),
            new Claim(ClaimTypes.Role, RoleNames.TopCommittee) }, "ViewChecks"));
        var details = FixtureDetails();
        var pendingRow = new CommitteeTenderRow(details.TenderId, details.Title, details.Status, DateTime.UtcNow,
            details.Report!.RecommendedBidId, details.Bids[0].SupplierName, 1, false, 1);
        var fake = new ReadModels(details, pendingRow);
        TopCommitteeController Controller(string action)
        {
            var context = Context(services, actor, action);
            return new TopCommitteeController(fake, services.GetRequiredService<ITopCommitteeClarifications>(),
                services.GetRequiredService<ITopCommitteeClarificationQueries>(), services.GetRequiredService<ITopCommitteeVoting>(), fake) { ControllerContext = new ControllerContext(context) };
        }

        var controllerType = typeof(TopCommitteeController);
        Check(controllerType.GetCustomAttribute<AuthorizeAttribute>()?.Roles == RoleNames.TopCommittee,
            "Committee controller must require the Identity role.");
        Check(controllerType.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore == true,
            "Private committee responses must not be cached.");
        foreach (var name in new[] { "Index", "Dashboard", "Decisions", "TenderDetails" })
            Check(controllerType.GetMethod(name)!.GetCustomAttribute<HttpGetAttribute>() != null, $"{name} must be GET-only.");
        Check(controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .All(m => m.Name is "CreateClarification" or "ResolveClarification" or "FollowUpClarification" or "SubmitVote" or "AssessImportance" or "MarkNotificationRead" or "MarkAllNotificationsRead"), "Only approved Top Committee POSTs may be introduced.");

        var dashboardResult = (ViewResult)await Controller("Dashboard").Dashboard(default);
        Check(dashboardResult.Model is CommitteeDashboard, "Dashboard must use the Phase 1 read model.");
        var listController = Controller("Decisions");
        var listResult = (ViewResult)await listController.Decisions(new CommitteeTenderFilter { Status = "Awarded", Search = "Test & supplier", Page = 2 }, default);
        Check(fake.LastFilter?.Status == "AwaitingCommitteeDecision" && fake.LastFilter.Search == "Test & supplier" && fake.LastFilter.Page == 2,
            "Browser Status cannot widen the pending list; search and paging must be preserved.");
        Check(listResult.Model is CommitteePendingPage && (string?)listController.ViewData["ActivePage"] == "Decisions",
            "Pending model and active navigation must be set.");
        var detailController = Controller("TenderDetails");
        var detailResult = (ViewResult)await detailController.TenderDetails(details.TenderId, null, default);
        Check(((CommitteeTenderPage)detailResult.Model!).SelectedBid?.IsNominated == true, "Details should initially open the nominated bid.");
        var alternative = (ViewResult)await Controller("TenderDetails").TenderDetails(details.TenderId, 12, default);
        Check(((CommitteeTenderPage)alternative.Model!).SelectedBid?.BidId == 12, "Comparison links must select the requested tender-owned bid.");
        Check(await Controller("TenderDetails").TenderDetails(details.TenderId, 999, default) is NotFoundResult,
            "An outside/unknown bid must not be loaded through another tender's details.");
        fake.Error = new CommitteeAccessDeniedException();
        Check(await Controller("Dashboard").Dashboard(default) is ForbidResult, "Service authorization denial must map to forbidden.");
        fake.Error = new CommitteeTenderNotFoundException();
        Check(await Controller("TenderDetails").TenderDetails(999, null, default) is NotFoundResult,
            "Missing/unassigned tenders must map to identical 404 responses.");
        fake.Error = new ValidationException("Invalid page settings");
        var invalid = Controller("Decisions");
        Check(await invalid.Decisions(new CommitteeTenderFilter { Page = 0 }, default) is ViewResult &&
            invalid.Response.StatusCode == 400 && !invalid.ModelState.IsValid, "Invalid filters must render useful validation, not a server error.");
        fake.Error = null;
        var malformed = Controller("Decisions");
        malformed.ModelState.AddModelError("FromDate", "Enter a valid date.");
        await malformed.Decisions(new CommitteeTenderFilter(), default);
        Check(malformed.Response.StatusCode == 400, "Malformed bound dates must not silently load an unfiltered list.");

        var htmlDashboard = await RenderAsync(services, actor, "Dashboard", dashboardResult.Model!);
        var htmlList = await RenderAsync(services, actor, "Decisions", listResult.Model!);
        var htmlDetails = await RenderAsync(services, actor, "TenderDetails", detailResult.Model!);
        Check(htmlDashboard.Contains("Committee progress") && htmlDashboard.Contains("Test member 4"), "Dashboard must show all four members' progress.");
        Check(htmlDashboard.Contains("/TopCommittee/TenderDetails/41"), "Dashboard must link to the protected details route.");
        Check(htmlList.Contains("name=\"Search\"") && htmlList.Contains("Test &amp; supplier") && htmlList.Contains("Page=1"),
            "GET filters and paging must preserve and encode search text.");
        Check(htmlDetails.Contains("Manager nominated") && htmlDetails.Contains("Test manager recommendation") &&
            htmlDetails.Contains("Test committee report"), "Details must expose the nomination, recommendation and unified report.");
        Check(htmlDetails.Contains("215.00 USD") && htmlDetails.Contains("80/100") && htmlDetails.Contains("Financial"),
            "Bid comparison must show actual per-currency prices and technical/financial scores.");
        Check(htmlDetails.Contains("bidId=12#selected-bid") && htmlDetails.Contains("Withdrawn"),
            "Submitted bid history and secure tender-scoped offer links must remain visible.");
        Check(!htmlDetails.Contains("<script>alert(1)</script>") && htmlDetails.Contains("&lt;script&gt;"), "Untrusted database text must be HTML encoded.");
        Check(!htmlDetails.Contains("Approve</button>") && !htmlDetails.Contains("Reject</button>") && !htmlDetails.Contains("AI Analysis"),
            "Phase 2 must not expose voting controls or new AI UI.");
        Check(htmlDetails.Contains("table-responsive") && htmlDetails.Contains("scope=\"col\"") && htmlList.Contains("for=\"tender-search\""),
            "Data tables and filters must retain responsive wrappers and accessible headers/labels.");

        var db = services.GetRequiredService<ApplicationDbContext>();
        var memberId = await db.Users.AsNoTracking().Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))).Select(u => u.Id).FirstAsync();
        var realActor = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, memberId),
            new Claim(ClaimTypes.Role, RoleNames.TopCommittee) }, "ReadOnlyChecks"));
        var actual = services.GetRequiredService<ITopCommitteeQueries>();
        var liveDashboard = await actual.GetDashboardAsync(realActor);
        var liveList = await actual.GetTendersAsync(realActor, new CommitteeTenderFilter { Status = "AwaitingCommitteeDecision" });
        Check(liveList.Items.All(t => t.Status == "AwaitingCommitteeDecision" && !t.HasRejection),
            "Live pending queries must not include ended/rejected tenders.");
        var liveDashboardHtml = await RenderAsync(services, actor, "Dashboard", liveDashboard);
        var liveListHtml = await RenderAsync(services, actor, "Decisions", new CommitteePendingPage(liveList, new CommitteeTenderFilter()));
        Check(liveDashboard.AssignedTenders > 0 || liveDashboardHtml.Contains("No tenders assigned yet"),
            "A real empty SQL dataset must produce the assignment empty state.");
        Check(liveList.TotalCount > 0 || liveListHtml.Contains("No pending tenders found"), "The real pending empty state must render.");
        var noData = details with { Report = null, ResponsibleManager = null, Bids = [], Documents = [], Votes = [] };
        var noDataHtml = await RenderAsync(services, actor, "TenderDetails", new CommitteeTenderPage(noData, null));
        Check(noDataHtml.Contains("No submitted bids") && noDataHtml.Contains("No unified evaluation report"),
            "Missing report and offers must produce useful states without invented data.");
        var completedHtml = await RenderAsync(services, actor, "TenderDetails", new CommitteeTenderPage(details with {
            Status = "Awarded", AwardedBidId = 11, AwardedAt = DateTime.UtcNow, AwardNotes = "Test award" }, details.Bids[0]));
        Check(completedHtml.Contains("Recorded decision") && completedHtml.Contains("Winning bid"), "Completed details must stay readable.");
        var errorHtml = await RenderAsync(services, actor, "Decisions", ((ViewResult)await invalid.Decisions(new(), default)).Model!,
            invalid.ModelState);
        Check(errorHtml.Contains("Correct the filter values"), "Filter errors must render an actionable recovery.");

        var output = Environment.GetEnvironmentVariable("TOP_COMMITTEE_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var root = Path.GetFullPath(services.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
            var preview = Path.GetFullPath(output);
            if (!Path.IsPathRooted(output) || !Path.GetRelativePath(root, preview).StartsWith("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Preview output must be an absolute directory outside the repository.");
            Directory.CreateDirectory(preview);
            foreach (var (name, html) in new[] { ("dashboard.html", htmlDashboard), ("pending.html", htmlList),
                ("details.html", htmlDetails), ("empty-dashboard.html", liveDashboardHtml), ("empty-pending.html", liveListHtml),
                ("missing-data.html", noDataHtml), ("completed.html", completedHtml), ("invalid-filter.html", errorHtml) })
                await File.WriteAllTextAsync(Path.Combine(preview, name), html);
            var webRoot = services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
            foreach (var asset in new[] { "css/site.css", "lib/bootstrap/dist/css/bootstrap.min.css",
                "lib/bootstrap/dist/js/bootstrap.bundle.min.js", "lib/jquery/dist/jquery.min.js" })
            {
                var target = Path.Combine(preview, asset);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(webRoot, asset), target, overwrite: true);
            }
            Console.WriteLine($"Preview HTML written outside the repository: {preview}. Populated examples are in-memory test fixtures, never database data.");
        }
        Check(!db.ChangeTracker.HasChanges(), "Phase 2 checks must not mutate tracked data.");
        Console.WriteLine($"PASS: {passed} Top Committee Phase 2 controller, Razor, access-boundary and read-only SQL checks.");
    }

    internal static ActionContext Context(IServiceProvider services, ClaimsPrincipal actor, string action)
    {
        var http = new DefaultHttpContext { RequestServices = services, User = actor };
        http.Request.Scheme = "http"; http.Request.Host = new HostString("localhost");
        var route = new RouteData();
        route.Values["controller"] = "TopCommittee"; route.Values["action"] = action;
        route.Routers.Add(new CheckRouter());
        return new ActionContext(http, route, new ControllerActionDescriptor { ControllerName = "TopCommittee", ActionName = action });
    }

    internal static async Task<string> RenderAsync(IServiceProvider services, ClaimsPrincipal actor, string name,
        object model, ModelStateDictionary? modelState = null)
    {
        var context = Context(services, actor, name);
        var engine = services.GetRequiredService<IRazorViewEngine>();
        var result = engine.GetView(null, $"/Views/TopCommittee/{name}.cshtml", isMainPage: true);
        if (!result.Success) throw new InvalidOperationException(string.Join(", ", result.SearchedLocations));
        using var writer = new StringWriter();
        var viewData = new ViewDataDictionary(services.GetRequiredService<IModelMetadataProvider>(), modelState ?? new()) { Model = model };
        var tempData = new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>());
        await result.View.RenderAsync(new ViewContext(context, result.View, viewData, tempData, writer, new HtmlHelperOptions()));
        return writer.ToString();
    }

    // Razor's link helpers need a router in this non-HTTP check. Production retains the existing MVC route.
    private sealed class CheckRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;
        public VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            var values = context.Values;
            var path = $"/{values["controller"]}/{values["action"]}";
            if (values.TryGetValue("id", out var id) && id != null) path += "/" + Uri.EscapeDataString(id.ToString()!);
            var query = values.Where(p => p.Key is not ("controller" or "action" or "id" or "area") && p.Value != null)
                .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!.ToString()!));
            var suffix = string.Join("&", query);
            return new VirtualPathData(this, path + (suffix.Length == 0 ? "" : "?" + suffix));
        }
    }

    private sealed class ReadModels(CommitteeTenderDetails details, CommitteeTenderRow row) : ITopCommitteeQueries, ITopCommitteeVotingReadiness
    {
        public Exception? Error { get; set; }
        public Task<bool> IsDatabaseReadyAsync(ClaimsPrincipal member, int tenderId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public CommitteeTenderFilter? LastFilter { get; private set; }
        public Task<CommitteeDashboard> GetDashboardAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
        {
            if (Error != null) throw Error;
            return Task.FromResult(new CommitteeDashboard(1, 1, 0, 0, 1, new[] { row }, [], details.Members.Select(m =>
                new CommitteeMemberProgress(details.TenderId, m.UserId, m.FullName, m.Position, true, true, m.Position == 1 ? "Approved" : null)).ToList()));
        }
        public Task<CommitteePage<CommitteeTenderRow>> GetTendersAsync(ClaimsPrincipal actor, CommitteeTenderFilter filter, CancellationToken cancellationToken = default)
        {
            if (Error != null) throw Error;
            LastFilter = filter;
            return Task.FromResult(new CommitteePage<CommitteeTenderRow>(new[] { row }, 21, filter.Page, filter.PageSize));
        }
        public Task<CommitteeTenderDetails> GetTenderDetailsAsync(ClaimsPrincipal actor, int tenderId, CancellationToken cancellationToken = default)
        {
            if (Error != null) throw Error;
            return Task.FromResult(details);
        }
    }

    private static CommitteeTenderDetails FixtureDetails()
    {
        var submitted = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var bid = new CommitteeBid(11, 5, "Test nominated supplier", "Compliant", "Compliant", "Test check", "USD",
            200, 10, 5, 215, 7, "Test payment terms", new DateOnly(2027, 1, 1), "Test technical proposal", submitted, true,
            new[] { new CommitteeBidItem(1, "Test computer", 2, 100, "Test specifications") },
            new[] { new CommitteeDocument(1, "test-offer.pdf", "Technical", 1024) },
            new[] { new CommitteeEvaluation("Test evaluator", "Approved", "Test assessment", submitted, submitted,
                new[] { new CommitteeScore("Compatibility", "Requirements", 40, 80, "Test technical result"),
                    new CommitteeScore("Cost", "Financial", 60, 90, "Test financial result") }) });
        return new CommitteeTenderDetails(41, "Test tender <script>alert(1)</script>", "Test tender description", "Test terms",
            "AwaitingCommitteeDecision", submitted.AddDays(-2), submitted.AddDays(2), null, null, null,
            new CommitteePurchaseRequest(1, "Test request", "Test need", "Test IT", "Normal", new DateOnly(2026, 12, 1),
                new[] { new CommitteeRequestItem(1, "Test computer", 2, "Each", "Test specifications") }),
            new CommitteeReport(1, "ManagerApproved", 11, "Test committee report", "Test officer summary", "Test manager recommendation", submitted),
            new CommitteeManager("test-manager", "Test manager", true, true, submitted),
            Enumerable.Range(1, 4).Select(p => new CommitteeMember(p, $"test-{p}", $"Test member {p}", p, true, true)).ToList(),
            new[] { new CommitteeVote(1, "test-1", 1, "Test member 1", "Approved", "Test approval", submitted) },
            new[] { bid, bid with { BidId = 12, SupplierId = 6, SupplierName = "Test alternative supplier", IsNominated = false, Status = "Withdrawn", Currency = "JOD" } },
            [], 1, [], new CommitteeReadiness(true, true, true, false, false, false, false, "Voting is paused until all relevant clarifications are resolved."));
    }
}
