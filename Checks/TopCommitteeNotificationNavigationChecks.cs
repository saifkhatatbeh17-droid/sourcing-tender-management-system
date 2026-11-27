using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;

namespace SourcingTenderManagement.Checks;
// Part of the --top-committee-foundation-check self-test (called by TopCommitteeFoundationChecks).

public static class TopCommitteeNotificationNavigationChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passed++; }
        Check(PageNavigation.Menus[RoleNames.TopCommittee].All(x => x.Action != "Notifications"),
            "Top Committee sidebar has no standalone Notifications link.");
        var existingMenus = new Dictionary<string, string>
        {
            [RoleNames.Admin] = "Dashboard,AiIntelligence,AiLog,Users,Departments,SupplierApplications,Suppliers,Index,Index,ContactMessages",
            [RoleNames.Department] = "Dashboard,AiIntelligence,Create,Requests,Clarifications,Index",
            [RoleNames.SourcingOfficer] = "Dashboard,AiIntelligence,Requests,Offers,Clarifications,Reports,Index",
            [RoleNames.EvaluationCommittee] = "Dashboard,AiIntelligence,Evaluations,Reports,Clarifications,Index",
            [RoleNames.SourcingManager] = "Dashboard,AiIntelligence,Submissions,ChosenOffers,Index,Clarifications",
            [RoleNames.Supplier] = "Dashboard,Invitations,Offers,Clarifications,History"
        };
        var engine = services.GetRequiredService<IRazorViewEngine>();
        foreach (var role in RoleNames.Primary)
        {
            var actor = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "navigation-fixture"),
                new Claim(ClaimTypes.Role, role)], "NavigationChecks"));
            var context = TopCommitteePhase2Checks.Context(services, actor, "Dashboard");
            context.RouteData.Values["controller"] = role;
            ((ControllerActionDescriptor)context.ActionDescriptor).ControllerName = role;
            var view = engine.FindView(context, "Components/NotificationBell/Default", false);
            var expected = role == RoleNames.TopCommittee
                ? "/Views/TopCommittee/Components/NotificationBell/Default.cshtml"
                : "/Views/Shared/Components/NotificationBell/Default.cshtml";
            Check(view.Success && view.View.Path == expected, $"Actual MVC component view resolution for {role}.");
            using var writer = new StringWriter();
            NotificationBellViewModel model = role == RoleNames.TopCommittee ? new CommitteeNotificationBellViewModel
            {
                Total = 25, Page = 2, Pages = 3, ReturnUrl = "/TopCommittee/Dashboard?notificationPage=2",
                PreviousUrl = "/TopCommittee/Dashboard?notificationPage=1", NextUrl = "/TopCommittee/Dashboard?notificationPage=3"
            } : new NotificationBellViewModel();
            model.UnreadCount = 12;
            model.Items =
            [
                new Notification { NotificationId = 1, Title = "<script>private</script>",
                    Message = "Synthetic notice", RelativeUrl = "/TopCommittee/TenderDetails/41", IsRead = false },
                new Notification { NotificationId = 2, Title = "Read notice", Message = "Already read",
                    RelativeUrl = "https://external.example/unsafe", IsRead = true }
            ];
            var data = new ViewDataDictionary(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
            var temp = new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>());
            await view.View!.RenderAsync(new ViewContext(context, view.View, data, temp, writer, new HtmlHelperOptions()));
            var html = writer.ToString();
            if (role == RoleNames.TopCommittee)
            {
                Check(html.Contains("href=\"/TopCommittee/Notifications\""), "Bell links to the full notifications page.");
                Check(html.Contains("12 unread") && html.Contains("View details"), "Existing badge and details retained.");
                Check(!html.Contains("<script>private</script>") && html.Contains("&lt;script&gt;"), "Notification encoding retained.");
                Check(!html.Contains("href=\"https://external.example/unsafe\""), "External destinations are not linked.");
                Check(html.Contains("/TopCommittee/MarkNotificationRead") && html.Contains("/TopCommittee/MarkAllNotificationsRead"), "Existing read actions available in bell.");
                Check(html.Contains("__RequestVerificationToken") && html.Contains("name=\"returnUrl\""), "Read forms retain CSRF protection and local return navigation.");
                Check(html.Contains("notificationPage=1") && html.Contains("notificationPage=3") && html.Contains("Page 2 of 3"), "Older and newer notices available within bell.");
                Check(html.Contains("name=\"id\" value=\"1\"") && !html.Contains("name=\"id\" value=\"2\""), "Only unread notices have individual read forms.");
            }
            else
            {
                Check(string.Join(',', PageNavigation.Menus[role].Select(x => x.Action)) == existingMenus[role], $"Other workspace menu unchanged: {role}.");
                Check(!html.Contains("top-committee-notifications"), $"Other role bell unchanged: {role}.");
            }
        }
        Console.WriteLine($"PASS: {passed} Top Committee notification navigation checks; no database writes.");
    }
}
