using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;

namespace SourcingTenderManagement.Services.Chat;

public sealed record AiChatWorkspaceSnapshot(
    object UserProfile,
    object WorkspaceData,
    object? CurrentRecord,
    string? CurrentRecordUrl,
    string? CurrentRecordLabel);

/// <summary>
/// What Safi can see before it answers: a short, role-scoped overview of the user's workspace plus the record they
/// are looking at. Anything deeper is fetched on demand with <c>inspect_record_details</c>, which uses the same
/// role-scoped loaders, so the chat can never read more than the user's own pages show.
/// </summary>
public sealed class AiChatContext(
    ApplicationDbContext db,
    ITopCommitteeQueries topCommittee,
    AiChatExportService records)
{
    private const int Recent = 8;

    public async Task<AiChatWorkspaceSnapshot> BuildSnapshotAsync(
        string workspace, string? pageAction, int? recordId, ClaimsPrincipal principal, ApplicationUser user, CancellationToken ct)
    {
        var roles = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == user.Id && r.Name != null
            orderby r.Name
            select r.Name!).ToListAsync(ct);

        var department = user.DepartmentId.HasValue
            ? await db.Departments.AsNoTracking().Where(d => d.DepartmentId == user.DepartmentId.Value).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;

        var profile = new
        {
            user.FullName,
            user.Email,
            ActiveWorkspace = RoleNames.DisplayName(workspace),
            WorkspaceCode = workspace,
            AssignedRoles = roles.Select(RoleNames.DisplayName).ToArray(),
            Department = department
        };

        AiChatRecordData? current = null;
        var recordType = recordId is > 0 ? AiChatPage.Record(workspace, pageAction) : null;
        if (recordType.HasValue)
            current = await ReadCurrentRecordAsync(workspace, recordType.Value, recordId!.Value, principal, user, ct);

        return new(profile, await BuildWorkspaceDataAsync(workspace, principal, user, ct), current?.Data, current?.Url, current?.Label);
    }

    // The page's own id (report, invitation, offer...) is mapped to the tender/request/supplier it belongs to,
    // then loaded through the same authorized loaders the chat tools use.
    private async Task<AiChatRecordData?> ReadCurrentRecordAsync(
        string workspace, AiChatRecord record, int id, ClaimsPrincipal principal, ApplicationUser user, CancellationToken ct)
    {
        Task<AiChatRecordData?> Load(string type, int? recordId) =>
            recordId is > 0 ? records.LoadRecordAsync(workspace, principal, user, type, recordId, null, ct) : Task.FromResult<AiChatRecordData?>(null);

        switch (record)
        {
            case AiChatRecord.DepartmentRequest or AiChatRecord.OfficerRequest:
                return await Load("request", id);
            case AiChatRecord.OfficerTender or AiChatRecord.EvaluationTender or AiChatRecord.TopTender:
                return await Load("tender", id);
            case AiChatRecord.SupplierApplication:
                return await Load("supplier", id);
            case AiChatRecord.ManagerReport:
                return await Load("tender", await db.EvaluationReports.AsNoTracking().InManagerReview()
                    .Where(r => r.EvaluationReportId == id).Select(r => (int?)r.TenderId).FirstOrDefaultAsync(ct));
            case AiChatRecord.SupplierInvitation:
                return await Load("tender", await db.Invitations.AsNoTracking()
                    .Where(i => i.InvitationId == id && i.SupplierProfile.UserId == user.Id).Select(i => (int?)i.TenderId).FirstOrDefaultAsync(ct));
            case AiChatRecord.SupplierOffer:
                return await Load("tender", await db.Bids.AsNoTracking()
                    .Where(b => b.BidId == id && b.Invitation.SupplierProfile.UserId == user.Id).Select(b => (int?)b.TenderId).FirstOrDefaultAsync(ct));
            case AiChatRecord.ContactMessage when workspace == RoleNames.Admin:
                var message = await db.ContactMessages.AsNoTracking().Where(m => m.ContactMessageId == id)
                    .Select(m => new { m.ContactMessageId, m.FullName, m.Email, m.Subject, m.Message, m.Status, m.SubmittedAt })
                    .SingleOrDefaultAsync(ct);
                return message == null ? null : new("Contact Message", id, message.Subject, message,
                    $"/Admin/ContactMessageDetails/{id}", $"Open Message #{id}");
            default:
                return null;
        }
    }

    private async Task<object> BuildWorkspaceDataAsync(string workspace, ClaimsPrincipal principal, ApplicationUser user, CancellationToken ct)
    {
        switch (workspace)
        {
            case RoleNames.Department:
            {
                if (!user.DepartmentId.HasValue)
                    return new { Error = "No department assigned to this account." };
                var requests = db.PurchaseRequests.AsNoTracking().InDepartment(user.DepartmentId);
                return new
                {
                    StatusCounts = await requests.GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct),
                    RecentRequests = await requests.OrderByDescending(r => r.CreatedAt).Take(Recent)
                        .Select(r => new
                        {
                            r.PurchaseRequestId, r.Title, r.Status, r.Priority,
                            TargetDate = r.TargetDate.ToString("yyyy-MM-dd"),
                            Items = r.PurchaseRequestItems.Count,
                            LinkedTenders = r.Tenders.Select(t => new { t.TenderId, t.Status }).ToList()
                        }).ToListAsync(ct)
                };
            }

            case RoleNames.SourcingOfficer:
                return new
                {
                    ReviewableRequests = await db.PurchaseRequests.AsNoTracking().ReviewableBy(user.Id)
                        .OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).Take(Recent)
                        .Select(r => new
                        {
                            r.PurchaseRequestId, r.Title, Department = r.Department.Name, r.Status, r.Priority,
                            HasTender = r.Tenders.Any()
                        }).ToListAsync(ct),
                    Tenders = await db.Tenders.AsNoTracking().Where(t => t.CreatedByUserId == user.Id)
                        .OrderByDescending(t => t.TenderId).Take(Recent)
                        .Select(t => new
                        {
                            t.TenderId, t.Title, t.Status, t.ClosesAt,
                            Round = 1 + t.OfferExtensions.Count,
                            Invited = t.Invitations.Count,
                            SubmittedOffers = t.Invitations.SelectMany(i => i.Bids).Count(b => b.SubmittedAt != null && b.Status != "Withdrawn"),
                            ReportStatus = t.EvaluationReport == null ? null : t.EvaluationReport.Status
                        }).ToListAsync(ct)
                };

            case RoleNames.EvaluationCommittee:
                return new
                {
                    Tenders = await db.Tenders.AsNoTracking().InEvaluationWorkspace(user.Id)
                        .OrderByDescending(t => t.Status == "UnderEvaluation").ThenByDescending(t => t.TenderId).Take(Recent)
                        .Select(t => new
                        {
                            t.TenderId, t.Title, t.Status,
                            ReportStatus = t.EvaluationReport == null ? null : t.EvaluationReport.Status
                        }).ToListAsync(ct)
                };

            case RoleNames.SourcingManager:
                return new
                {
                    SubmissionsInManagerQueue = await db.EvaluationReports.AsNoTracking().InManagerReview()
                        .OrderByDescending(r => r.SubmittedAt).Take(12)
                        .Select(r => new
                        {
                            r.EvaluationReportId, r.TenderId, TenderTitle = r.Tender.Title, ReportStatus = r.Status,
                            r.RecommendedBidId, r.SubmittedAt
                        }).ToListAsync(ct),
                    WithCeoAndChiefs = await db.Tenders.AsNoTracking().Where(t => t.Status == "AwaitingCommitteeDecision")
                        .OrderByDescending(t => t.TenderId).Take(Recent).Select(t => new { t.TenderId, t.Title, t.Status }).ToListAsync(ct),
                    RecentlyAwarded = await db.Tenders.AsNoTracking().Where(t => t.Status == "Awarded")
                        .OrderByDescending(t => t.AwardedAt).Take(6)
                        .Select(t => new
                        {
                            t.TenderId, t.Title, t.AwardedAt,
                            Winner = t.Bid == null ? null : t.Bid.Invitation.SupplierProfile.CompanyName
                        }).ToListAsync(ct)
                };

            case RoleNames.TopCommittee:
            {
                var dashboard = await topCommittee.GetDashboardAsync(principal, ct);
                return new
                {
                    Summary = new
                    {
                        dashboard.AssignedTenders, dashboard.AwaitingDecision, dashboard.AwardedTenders,
                        dashboard.RejectedTenders, dashboard.OpenClarifications
                    },
                    PendingTenders = dashboard.PendingTenders,
                    RecentDecisions = dashboard.RecentDecisions
                };
            }

            case RoleNames.Supplier:
            {
                var profile = await db.SupplierProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(s => s.UserId == user.Id && s.Status == "Approved", ct);
                if (profile == null)
                    return new { Error = "Supplier profile is not approved." };
                return new
                {
                    Company = new { profile.SupplierProfileId, profile.CompanyName, profile.Category, profile.Status },
                    ConfidentialityRule = "Competitor identities, competitor bids, and internal committee marks are strictly hidden from Supplier accounts.",
                    Invitations = await db.Invitations.AsNoTracking().Where(i => i.SupplierProfileId == profile.SupplierProfileId)
                        .OrderByDescending(i => i.SentAt).Take(Recent)
                        .Select(i => new
                        {
                            i.InvitationId, i.TenderId, TenderTitle = i.Tender.Title, InvitationStatus = i.Status,
                            TenderStatus = i.Tender.Status, i.Tender.ClosesAt,
                            MyOffer = i.Bids.OrderByDescending(b => b.BidId)
                                .Select(b => new { b.BidId, b.Status, b.ComplianceStatus }).FirstOrDefault()
                        }).ToListAsync(ct)
                };
            }

            case RoleNames.Admin:
                return new
                {
                    Counts = new
                    {
                        Users = await db.Users.AsNoTracking().CountAsync(ct),
                        Departments = await db.Departments.AsNoTracking().CountAsync(ct),
                        Suppliers = await db.SupplierProfiles.AsNoTracking().CountAsync(ct),
                        PendingSuppliers = await db.SupplierProfiles.AsNoTracking().CountAsync(s => s.Status == "Pending", ct),
                        PurchaseRequests = await db.PurchaseRequests.AsNoTracking().CountAsync(ct),
                        Tenders = await db.Tenders.AsNoTracking().CountAsync(ct),
                        UnreadMessages = await db.ContactMessages.AsNoTracking().CountAsync(m => m.Status == ContactMessage.Unread, ct)
                    },
                    Departments = await db.Departments.AsNoTracking().OrderBy(d => d.DepartmentId)
                        .Select(d => new { d.DepartmentId, d.Name, d.IsActive }).ToListAsync(ct),
                    Suppliers = await db.SupplierProfiles.AsNoTracking()
                        .OrderByDescending(s => s.Status == "Pending" || s.Status == "ChangesRequested").ThenByDescending(s => s.SupplierProfileId)
                        .Take(12).Select(s => new { s.SupplierProfileId, s.CompanyName, s.Category, s.Status }).ToListAsync(ct),
                    RecentContactMessages = await db.ContactMessages.AsNoTracking()
                        .OrderByDescending(m => m.Status == ContactMessage.Unread).ThenByDescending(m => m.SubmittedAt).Take(5)
                        .Select(m => new { m.ContactMessageId, m.FullName, m.Subject, m.Status, m.SubmittedAt }).ToListAsync(ct),
                    RecentTenders = await db.Tenders.AsNoTracking().OrderByDescending(t => t.TenderId).Take(6)
                        .Select(t => new { t.TenderId, t.Title, t.Category, t.Status, t.ClosesAt }).ToListAsync(ct)
                };

            default:
                return new { };
        }
    }
}

