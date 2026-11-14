using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Checks;

public static class TopCommitteeNotificationNavigationChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passed++; }
        Check(PageNavigation.Menus[RoleNames.TopCommittee].Count(x => x.Action == "Notifications") == 1,
            "Top Committee sidebar must contain exactly one Notifications link.");
        var existingMenus = new Dictionary<string, string>
        {
            [RoleNames.Admin] = "Dashboard,Users,Departments,SupplierApplications,Suppliers",
            [RoleNames.Department] = "Dashboard,Create,Requests,Clarifications",
            [RoleNames.SourcingOfficer] = "Dashboard,Requests,Tenders,Invitations,Offers,Compliance,Comparison,Evaluations,Clarifications,Reports",
            [RoleNames.EvaluationCommittee] = "Dashboard,Evaluations,Reports,Clarifications",
            [RoleNames.SourcingManager] = "Dashboard,Submissions,ChosenOffers,Reports,Clarifications",
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
            var model = new NotificationBellViewModel
            {
                UnreadCount = 12,
                Items = [new Notification { NotificationId = 1, Title = "<script>private</script>",
                    Message = "Synthetic notice", RelativeUrl = "/TopCommittee/TenderDetails/41", IsRead = false }]
            };
            var data = new ViewDataDictionary(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
            var temp = new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>());
            await view.View!.RenderAsync(new ViewContext(context, view.View, data, temp, writer, new HtmlHelperOptions()));
            var html = writer.ToString();
            Check(html.Contains("View all notifications") == (role == RoleNames.TopCommittee), $"Workspace-only View all link for {role}.");
            if (role == RoleNames.TopCommittee)
            {
                Check(html.Contains("href=\"/TopCommittee/Notifications\""), "Full-list link target.");
                Check(html.Contains("12 unread") && html.Contains("View details"), "Existing badge and details retained.");
                Check(!html.Contains("<script>private</script>") && html.Contains("&lt;script&gt;"), "Notification encoding retained.");
                Check(!html.Contains("method=\"post\""), "Navigation does not introduce read-state mutation.");
            }
            else Check(string.Join(',', PageNavigation.Menus[role].Select(x => x.Action)) == existingMenus[role], $"Other workspace menu unchanged: {role}.");
        }
        Console.WriteLine($"PASS: {passed} Top Committee notification navigation checks; no database writes.");
    }
}
