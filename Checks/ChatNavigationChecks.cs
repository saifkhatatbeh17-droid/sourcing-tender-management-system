using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Checks;

internal static class ChatNavigationChecks
{
    // Every page in every role's menu must open through the real route table, exactly like the sidebar link does.
    public static void Run(IServiceProvider services)
    {
        var links = services.GetRequiredService<LinkGenerator>();
        var actions = new AiChatWorkspaceActions(null!, null!, links);
        var failures = new List<string>();

        foreach (var (role, menu) in PageNavigation.Menus)
        {
            foreach (var item in menu)
            {
                var expected = links.GetPathByAction(item.Action, item.Controller ?? role);
                using var args = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { page = item.Title }));
                var result = actions.Navigate(role, args.RootElement);
                Console.WriteLine($"{role,-20} {item.Title,-22} -> {(result.Success ? result.NavigateUrl : "FAIL " + result.Summary)}");
                if (!result.Success || result.NavigateUrl is null || result.NavigateUrl.Split('?')[0] != expected)
                    failures.Add($"{role} / {item.Title}: got '{result.NavigateUrl}', sidebar link is '{expected}'");
            }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("Chat navigation differs from the sidebar:\n" + string.Join("\n", failures));
        Console.WriteLine("PASS: every menu page opens the same address the sidebar link does.");
    }
}