internal enum AiChatRecord
{
    DepartmentRequest,
    OfficerRequest,
    OfficerTender,
    EvaluationTender,
    ManagerReport,
    TopTender,
    SupplierInvitation,
    SupplierOffer,
    SupplierApplication,
    ContactMessage
}

public static class AiChatPage
{
    internal static AiChatRecord? Record(string workspace, string? action) => (workspace, action) switch
    {
        (RoleNames.Department, "Details" or "Edit") => AiChatRecord.DepartmentRequest,
        (RoleNames.SourcingOfficer, "Requests" or "Details") => AiChatRecord.OfficerRequest,
        (RoleNames.SourcingOfficer, "Offers" or "Tenders" or "Comparison" or "Evaluations") => AiChatRecord.OfficerTender,
        (RoleNames.EvaluationCommittee, "Evaluations" or "Reports") => AiChatRecord.EvaluationTender,
        (RoleNames.SourcingManager, "Submissions") => AiChatRecord.ManagerReport,
        (RoleNames.TopCommittee, "TenderDetails") => AiChatRecord.TopTender,
        (RoleNames.Supplier, "Invitation" or "CreateOffer") => AiChatRecord.SupplierInvitation,
        (RoleNames.Supplier, "Offer") => AiChatRecord.SupplierOffer,
        (RoleNames.Admin, "SupplierDetails") => AiChatRecord.SupplierApplication,
        (RoleNames.Admin, "ContactMessageDetails") => AiChatRecord.ContactMessage,
        _ => null
    };

