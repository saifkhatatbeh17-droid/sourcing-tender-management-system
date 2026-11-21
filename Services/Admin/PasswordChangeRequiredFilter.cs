using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SourcingTenderManagement.Services.Admin;

/// <summary>
/// Accounts created with a temporary password (spreadsheet import) carry
/// <see cref="EmployeeAccounts.PasswordChangeRequiredClaim"/>. Until the person changes the password,
/// every page sends them to Account security; background requests (Safi, notifications) get 403.
/// </summary>
public sealed class PasswordChangeRequiredFilter : IAsyncActionFilter
{
    // Account pages that stay reachable so the person can change the password or leave.
    private static readonly HashSet<string> AllowedAccountActions =
        new(StringComparer.OrdinalIgnoreCase) { "Profile", "ChangePassword", "Logout", "AccessDenied", "Login" };

    public static bool IsAllowed(string? controller, string? action) =>
        string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase) && action != null && AllowedAccountActions.Contains(action);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true || !user.HasClaim(EmployeeAccounts.PasswordChangeRequiredClaim, "true"))
        {
            await next();
            return;
        }

        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        if (IsAllowed(descriptor?.ControllerName, descriptor?.ActionName))
        {
            await next();
            return;
        }

        var request = context.HttpContext.Request;
        var wantsPage = HttpMethods.IsGet(request.Method) &&
                        request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);
        context.Result = wantsPage
            ? new RedirectToActionResult("Profile", "Account", null, "profile-security")
            : new ObjectResult(new { error = "Change your temporary password before continuing." }) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
