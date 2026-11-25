using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Chat;

public sealed record AiChatToolDefinition(string Name, string Description, string ParametersSchemaHint);

public sealed record AiChatToolExecutionResult(
    bool Success,
    string Summary,
    string? TargetUrl = null,
    string? TargetLabel = null,
    string? NavigateUrl = null,
    IReadOnlyList<AiChatDownloadLink>? Downloads = null);

/// <summary>
/// The only things Safi can do are read-only: open a page, look a record up, and build a PDF/Excel file.
/// It never changes workflow data. (Creating accounts from an Excel sheet is a separate Admin import button.)
/// </summary>
public sealed class AiChatWorkspaceActions(AiChatContext context, AiChatExportService exportService, LinkGenerator links)
{
    // Pages that need a record id, keyed by workspace and page action.
    private static readonly Dictionary<(string Workspace, string Page), string> RecordPages = new()
    {
        [(RoleNames.Department, "Details")] = "/Department/Details/{0}",
        [(RoleNames.Department, "Edit")] = "/Department/Edit/{0}",
        [(RoleNames.SourcingOfficer, "Requests")] = "/SourcingOfficer/Requests?id={0}",
        [(RoleNames.SourcingOfficer, "Offers")] = "/SourcingOfficer/Offers?tenderId={0}",
        [(RoleNames.SourcingOfficer, "Create")] = "/SourcingOfficer/Create?purchaseRequestId={0}",
        [(RoleNames.SourcingOfficer, "Comparison")] = "/SourcingOfficer/Comparison?tenderId={0}",
        [(RoleNames.SourcingOfficer, "Reports")] = "/SourcingOfficer/Reports/{0}",
        [(RoleNames.EvaluationCommittee, "Evaluations")] = "/EvaluationCommittee/Evaluations/{0}",
        [(RoleNames.EvaluationCommittee, "Reports")] = "/EvaluationCommittee/Reports/{0}",
        [(RoleNames.SourcingManager, "Submissions")] = "/SourcingManager/Submissions?id={0}",
        [(RoleNames.TopCommittee, "TenderDetails")] = "/TopCommittee/TenderDetails/{0}",
        [(RoleNames.Supplier, "Invitation")] = "/Supplier/Invitation/{0}",
        [(RoleNames.Supplier, "CreateOffer")] = "/Supplier/CreateOffer?invitationId={0}",
        [(RoleNames.Supplier, "Offer")] = "/Supplier/Offer/{0}",
        [(RoleNames.Admin, "SupplierDetails")] = "/Admin/SupplierDetails/{0}",
        [(RoleNames.Admin, "ContactMessageDetails")] = "/Admin/ContactMessageDetails/{0}"
    };

    public static IReadOnlyList<AiChatToolDefinition> GetAuthorizedTools(string workspace)
    {
        var pages = PageNavigation.Menus.TryGetValue(workspace, out var menu)
            ? menu.Select(m => m.Action == "Index" ? m.Title : m.Action).ToList()
            : ["Dashboard"];
        pages.AddRange(RecordPages.Keys.Where(k => k.Workspace == workspace).Select(k => $"{k.Page} (needs recordId)"));
        pages.Add("Profile");

        return
        [
            new("navigate_to_page",
                $"Open a page or record in the user's {RoleNames.DisplayName(workspace)} workspace when they ask to open, go to, or switch to it. Navigation only: you cannot create, edit, approve, score, vote or submit anything for the user.",
                $"{{\"page\":\"{string.Join(" | ", pages)}\",\"recordId\":null}}"),
            new("inspect_record_details",
                "Load the live details of an authorized Tender, Purchase Request or Supplier (items, quotations, compliance, scores, reports) to answer detailed questions about it by ID or title.",
                "{\"recordType\":\"tender|request|supplier\",\"id\":191,\"search\":\"optional title or company\"}"),
            new("export_downloadable_file",
                "Build a downloadable PDF and/or Excel file for a Tender, Purchase Request, the whole workspace summary, or a custom table the user asked for.",
                "{\"scope\":\"tender|request|workspace|custom\",\"tenderId\":null,\"requestId\":null,\"search\":null,\"format\":\"both|pdf|excel\",\"title\":\"string\",\"executiveSummary\":\"text\",\"highlights\":[\"bullet\"],\"columns\":[\"optional col\"],\"rows\":[[\"optional value\"]]}")
        ];
    }

    public async Task<AiChatToolExecutionResult> ExecuteAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        string toolName,
        string argumentsJson,
        string? currentPage,
        int? currentRecordId,
        CancellationToken ct)
    {
        JsonElement args;
        try
        {
            using var parsed = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = parsed.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new(false, "Invalid tool arguments JSON.");
        }

        switch (toolName)
        {
            case "navigate_to_page":
                return Navigate(workspace, args);
            case "inspect_record_details":
                return await exportService.InspectRecordAsync(workspace, principal, user, args, ct);
            case "export_downloadable_file":
                object? workspaceData = null;
                if (string.Equals(AiChatExportService.Str(args, "scope"), "workspace", StringComparison.OrdinalIgnoreCase))
                    workspaceData = (await context.BuildSnapshotAsync(workspace, currentPage, currentRecordId, principal, user, ct)).WorkspaceData;
                return await exportService.GenerateDownloadAsync(workspace, principal, user, args, workspaceData, ct);
            default:
                return new(false, $"Action '{toolName}' is not available. Safi can only open pages, look records up and build PDF/Excel files.");
        }
    }

    internal AiChatToolExecutionResult Navigate(string workspace, JsonElement args)
    {
        var page = Normalize(AiChatExportService.Str(args, "page"));
        var recordId = AiChatExportService.Int(args, "recordId") is > 0 and var id ? id : (int?)null;

        if (page is "profile" or "account" or "myprofile")
            return Go("/Account/Profile", "Account Profile");

        var menu = PageNavigation.Menus.GetValueOrDefault(workspace) ?? [];
        // What the user reads in the sidebar (the title) wins over the internal action name.
        var item = menu.FirstOrDefault(m => Normalize(m.Title) == page) ?? menu.FirstOrDefault(m => Normalize(m.Action) == page);
        var action = item?.Action;

        // A page that needs an id (Details, TenderDetails, ...) is not always in the sidebar menu.
        if (action == null)
        {
            var record = RecordPages.Keys.FirstOrDefault(k => k.Workspace == workspace && Normalize(k.Page) == page);
            action = record.Page;
        }

        if (action == null)
            return new(false, $"'{page}' is not a page in your {RoleNames.DisplayName(workspace)} workspace. Available pages: {string.Join(", ", menu.Select(m => m.Title))}.");

        if (recordId.HasValue && RecordPages.TryGetValue((workspace, action), out var template))
            return Go(string.Format(template, recordId.Value), $"{item?.Title ?? action} #{recordId.Value}");

        if (RecordPages.ContainsKey((workspace, action)) && item == null)
            return new(false, $"The {action} page needs a record id.");

        // Same lookup the sidebar uses (asp-controller / asp-action), so attribute-routed pages like /Reports resolve correctly.
        var path = links.GetPathByAction(action, item?.Controller ?? workspace,
            action == "AiIntelligence" && recordId.HasValue ? new { tenderId = recordId.Value } : null);
        return path == null
            ? new(false, $"The {item?.Title ?? action} page could not be opened from here.")
            : Go(path, item?.Title ?? action);
    }

    private static AiChatToolExecutionResult Go(string url, string label) =>
        new(true, $"Opening {label} ({url}).", url, $"Open {label}", url);

    private static string Normalize(string? text) =>
        (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
}