    public static string Guidance(string workspace) => workspace switch
    {
        RoleNames.Department =>
            "Department workspace: purchase requests (title, reason, priority, target date, line items), request history and progress, returned requests that need edits, clarifications. Cannot view supplier bids, pricing, or committee scores.",
        RoleNames.SourcingOfficer =>
            "Sourcing Officer workspace: review incoming purchase requests, publish tenders and invite approved suppliers, check offer compliance (normal committee handoff needs 3 distinct compliant suppliers, or round 4 with a written reason), and review committee evaluation reports.",
        RoleNames.EvaluationCommittee =>
            "Evaluation Committee workspace: 4 seats evaluate tenders in UnderEvaluation. Members confirm a financial weight (technical = 100 - financial), then score every compliant offer; the highest average wins and ties need all 4 tie votes. Position 1 (Department Manager) submits the combined report.",
        RoleNames.SourcingManager =>
            "Sourcing Manager workspace: review officer-submitted evaluation reports (approve, return for review, reject), forward approved recommendations to CEO & Chiefs with a referral reason, and see Chosen Offers.",
        RoleNames.TopCommittee =>
            "CEO & Chiefs workspace: 4 executive seats vote on referred tenders on the Decisions page. Four approvals award the tender; a rejection returns it to Sourcing Manager review.",
        RoleNames.Supplier =>
            "Supplier workspace: invitations sent to your company, offers (quotations) in JOD with line prices, delivery days, payment terms, 16% tax on the item subtotal and additional costs. Strictly confidential: never competitor names, competitor bids, or committee evaluations.",
        RoleNames.Admin =>
            "Administrator workspace: full cross-role visibility. Users, departments, supplier applications and statuses, contact messages. Admins can create employee accounts from an Excel sheet with the import button in this chat panel.",
        _ => ""
    };

    public static string[] StarterPrompts(string workspace, bool hasRecord) => workspace switch
    {
        RoleNames.Department =>
        [
            hasRecord ? "Summarize this request and its current status" : "Summarize my department's requests",
            "Which of my requests need changes?",
            "Take me to Requests history"
        ],
        RoleNames.SourcingOfficer =>
        [
            hasRecord ? "Summarize this tender and export a downloadable PDF & Excel" : "What requests and tenders need my attention?",
            "Create a downloadable summary of my active tenders",
            "Take me to Tender workspace"
        ],
        RoleNames.EvaluationCommittee =>
        [
            hasRecord ? "Compare the eligible offers and scores on this tender" : "Summarize my evaluation queue",
            "How do weights and scoring work here?",
            "Take me to Committee report"
        ],
        RoleNames.SourcingManager =>
        [
            hasRecord ? "Summarize this submission and winning offer" : "Which committee submissions need my decision?",
            "Which tenders are with CEO & Chiefs?",
            "Take me to Chosen Offers"
        ],
        RoleNames.TopCommittee =>
        [
            hasRecord ? "Summarize this tender referral and voting readiness" : "Which referred tenders are waiting for my vote?",
            "Explain how voting and awarding work here",
            "Take me to Decisions"
        ],
        RoleNames.Supplier =>
        [
            hasRecord ? "Summarize this invitation or offer and deadline" : "Show my open tender invitations and offers",
            "How is the tax on my offer calculated?",
            "Take me to My Offers"
        ],
        RoleNames.Admin =>
        [
            "Give me a live overview of Tiryaq across all roles",
            "List active departments and approved suppliers",
            "Take me to Supplier Applications"
        ],
        _ => ["What can you help me do here?"]
    };

    public static string[] CharacterGreetings(string workspace, string? action, bool hasRecord) => workspace switch
    {
        RoleNames.Department =>
        [
            hasRecord ? "I'm Safi! Want me to check this request's progress?" : "Hey! I'm Safi — ask me about your department's requests.",
            "Need to jump to Requests history or Clarifications? Just say the word."
        ],
        RoleNames.SourcingOfficer =>
        [
            hasRecord ? "Safi here! Want a summary of this record, or to open another page?" : "Safi on duty! Ask me about pending requests and tenders, or jump pages.",
            "Tell me 'take me to Tender workspace' or 'check incoming requests' anytime."
        ],
        RoleNames.EvaluationCommittee =>
        [
            hasRecord
                ? "I'm Safi! Need a breakdown of these offers or the weight lock status?"
                : (action == "Reports"
                    ? "Reviewing the committee report? Ask me to summarize rankings or jump to Evaluations."
                    : "Hey! I'm Safi — ready to check your evaluation queue or switch pages."),
            "Say 'take me to Committee report' or ask how your committee seat works."
        ],
        RoleNames.SourcingManager =>
        [
            hasRecord ? "Safi here! Want a crisp brief on this winning offer before you decide?" : "Hi! I'm Safi — ask me which submissions await your decision.",
            "Ask me for a summary or to open Chosen Offers anytime."
        ],
        RoleNames.TopCommittee =>
        [
            hasRecord ? "Good day! Safi here — want me to check voting readiness on this tender?" : "Welcome! I'm Safi — ask me which referred tenders need your vote.",
            "Tell me 'take me to Decisions' or 'open Tender History' to jump straight there."
        ],
        RoleNames.Supplier =>
        [
            hasRecord ? "Hi! I'm Safi — need help understanding this invitation or offer?" : "Welcome! I'm Safi — ask me about your open invitations, deadlines, or offers.",
            "Need to jump to Invitations or My Offers? Just ask and I'll take you there."
        ],
        RoleNames.Admin =>
        [
            "All lines clear! I'm Safi — ask for a live Tiryaq pulse or jump to any Admin page.",
            "Need to check supplier applications or unread messages? Or import accounts from an Excel sheet with the import button in this chat."
        ],
        _ => ["Hi! I'm Safi, your Tiryaq co-pilot. Ask me anything about your workspace."]
    };
}

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
