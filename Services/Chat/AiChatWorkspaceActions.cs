using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Services.Reports;

namespace SourcingTenderManagement.Services.Chat;

public sealed record AiChatToolDefinition(string Name, string Description, string ParametersSchemaHint);

public sealed record AiChatToolExecutionResult(
    bool Success,
    bool Mutated,
    string Summary,
    string? TargetUrl = null,
    string? TargetLabel = null,
    string? NavigateUrl = null,
    IReadOnlyList<AiChatDownloadLink>? Downloads = null);

public sealed class AiChatWorkspaceActions(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ITopCommitteeVoting topCommitteeVoting,
    AiChatExportService exportService)
{
    public static IReadOnlyList<AiChatToolDefinition> GetAuthorizedTools(string workspace)
    {
        var allowedPages = workspace switch
        {
            RoleNames.Department => "Dashboard | AiIntelligence | Create (New request) | Requests (Requests history) | Details (needs recordId) | Edit (needs recordId) | Clarifications | Profile",
            RoleNames.SourcingOfficer => "Dashboard | AiIntelligence | Requests (Request Review, optional recordId) | Offers (Tender workspace, optional recordId=tenderId) | Create (New tender, optional recordId=requestId) | Comparison (optional recordId=tenderId) | Reports (Evaluation reports, optional recordId) | Clarifications | Profile",
            RoleNames.EvaluationCommittee => "Dashboard | AiIntelligence | Evaluations (optional recordId=tenderId) | Reports (Combined Reports, optional recordId=tenderId) | Clarifications | Profile",
            RoleNames.SourcingManager => "Dashboard | AiIntelligence | Submissions (Officer Submissions, optional recordId=reportId) | ChosenOffers (Chosen Offers) | Reports | Clarifications | Profile",
            RoleNames.TopCommittee => "Dashboard | AiIntelligence | Decisions | TenderDetails (needs recordId=tenderId) | History (Tender History) | Reports | Clarifications | Profile",
            RoleNames.Supplier => "Dashboard | Invitations | Invitation (needs recordId) | CreateOffer (needs recordId=invitationId) | Offers (My Offers) | Offer (needs recordId=bidId) | History | Clarifications | Profile",
            RoleNames.Admin => "Dashboard | AiIntelligence | AiLog | Users | Departments | SupplierApplications | Suppliers | SupplierDetails (needs recordId) | SupplierActivity | ContactMessages | ContactMessageDetails (needs recordId) | Profile",
            _ => "Dashboard | Profile"
        };

        var list = new List<AiChatToolDefinition>
        {
            new(
                "navigate_to_page",
                $"Transfer/navigate the user's browser directly to an authorized page or record inside their {RoleNames.DisplayName(workspace)} workspace when they ask to open, go to, switch to, or transfer to a page.",
                $"{{\"page\":\"{allowedPages}\",\"recordId\":null}}"),
            new(
                "inspect_record_details",
                "Load complete live details of an authorized Tender, Purchase Request, or Supplier (items, specifications, quotations, compliance, scores, reports) so you can summarize or answer deep questions about any record by ID or title.",
                "{\"recordType\":\"tender|request|supplier\",\"id\":191,\"search\":\"optional title or company\"}"),
            new(
                "export_downloadable_file",
                "Generate downloadable PDF (.pdf) and/or Excel (.xlsx) files for a specific Tender, Purchase Request, full Workspace summary, or any custom summary/note/comparison requested by the user (including follow-ups like 'can I download it' or 'export this').",
                "{\"scope\":\"tender|request|workspace|custom\",\"tenderId\":null,\"requestId\":null,\"search\":null,\"format\":\"both|pdf|excel\",\"title\":\"string\",\"executiveSummary\":\"detailed summary text\",\"highlights\":[\"bullet 1\"],\"columns\":[\"optional col 1\"],\"rows\":[[\"optional val 1\"]]}")
        };

        switch (workspace)
        {
            case RoleNames.Department:
                list.Add(new(
                    "department_create_request",
                    "Create and submit a new Purchase Request for the user's department.",
                    "{\"title\":\"string\",\"reason\":\"string\",\"priority\":\"Low|Normal|High|Urgent\",\"targetDate\":\"YYYY-MM-DD (optional)\",\"items\":[{\"itemName\":\"string\",\"quantity\":100,\"unit\":\"kg|vials|pcs|kits\",\"specifications\":\"string\"}]}"));
                list.Add(new(
                    "department_update_request",
                    "Edit an existing Returned or unreviewed Submitted Purchase Request in the user's department (and automatically resubmit if Returned).",
                    "{\"requestId\":1,\"title\":\"optional string\",\"reason\":\"optional string\",\"priority\":\"optional Low|Normal|High|Urgent\",\"targetDate\":\"optional YYYY-MM-DD\",\"items\":[{\"itemName\":\"string\",\"quantity\":100,\"unit\":\"string\",\"specifications\":\"string\"}]}"));
                break;

            case RoleNames.SourcingOfficer:
                list.Add(new(
                    "officer_review_request",
                    "Start review, Approve, Return, or Reject an incoming Purchase Request.",
                    "{\"requestId\":1,\"decision\":\"StartReview|Approved|Returned|Rejected\",\"notes\":\"required when Returned or Rejected\"}"));
                list.Add(new(
                    "officer_create_tender",
                    "Publish a new Tender from an eligible Purchase Request and invite approved suppliers.",
                    "{\"requestId\":1,\"title\":\"optional string\",\"category\":\"optional string\",\"description\":\"optional string\",\"terms\":\"optional string\",\"closesInDays\":7}"));
                list.Add(new(
                    "officer_review_compliance_and_handoff",
                    "Mark one or all submitted supplier offers on the officer's tender as Compliant or NonCompliant, and optionally send compliant offers to the 4-member Evaluation Committee.",
                    "{\"tenderId\":1,\"bidId\":null,\"decision\":\"Compliant|NonCompliant\",\"notes\":\"optional string\",\"markAllPendingCompliant\":true,\"sendToCommittee\":false,\"shortfallReason\":\"optional string if <3 offers on round 4\"}"));
                list.Add(new(
                    "officer_review_committee_report",
                    "Review a CommitteeApproved evaluation report on the officer's tender (Approve -> sends to Sourcing Manager; RequestEdits -> returns to committee; Reject -> cancels tender).",
                    "{\"tenderId\":1,\"decision\":\"Approve|RequestEdits|Reject\",\"reason\":\"required for RequestEdits or Reject\"}"));
                break;

            case RoleNames.EvaluationCommittee:
                list.Add(new(
                    "evaluator_confirm_weight",
                    "Confirm the current Evaluation Committee member's FinancialWeight whole-number percentage (1–99) on an UnderEvaluation tender (auto-initializing the 4 seats if needed).",
                    "{\"tenderId\":1,\"financialWeight\":40}"));
                list.Add(new(
                    "evaluator_score_offers",
                    "Submit the current Evaluation Committee member's whole-number FinancialMark (0..FinancialWeightCap) and TechnicalMark (0..TechnicalWeightCap) plus optional comment for one or more compliant offers.",
                    "{\"tenderId\":1,\"scores\":[{\"bidId\":1,\"financialMark\":36,\"technicalMark\":54,\"comment\":\"optional string\"}]}"));
                list.Add(new(
                    "evaluator_resolve_tie",
                    "Cast the current committee member's tie-breaker vote when two or more offers are tied for highest score.",
                    "{\"tenderId\":1,\"bidId\":1}"));
                list.Add(new(
                    "evaluator_submit_report",
                    "For Position 1 (Department Manager), finalize and submit the CommitteeApproved evaluation report to the Sourcing Officer once all 4 members have scored.",
                    "{\"tenderId\":1,\"summaryNote\":\"optional executive highlight\"}"));
                break;

            case RoleNames.SourcingManager:
                list.Add(new(
                    "manager_decide_submission",
                    "Approve, Return for review, or Reject an evaluation report in Sourcing Manager review.",
                    "{\"reportId\":1,\"tenderId\":null,\"decision\":\"Approved|ReturnedForReview|Rejected\",\"notes\":\"required when ReturnedForReview or Rejected, optional for Approved\"}"));
                list.Add(new(
                    "manager_forward_to_ceo_chiefs",
                    "Forward an evaluation report (auto-approving first if still OfficerSubmitted) with a policy referral reason to CEO & Chiefs for final award voting.",
                    "{\"reportId\":1,\"tenderId\":null,\"reason\":\"string referral justification\"}"));
                break;

            case RoleNames.TopCommittee:
                list.Add(new(
                    "top_committee_cast_vote",
                    "Cast the current CEO & Chiefs member's decision (Approved, RequestEdit to ask Sourcing Manager/Officer for edits or another offer, or Rejected to completely reject) on an assigned tender in AwaitingCommitteeDecision. Four approvals automatically award the tender.",
                    "{\"tenderId\":1,\"decision\":\"Approved|RequestEdit|Rejected\",\"notes\":\"required when RequestEdit or Rejected, optional when Approved\"}"));
                break;

            case RoleNames.Supplier:
                list.Add(new(
                    "supplier_respond_invitation",
                    "Accept or Decline an open tender invitation sent to this supplier.",
                    "{\"invitationId\":1,\"tenderId\":null,\"decision\":\"Accepted|Declined\"}"));
                list.Add(new(
                    "supplier_upsert_offer",
                    "Create or update this supplier's quotation (offer) in JOD on an open tender invitation (auto-accepts Pending invitation if needed) and save as Draft or Submitted.",
                    "{\"invitationId\":1,\"tenderId\":null,\"deliveryDays\":14,\"paymentTerms\":\"30 days net\",\"technicalDetails\":\"GMP CoA included\",\"taxPercentage\":4,\"additionalCosts\":50,\"defaultUnitPrice\":12.5,\"items\":[{\"purchaseRequestItemId\":1,\"unitPrice\":12.5,\"specifications\":\"optional\"}],\"submit\":true}"));
                list.Add(new(
                    "supplier_withdraw_offer",
                    "Withdraw (cancel) this supplier's submitted offer on an active open tender before the deadline.",
                    "{\"bidId\":1}"));
                break;

            case RoleNames.Admin:
                list.Add(new(
                    "admin_manage_department",
                    "Create a new department or rename/activate/deactivate an existing department.",
                    "{\"departmentId\":null,\"name\":\"string\",\"isActive\":true}"));
                list.Add(new(
                    "admin_review_supplier",
                    "Update a supplier profile's review status (Approved, ChangesRequested, Rejected, Suspended) with review notes.",
                    "{\"supplierProfileId\":1,\"companyName\":\"optional string\",\"status\":\"Approved|ChangesRequested|Rejected|Suspended\",\"notes\":\"string\"}"));
                list.Add(new(
                    "admin_update_user",
                    "Update an account's FullName, IsActive status, DepartmentId, or PhoneNumber (protects current admin and active committee position uniqueness).",
                    "{\"email\":\"user@tiryaq.com\",\"fullName\":\"optional string\",\"isActive\":true,\"departmentId\":null,\"phoneNumber\":\"optional string\"}"));
                list.Add(new(
                    "admin_mark_contact_messages",
                    "Mark a specific contact message or all unread contact messages as Read or Unread.",
                    "{\"contactMessageId\":null,\"status\":\"Read|Unread\",\"markAllRead\":true}"));
                break;
        }

        return list;
    }

    public async Task<AiChatToolExecutionResult> ExecuteAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        string toolName,
        string argumentsJson,
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
            return new AiChatToolExecutionResult(false, false, "Invalid tool arguments JSON.");
        }

        if (toolName == "navigate_to_page")
        {
            return await ResolveNavigationAsync(workspace, user, args, ct);
        }

        if (toolName == "inspect_record_details")
        {
            return await exportService.InspectRecordAsync(workspace, principal, user, args, ct);
        }

        if (toolName == "export_downloadable_file")
        {
            return await exportService.GenerateDownloadAsync(workspace, principal, user, args, ct);
        }

        return (workspace, toolName) switch
        {
            (RoleNames.Department, "department_create_request") => await DepartmentCreateRequestAsync(user, args, ct),
            (RoleNames.Department, "department_update_request") => await DepartmentUpdateRequestAsync(user, args, ct),

            (RoleNames.SourcingOfficer, "officer_review_request") => await OfficerReviewRequestAsync(user, args, ct),
            (RoleNames.SourcingOfficer, "officer_create_tender") => await OfficerCreateTenderAsync(user, args, ct),
            (RoleNames.SourcingOfficer, "officer_review_compliance_and_handoff") => await OfficerReviewComplianceAndHandoffAsync(user, args, ct),
            (RoleNames.SourcingOfficer, "officer_review_committee_report") => await OfficerReviewCommitteeReportAsync(user, args, ct),

            (RoleNames.EvaluationCommittee, "evaluator_confirm_weight") => await EvaluatorConfirmWeightAsync(user, args, ct),
            (RoleNames.EvaluationCommittee, "evaluator_score_offers") => await EvaluatorScoreOffersAsync(user, args, ct),
            (RoleNames.EvaluationCommittee, "evaluator_resolve_tie") => await EvaluatorResolveTieAsync(user, args, ct),
            (RoleNames.EvaluationCommittee, "evaluator_submit_report") => await EvaluatorSubmitReportAsync(user, args, ct),

            (RoleNames.SourcingManager, "manager_decide_submission") => await ManagerDecideSubmissionAsync(user, args, ct),
            (RoleNames.SourcingManager, "manager_forward_to_ceo_chiefs") => await ManagerForwardAsync(user, args, ct),

            (RoleNames.TopCommittee, "top_committee_cast_vote") => await TopCommitteeCastVoteAsync(principal, user, args, ct),

            (RoleNames.Supplier, "supplier_respond_invitation") => await SupplierRespondInvitationAsync(user, args, ct),
            (RoleNames.Supplier, "supplier_upsert_offer") => await SupplierUpsertOfferAsync(user, args, ct),
            (RoleNames.Supplier, "supplier_withdraw_offer") => await SupplierWithdrawOfferAsync(user, args, ct),

            (RoleNames.Admin, "admin_manage_department") => await AdminManageDepartmentAsync(args, ct),
            (RoleNames.Admin, "admin_review_supplier") => await AdminReviewSupplierAsync(user, args, ct),
            (RoleNames.Admin, "admin_update_user") => await AdminUpdateUserAsync(user, args, ct),
            (RoleNames.Admin, "admin_mark_contact_messages") => await AdminMarkContactMessagesAsync(user, args, ct),

            _ => new AiChatToolExecutionResult(
                false,
                false,
                $"Action '{toolName}' is not permitted in the {RoleNames.DisplayName(workspace)} workspace.")
        };
    }

    // ---------------- ROLE-SCOPED NAVIGATION / PAGE TRANSFER ----------------

    private async Task<AiChatToolExecutionResult> ResolveNavigationAsync(
        string workspace,
        ApplicationUser user,
        JsonElement args,
        CancellationToken ct)
    {
        var rawPage = (GetString(args, "page") ?? "").Trim();
        var recordId = GetInt(args, "recordId");
        var key = rawPage.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");

        if (key is "profile" or "account" or "settings" or "accountprofile" or "myprofile")
        {
            return Nav("/Account/Profile", "Account Profile");
        }

        if (key is "aiintelligence" or "intelligence" or "aihub" or "aidashboard")
        {
            if (workspace == RoleNames.Supplier)
                return new(false, false, "The AI Intelligence hub is restricted to internal Tiryaq roles and is not available to Supplier accounts.");

            var suffix = recordId is > 0 ? $"?tenderId={recordId}" : "";
            return Nav($"/{workspace}/AiIntelligence{suffix}", "AI Intelligence");
        }

        switch (workspace)
        {
            case RoleNames.Department:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/Department/Dashboard", "Department Dashboard");
                if (key is "create" or "newrequest" or "createrequest" or "new")
                    return Nav("/Department/Create", "New Request");
                if (key is "requests" or "requestshistory" or "history")
                    return Nav("/Department/Requests", "Requests History");
                if (key is "clarifications" or "clarification" or "questions")
                    return Nav("/Clarification", "Clarifications");
                if (key is "details" or "request" or "requestdetails" && recordId is > 0)
                {
                    if (!await db.PurchaseRequests.AsNoTracking().InDepartment(user.DepartmentId).AnyAsync(r => r.PurchaseRequestId == recordId.Value, ct))
                        return new(false, false, $"Purchase Request #{recordId.Value} was not found in your department.");
                    return Nav($"/Department/Details/{recordId.Value}", $"Request #{recordId.Value}");
                }
                if (key is "edit" or "editrequest" && recordId is > 0)
                {
                    if (!await db.PurchaseRequests.AsNoTracking().InDepartment(user.DepartmentId).AnyAsync(r => r.PurchaseRequestId == recordId.Value && r.RequestedByUserId == user.Id, ct))
                        return new(false, false, $"Purchase Request #{recordId.Value} is not editable by your account.");
                    return Nav($"/Department/Edit/{recordId.Value}", $"Edit Request #{recordId.Value}");
                }
                break;

            case RoleNames.SourcingOfficer:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/SourcingOfficer/Dashboard", "Sourcing Officer Dashboard");
                if (key is "requests" or "requestreview" or "incomingrequests")
                    return Nav(recordId is > 0 ? $"/SourcingOfficer/Requests?id={recordId.Value}" : "/SourcingOfficer/Requests",
                        recordId is > 0 ? $"Request Review #{recordId.Value}" : "Request Review");
                if (key is "offers" or "tenders" or "tenderworkspace" or "tender")
                    return Nav(recordId is > 0 ? $"/SourcingOfficer/Offers?tenderId={recordId.Value}" : "/SourcingOfficer/Offers",
                        recordId is > 0 ? $"Tender Workspace #{recordId.Value}" : "Tender Workspace");
                if (key is "create" or "createtender" or "newtender")
                    return Nav(recordId is > 0 ? $"/SourcingOfficer/Create?purchaseRequestId={recordId.Value}" : "/SourcingOfficer/Create", "Create Tender");
                if (key is "comparison" or "compare" or "offercomparison")
                    return Nav(recordId is > 0 ? $"/SourcingOfficer/Comparison?tenderId={recordId.Value}" : "/SourcingOfficer/Comparison", "Offer Comparison");
                if (key is "reports" or "evaluationreports" or "report")
                    return Nav(recordId is > 0 ? $"/SourcingOfficer/Reports/{recordId.Value}" : "/SourcingOfficer/Reports", "Evaluation Reports");
                if (key is "clarifications" or "clarification" or "questions")
                    return Nav("/Clarification", "Clarifications");
                break;

            case RoleNames.EvaluationCommittee:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/EvaluationCommittee/Dashboard", "Evaluation Committee Dashboard");
                if (key is "evaluations" or "evaluation" or "tender" or "tenders" or "scoring")
                    return Nav(recordId is > 0 ? $"/EvaluationCommittee/Evaluations/{recordId.Value}" : "/EvaluationCommittee/Evaluations",
                        recordId is > 0 ? $"Evaluation #{recordId.Value}" : "Evaluations");
                if (key is "reports" or "combinedreports" or "report" or "combinedreport")
                    return Nav(recordId is > 0 ? $"/EvaluationCommittee/Reports/{recordId.Value}" : "/EvaluationCommittee/Reports",
                        recordId is > 0 ? $"Combined Report #{recordId.Value}" : "Combined Reports");
                if (key is "clarifications" or "clarification" or "questions")
                    return Nav("/Clarification", "Clarifications");
                break;

            case RoleNames.SourcingManager:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/SourcingManager/Dashboard", "Sourcing Manager Dashboard");
                if (key is "submissions" or "officersubmissions" or "committeesubmissions" or "submission")
                    return Nav(recordId is > 0 ? $"/SourcingManager/Submissions?id={recordId.Value}" : "/SourcingManager/Submissions",
                        recordId is > 0 ? $"Submission #{recordId.Value}" : "Officer Submissions");
                if (key is "chosenoffers" or "awards" or "awarded")
                    return Nav("/SourcingManager/ChosenOffers", "Chosen Offers");
                if (key is "reports" or "decisionhistory" or "history")
                    return Nav("/SourcingManager/Reports", "Manager Reports");
                if (key is "clarifications" or "clarification" or "questions")
                    return Nav("/Clarification", "Clarifications");
                break;

            case RoleNames.TopCommittee:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/TopCommittee/Dashboard", "CEO & Chiefs Dashboard");
                if (key is "decisions" or "voting" or "pendingdecisions")
                    return Nav("/TopCommittee/Decisions", "Decisions");
                if (key is "tenderdetails" or "tender" or "decision" && recordId is > 0)
                    return Nav($"/TopCommittee/TenderDetails/{recordId.Value}", $"Tender #{recordId.Value}");
                if (key is "history" or "tenderhistory")
                    return Nav("/TopCommittee/History", "Tender History");
                if (key is "reports" or "report")
                    return Nav("/TopCommittee/Reports", "CEO & Chiefs Reports");
                if (key is "clarifications" or "clarification" or "questions")
                    return Nav("/TopCommittee/Clarifications", "Clarifications");
                break;

            case RoleNames.Supplier:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/Supplier/Dashboard", "Supplier Dashboard");
                if (key is "invitations" or "tenders")
                    return Nav("/Supplier/Invitations", "Invitations");
                if (key is "invitation" && recordId is > 0)
                    return Nav($"/Supplier/Invitation/{recordId.Value}", $"Invitation #{recordId.Value}");
                if (key is "createoffer" or "newoffer" or "submitoffer" && recordId is > 0)
                    return Nav($"/Supplier/CreateOffer?invitationId={recordId.Value}", $"Prepare Offer (Invitation #{recordId.Value})");
                if (key is "offers" or "myoffers" or "quotations")
                    return Nav("/Supplier/Offers", "My Offers");
                if (key is "offer" && recordId is > 0)
                    return Nav($"/Supplier/Offer/{recordId.Value}", $"Offer #{recordId.Value}");
                if (key is "history" or "offerhistory")
                    return Nav("/Supplier/History", "Offer History");
                if (key is "clarifications" or "clarification" or "questions")
                    return Nav("/Clarification", "Clarifications");
                break;

            case RoleNames.Admin:
                if (key is "dashboard" or "home" or "overview")
                    return Nav("/Admin/Dashboard", "Admin Dashboard");
                if (key is "users" or "accounts" or "employees")
                    return Nav("/Admin/Users", "Users");
                if (key is "departments" or "department")
                    return Nav("/Admin/Departments", "Departments");
                if (key is "supplierapplications" or "applications" or "pendingsuppliers")
                    return Nav("/Admin/SupplierApplications", "Supplier Applications");
                if (key is "suppliers" or "approvedsuppliers")
                    return Nav("/Admin/Suppliers", "Suppliers");
                if (key is "supplierdetails" or "supplier" && recordId is > 0)
                    return Nav($"/Admin/SupplierDetails/{recordId.Value}", $"Supplier #{recordId.Value}");
                if (key is "supplieractivity" or "supplieractivityreport" or "activityreport")
                    return Nav("/Admin/SupplierActivity", "Supplier Activity Report");
                if (key is "contactmessages" or "messages" or "inbox")
                    return Nav("/Admin/ContactMessages", "Contact Messages");
                if (key is "contactmessagedetails" or "message" && recordId is > 0)
                    return Nav($"/Admin/ContactMessageDetails/{recordId.Value}", $"Contact Message #{recordId.Value}");
                break;
        }

        var available = PageNavigation.Menus.TryGetValue(workspace, out var menu)
            ? string.Join(", ", menu.Select(m => m.Title))
            : "Dashboard";
        return new(
            false,
            false,
            $"Page '{rawPage}' is not part of your {RoleNames.DisplayName(workspace)} workspace. Available pages in your workspace: {available}.");

        static AiChatToolExecutionResult Nav(string url, string label) =>
            new(true, false, $"Transferring user to {label} ({url}).", url, $"Open {label}", url);
    }

    // ---------------- DEPARTMENT ----------------

    private async Task<AiChatToolExecutionResult> DepartmentCreateRequestAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        if (!user.DepartmentId.HasValue)
            return new(false, false, "Your account is not assigned to an active department.");

        var title = TrimTo(GetString(args, "title"), 200);
        var reason = TrimTo(GetString(args, "reason") ?? $"Department procurement request for {title}.", 2000);
        if (string.IsNullOrWhiteSpace(title))
            return new(false, false, "Provide a request title.");

        var priority = NormalizeChoice(GetString(args, "priority"), PurchaseRequestForm.Priorities, "Normal");
        var earliest = PurchaseRequestForm.EarliestTargetDate;
        var targetDate = ParseDateOnly(GetString(args, "targetDate")) ?? earliest.AddDays(21);
        if (targetDate < earliest) targetDate = earliest.AddDays(14);

        var parsedItems = ParseRequestItems(args);
        if (parsedItems.Count == 0)
        {
            parsedItems.Add(new PurchaseRequestItem
            {
                ItemName = title,
                Quantity = 100m,
                Unit = "units",
                Specifications = reason!
            });
        }

        var request = new PurchaseRequest
        {
            DepartmentId = user.DepartmentId.Value,
            RequestedByUserId = user.Id,
            Title = title,
            Reason = reason!,
            Priority = priority,
            TargetDate = targetDate,
            Status = "Submitted",
            CreatedAt = DateTime.UtcNow,
            SubmittedAt = DateTime.UtcNow,
            PurchaseRequestItems = parsedItems
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync(ct);

        var officers = await users.GetUsersInRoleAsync(RoleNames.SourcingOfficer);
        foreach (var officer in officers.Where(o => o.IsActive))
        {
            db.Notifications.Add(new Notification
            {
                UserId = officer.Id,
                Title = "New purchase request",
                Message = $"Request #{request.PurchaseRequestId}: {request.Title}",
                RelativeUrl = $"/SourcingOfficer/Requests?id={request.PurchaseRequestId}",
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Created and submitted Purchase Request #{request.PurchaseRequestId} ('{request.Title}', Priority: {request.Priority}, Target: {request.TargetDate:yyyy-MM-dd}, {request.PurchaseRequestItems.Count} item(s)).",
            $"/Department/Details/{request.PurchaseRequestId}",
            $"Open Request #{request.PurchaseRequestId}");
    }

    private async Task<AiChatToolExecutionResult> DepartmentUpdateRequestAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        if (!user.DepartmentId.HasValue)
            return new(false, false, "Your account is not assigned to a department.");

        var requestId = GetInt(args, "requestId") ?? 0;
        if (requestId <= 0)
            return new(false, false, "Specify a valid requestId.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var request = await db.PurchaseRequests
            .Include(r => r.PurchaseRequestItems)
            .Include(r => r.Tenders)
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == requestId && r.DepartmentId == user.DepartmentId, ct);

        if (request == null)
            return new(false, false, $"Purchase Request #{requestId} was not found in your department.");
        if (request.RequestedByUserId != user.Id)
            return new(false, false, "Only the original requester can edit this purchase request.");
        if (request.Status is not ("Returned" or "Submitted") || request.Tenders.Count > 0)
            return new(false, false, $"Purchase Request #{requestId} is currently '{request.Status}' and can no longer be edited.");

        var title = TrimTo(GetString(args, "title"), 200);
        var reason = TrimTo(GetString(args, "reason"), 2000);
        var priority = GetString(args, "priority");
        var targetDate = ParseDateOnly(GetString(args, "targetDate"));

        if (!string.IsNullOrWhiteSpace(title)) request.Title = title;
        if (!string.IsNullOrWhiteSpace(reason)) request.Reason = reason;
        if (!string.IsNullOrWhiteSpace(priority) && PurchaseRequestForm.Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
            request.Priority = PurchaseRequestForm.Priorities.First(p => string.Equals(p, priority, StringComparison.OrdinalIgnoreCase));
        if (targetDate.HasValue && targetDate.Value >= PurchaseRequestForm.EarliestTargetDate)
            request.TargetDate = targetDate.Value;

        var newItems = ParseRequestItems(args);
        if (newItems.Count > 0)
        {
            // Replacing the items must not wipe the legal warranty the department asked for. Keep it for items with the same name.
            var keptWarranty = request.PurchaseRequestItems.Where(i => i.LegalWarrantyMonths != null)
                .GroupBy(i => i.ItemName.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().LegalWarrantyMonths);
            foreach (var item in newItems.Where(i => i.LegalWarrantyMonths == null))
                if (keptWarranty.TryGetValue(item.ItemName.Trim().ToLowerInvariant(), out var months)) item.LegalWarrantyMonths = months;
            db.PurchaseRequestItems.RemoveRange(request.PurchaseRequestItems);
            request.PurchaseRequestItems.Clear();
            foreach (var item in newItems)
                request.PurchaseRequestItems.Add(item);
        }

        var wasReturned = request.Status == "Returned";
        request.Status = "Submitted";
        request.SubmittedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Updated {(wasReturned ? "and resubmitted " : "")}Purchase Request #{request.PurchaseRequestId} ('{request.Title}', Priority: {request.Priority}, Target: {request.TargetDate:yyyy-MM-dd}).",
            $"/Department/Details/{request.PurchaseRequestId}",
            $"Open Request #{request.PurchaseRequestId}");
    }

    // ---------------- SOURCING OFFICER ----------------

    private async Task<AiChatToolExecutionResult> OfficerReviewRequestAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var requestId = GetInt(args, "requestId") ?? 0;
        var rawDecision = (GetString(args, "decision") ?? "").Trim();
        var notes = TrimTo(GetString(args, "notes"), 2000);

        var decision = rawDecision.ToLowerInvariant() switch
        {
            "start" or "startreview" or "underreview" => "StartReview",
            "approve" or "approved" => "Approved",
            "return" or "returned" => "Returned",
            "reject" or "rejected" => "Rejected",
            _ => ""
        };
        if (requestId <= 0 || decision == "")
            return new(false, false, "Specify requestId and decision (StartReview, Approved, Returned, or Rejected).");

        if (decision is "Returned" or "Rejected" && string.IsNullOrWhiteSpace(notes))
            return new(false, false, $"Provide review notes explaining why Request #{requestId} is {decision.ToLowerInvariant()}.");

        var req = await db.PurchaseRequests
            .Include(r => r.Clarifications)
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == requestId && r.Status != "Draft", ct);
        if (req == null)
            return new(false, false, $"Purchase Request #{requestId} was not found.");
        if (req.ReviewedByUserId != null && req.ReviewedByUserId != user.Id)
            return new(false, false, $"Purchase Request #{requestId} is assigned to another Sourcing Officer.");

        if (decision == "StartReview")
        {
            if (req.Status != "Submitted")
                return new(false, false, $"Purchase Request #{requestId} is already '{req.Status}'.");
            req.Status = "UnderReview";
            req.ReviewedByUserId = user.Id;
            await db.SaveChangesAsync(ct);
            return new(true, true, $"Started review on Purchase Request #{requestId} ('{req.Title}').", $"/SourcingOfficer/Requests?id={requestId}", $"Open Request #{requestId}");
        }

        if (req.Status is not ("Submitted" or "UnderReview"))
            return new(false, false, $"Purchase Request #{requestId} is currently '{req.Status}' and cannot be decided again.");
        if (req.Clarifications.Any(c => c.Status != "Resolved"))
            return new(false, false, $"Resolve open clarifications on Request #{requestId} before recording a decision.");

        req.Status = decision;
        req.ReviewedByUserId = user.Id;
        req.ReviewedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes)) req.ReviewNotes = notes;
        await db.SaveChangesAsync(ct);

        return new(
            true,
            true,
            $"Recorded '{decision}' on Purchase Request #{requestId} ('{req.Title}').",
            $"/SourcingOfficer/Requests?id={requestId}",
            $"Open Request #{requestId}");
    }

    private async Task<AiChatToolExecutionResult> OfficerCreateTenderAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var requestId = GetInt(args, "requestId") ?? 0;
        if (requestId <= 0)
            return new(false, false, "Specify an eligible requestId to convert into a tender.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var req = await db.PurchaseRequests
            .Include(r => r.PurchaseRequestItems)
            .Include(r => r.Clarifications)
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == requestId, ct);

        if (req == null)
            return new(false, false, $"Purchase Request #{requestId} was not found.");
        if (await db.Tenders.AnyAsync(t => t.PurchaseRequestId == requestId, ct))
            return new(false, false, $"A tender already exists for Purchase Request #{requestId}.");
        if (req.Status != "Approved" &&
            !(req.Status == "UnderReview" && req.ReviewedByUserId == user.Id) &&
            !(req.Status == "Submitted" && (req.ReviewedByUserId == null || req.ReviewedByUserId == user.Id)))
            return new(false, false, $"Purchase Request #{requestId} (status '{req.Status}') is not eligible for tender creation.");
        if (req.Clarifications.Any(c => c.Status != "Resolved"))
            return new(false, false, $"Resolve open clarifications on Request #{requestId} before publishing a tender.");

        var requestedCategory = TrimTo(GetString(args, "category"), 100);
        var approvedSuppliers = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == "Approved" && s.User.IsActive)
            .OrderBy(s => s.SupplierProfileId)
            .ToListAsync(ct);
        if (approvedSuppliers.Count == 0)
            return new(false, false, "No approved active suppliers exist to invite.");

        var matchedSuppliers = !string.IsNullOrWhiteSpace(requestedCategory)
            ? approvedSuppliers.Where(s => string.Equals(s.Category, requestedCategory, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        if (matchedSuppliers.Count == 0)
        {
            requestedCategory = approvedSuppliers[0].Category ?? "Pharmaceuticals";
            matchedSuppliers = approvedSuppliers.Where(s => string.Equals(s.Category, requestedCategory, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var closesInDays = Math.Clamp(GetInt(args, "closesInDays") ?? 7, 1, 60);
        var now = DateTime.UtcNow;
        var title = TrimTo(GetString(args, "title"), 200);
        if (string.IsNullOrWhiteSpace(title)) title = req.Title;
        var desc = TrimTo(GetString(args, "description"), 4000);
        if (string.IsNullOrWhiteSpace(desc))
        {
            desc = req.Reason + "\n" + string.Join("\n", req.PurchaseRequestItems.Select(i => $"{i.ItemName}: {i.Quantity:0.###} {i.Unit}. {i.Specifications}"));
            desc = TrimTo(desc, 4000)!;
        }
        var terms = TrimTo(GetString(args, "terms"), 4000) ?? "GMP/ISO compliance certificates and batch CoA required upon delivery.";

        if (req.Status is "Submitted" or "UnderReview")
        {
            req.ReviewedByUserId = user.Id;
            req.ReviewedAt = now;
        }
        req.Status = "ConvertedToTender";

        var tender = new Tender
        {
            PurchaseRequestId = req.PurchaseRequestId,
            CreatedByUserId = user.Id,
            Title = title,
            Category = requestedCategory,
            Description = desc,
            Terms = terms,
            OpensAt = now,
            ClosesAt = now.AddDays(closesInDays),
            Status = "Published"
        };

        foreach (var sup in matchedSuppliers)
        {
            tender.Invitations.Add(new Invitation
            {
                SupplierProfileId = sup.SupplierProfileId,
                InvitedByUserId = user.Id,
                Status = "Pending",
                SentAt = now
            });
        }

        db.Tenders.Add(tender);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Published Tender #{tender.TenderId} ('{tender.Title}', category '{tender.Category}') from Request #{req.PurchaseRequestId} and invited {matchedSuppliers.Count} approved supplier(s).",
            $"/SourcingOfficer/Offers?tenderId={tender.TenderId}",
            $"Open Tender #{tender.TenderId}");
    }

    private async Task<AiChatToolExecutionResult> OfficerReviewComplianceAndHandoffAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        var bidId = GetInt(args, "bidId");
        var rawDecision = GetString(args, "decision");
        var notes = TrimTo(GetString(args, "notes"), 2000);
        var markAllCompliant = GetBool(args, "markAllPendingCompliant") ?? false;
        var sendToCommittee = GetBool(args, "sendToCommittee") ?? false;
        var shortfallReason = TrimTo(GetString(args, "shortfallReason"), 2000);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var tender = await db.Tenders
            .Include(t => t.OfferExtensions)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == user.Id, ct);
        if (tender == null)
            return new(false, false, $"Tender #{tenderId} was not found in your Sourcing Officer workspace.");

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId, ct) ||
            await db.Evaluations.AnyAsync(e => e.TenderId == tenderId, ct) ||
            tender.Status is not ("Published" or "Open" or "Closed"))
            return new(false, false, $"Tender #{tenderId} is already in '{tender.Status}' and past compliance review.");

        var bids = await db.Bids
            .Include(b => b.Invitation)
            .Include(b => b.Clarifications)
            .Where(b => b.TenderId == tenderId && b.SubmittedAt != null && b.Status != "Withdrawn")
            .ToListAsync(ct);

        if (bids.Count == 0)
            return new(false, false, $"Tender #{tenderId} has no submitted supplier offers yet.");

        var now = DateTime.UtcNow;
        var updatedCount = 0;

        if (bidId.HasValue && bidId.Value > 0)
        {
            var targetBid = bids.SingleOrDefault(b => b.BidId == bidId.Value);
            if (targetBid == null)
                return new(false, false, $"Offer #{bidId.Value} was not found on Tender #{tenderId}.");
            if (targetBid.ComplianceStatus is "Compliant" or "NonCompliant")
                return new(false, false, $"Offer #{bidId.Value} already has a final compliance decision ({targetBid.ComplianceStatus}).");
            if (targetBid.Clarifications.Any(c => c.Status != "Resolved"))
                return new(false, false, $"Offer #{bidId.Value} has unresolved clarifications.");

            var dec = string.Equals(rawDecision, "NonCompliant", StringComparison.OrdinalIgnoreCase) ? "NonCompliant" : "Compliant";
            targetBid.ComplianceStatus = dec;
            targetBid.Status = dec;
            targetBid.ComplianceNotes = notes;
            targetBid.ComplianceReviewedByUserId = user.Id;
            targetBid.ComplianceReviewedAt = now;
            updatedCount++;
        }

        if (markAllCompliant)
        {
            foreach (var b in bids.Where(b => b.ComplianceStatus is not ("Compliant" or "NonCompliant") && b.Clarifications.All(c => c.Status == "Resolved")))
            {
                b.ComplianceStatus = "Compliant";
                b.Status = "Compliant";
                b.ComplianceNotes = notes ?? "Verified compliant with tender requirements.";
                b.ComplianceReviewedByUserId = user.Id;
                b.ComplianceReviewedAt = now;
                updatedCount++;
            }
        }

        if (!sendToCommittee)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(
                true,
                updatedCount > 0,
                $"Saved compliance decision(s) on {updatedCount} offer(s) for Tender #{tenderId}.",
                $"/SourcingOfficer/Offers?tenderId={tenderId}",
                $"Open Tender #{tenderId}");
        }

        if (bids.Any(b => b.ComplianceStatus is not ("Compliant" or "NonCompliant")))
            return new(false, false, $"Every submitted offer on Tender #{tenderId} must be marked Compliant or NonCompliant before sending to the Evaluation Committee.");

        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
            ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn")), ct))
            return new(false, false, $"Resolve open clarifications on Tender #{tenderId} before sending to the Evaluation Committee.");

        var compliantSuppliers = bids
            .Where(b => b.ComplianceStatus == "Compliant")
            .Select(b => b.Invitation.SupplierProfileId)
            .Distinct()
            .Count();

        var round = 1 + tender.OfferExtensions.Count;
        if (tender.ClosesAt > now)
        {
            if (compliantSuppliers < TenderOfferExtension.RequiredCompliantOffers || round > 1)
                return new(false, false, $"Tender #{tenderId} deadline has not passed yet ({tender.ClosesAt:yyyy-MM-dd HH:mm} UTC). At least 3 compliant suppliers on Round 1 are required to close early.");
            if (tender.OpensAt >= now) tender.OpensAt = now.AddMinutes(-5);
            tender.ClosesAt = now;
        }

        if (compliantSuppliers == 0)
            return new(false, false, "At least one compliant offer is required.");
        if (compliantSuppliers < TenderOfferExtension.RequiredCompliantOffers &&
            (round < TenderOfferExtension.MaximumRound || string.IsNullOrWhiteSpace(shortfallReason)))
        {
            return new(false, false, $"Tender #{tenderId} currently has {compliantSuppliers} compliant supplier(s) on round {round} (standard handoff requires 3, or round 4 with a shortfallReason).");
        }

        tender.OfferShortfallReason = compliantSuppliers < TenderOfferExtension.RequiredCompliantOffers ? shortfallReason : null;
        var setupErr = await CommitteeSetup.Prepare(db, tender);
        if (setupErr != null)
            return new(false, false, setupErr);

        tender.Status = "UnderEvaluation";
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Reviewed compliance and handed off Tender #{tenderId} ('{tender.Title}') with {compliantSuppliers} compliant supplier offer(s) to the 4-member Evaluation Committee.",
            $"/SourcingOfficer/Offers?tenderId={tenderId}",
            $"Open Tender #{tenderId}");
    }

    private async Task<AiChatToolExecutionResult> OfficerReviewCommitteeReportAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        var rawDecision = (GetString(args, "decision") ?? "").Trim();
        var reason = TrimTo(GetString(args, "reason"), 4000);

        var decision = rawDecision.ToLowerInvariant() switch
        {
            "approve" or "approved" => "Approve",
            "requestedits" or "return" or "returned" or "edits" => "RequestEdits",
            "reject" or "rejected" => "Reject",
            _ => ""
        };
        if (tenderId <= 0 || decision == "")
            return new(false, false, "Specify tenderId and decision (Approve, RequestEdits, or Reject).");
        if (decision != "Approve" && string.IsNullOrWhiteSpace(reason))
            return new(false, false, $"Provide a reason when choosing {decision}.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var tender = await db.Tenders
            .Include(t => t.EvaluationReport)
            .Include(t => t.CommitteeAssignments)
            .Include(t => t.TenderApprovals)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == user.Id, ct);

        if (tender == null)
            return new(false, false, $"Tender #{tenderId} was not found in your Sourcing Officer workspace.");
        var report = tender.EvaluationReport;
        if (tender.Status != "AwaitingOfficerSummary" || report?.Status != "CommitteeApproved")
            return new(false, false, $"Tender #{tenderId} does not have a CommitteeApproved report awaiting officer review (current tender status: '{tender.Status}').");

        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
            ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn")), ct))
            return new(false, false, "Resolve open clarifications before completing report review.");

        report.OfficerSummary = decision == "Approve"
            ? "Approved by sourcing officer. Report and recommended offer forwarded unchanged."
            : (decision == "RequestEdits" ? "Edits requested: " : "Rejected by sourcing officer: ") + reason;

        if (decision == "Approve")
        {
            tender.Status = "AwaitingManagerReview";
            report.Status = "OfficerSubmitted";
            report.SubmittedAt = DateTime.UtcNow;
        }
        else if (decision == "RequestEdits")
        {
            tender.Status = "UnderEvaluation";
            report.Status = "ReturnedForReview";
            report.RecommendedBidId = null;
            report.CommitteeSummary = null;
            db.CommitteeTieVotes.RemoveRange(await db.CommitteeTieVotes.Where(v => v.TenderId == tenderId).ToListAsync(ct));
            db.TenderApprovals.RemoveRange(tender.TenderApprovals.Where(a => a.Stage == "Evaluation"));
            foreach (var ev in await db.Evaluations.Where(e => e.TenderId == tenderId).ToListAsync(ct))
            {
                ev.Status = "Submitted";
                ev.ApprovedAt = null;
            }
        }
        else
        {
            tender.Status = "Cancelled";
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Recorded officer decision '{decision}' on Evaluation Report #{report.EvaluationReportId} for Tender #{tenderId} (new tender status: '{tender.Status}').",
            $"/SourcingOfficer/Reports/{report.EvaluationReportId}",
            $"Open Report #{report.EvaluationReportId}");
    }

    // ---------------- EVALUATION COMMITTEE ----------------

    private IQueryable<Tender> LoadEvaluationTenderQuery() => db.Tenders
        .AsSplitQuery()
        .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
        .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
        .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant")).ThenInclude(b => b.BidItems)
        .Include(t => t.CommitteeWeightProposals)
        .Include(t => t.CommitteeTieVotes)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
        .Include(t => t.TenderApprovals)
        .Include(t => t.OfferExtensions)
        .Include(t => t.EvaluationReport);

    private async Task<AiChatToolExecutionResult> EvaluatorConfirmWeightAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        var weight = GetInt(args, "financialWeight");
        if (tenderId <= 0 || weight is null or <= 0 or >= 100)
            return new(false, false, "Provide tenderId and a whole-number financialWeight between 1 and 99.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};", ct);

        var tender = await LoadEvaluationTenderQuery().SingleOrDefaultAsync(t => t.TenderId == tenderId, ct);
        if (tender == null || tender.Status != "UnderEvaluation")
            return new(false, false, $"Tender #{tenderId} is not currently UnderEvaluation.");

        if (!tender.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation"))
        {
            var prepErr = await CommitteeSetup.Prepare(db, tender);
            if (prepErr != null) return new(false, false, prepErr);
            await db.SaveChangesAsync(ct);
            tender = await LoadEvaluationTenderQuery().SingleAsync(t => t.TenderId == tenderId, ct);
        }

        var evals = await db.Evaluations.Where(e => e.TenderId == tenderId).ToListAsync(ct);
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = user.Id, Evaluations = evals };
        if (!page.Members.Any(m => m.UserId == user.Id))
            return new(false, false, "Your account is not assigned to this tender's Evaluation Committee.");

        var err = CommitteeConsensus.ConfirmWeight(page, weight.Value, DateTime.UtcNow);
        if (err != null)
            return new(false, false, err);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var lockMsg = page.ConsensusLocked
            ? $"Consensus locked at {page.FinancialWeight:0}% Financial / {page.TechnicalWeight:0}% Technical!"
            : $"({page.WeightProposals.Count} of 4 member weights confirmed).";

        return new(
            true,
            true,
            $"Confirmed your financial weight at {weight.Value}% on Tender #{tenderId}. {lockMsg}",
            $"/EvaluationCommittee/Evaluations/{tenderId}",
            $"Open Evaluation #{tenderId}");
    }

    private async Task<AiChatToolExecutionResult> EvaluatorScoreOffersAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        if (tenderId <= 0 || !args.TryGetProperty("scores", out var scoresEl) || scoresEl.ValueKind != JsonValueKind.Array || scoresEl.GetArrayLength() == 0)
            return new(false, false, "Provide tenderId and a non-empty scores array of { bidId, financialMark, technicalMark, comment }.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};", ct);

        var tender = await LoadEvaluationTenderQuery().SingleOrDefaultAsync(t => t.TenderId == tenderId, ct);
        if (tender == null || tender.Status != "UnderEvaluation")
            return new(false, false, $"Tender #{tenderId} is not currently UnderEvaluation.");

        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == tenderId || (c.Bid != null && c.Bid.TenderId == tenderId)), ct))
            return new(false, false, $"Resolve open clarifications on Tender #{tenderId} before submitting marks.");

        var evals = await db.Evaluations.Include(e => e.EvaluationScores).Where(e => e.TenderId == tenderId).ToListAsync(ct);
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = user.Id, Evaluations = evals };

        if (!page.Members.Any(m => m.UserId == user.Id))
            return new(false, false, "Your account is not assigned to this tender's Evaluation Committee.");
        if (!page.ConsensusLocked)
            return new(false, false, $"Weights are not locked yet ({page.WeightProposals.Count}/4 confirmed). Confirm all 4 percentages before scoring.");

        var now = DateTime.UtcNow;
        var savedDetails = new List<string>();
        foreach (var item in scoresEl.EnumerateArray())
        {
            var bId = GetInt(item, "bidId") ?? 0;
            var fin = GetInt(item, "financialMark");
            var tech = GetInt(item, "technicalMark");
            var comment = GetString(item, "comment");

            var err = AllOfferScoring.Save(page, bId, fin, tech, comment, now);
            if (err != null)
                return new(false, false, $"Offer #{bId}: {err} (Allowed limits: Financial 0–{page.FinancialWeight:0}, Technical 0–{page.TechnicalWeight:0}).");

            savedDetails.Add($"Offer #{bId} ({fin}/{page.FinancialWeight:0} Fin, {tech}/{page.TechnicalWeight:0} Tech)");
        }

        foreach (var entry in page.Evaluations.Where(e => e.EvaluationId == 0))
            db.Evaluations.Add(entry);

        AllOfferScoring.SelectWinner(page, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var winnerNote = page.SelectionReady && tender.EvaluationReport?.RecommendedBidId is int winId
            ? $" All 4/4 reviews are now complete—Offer #{winId} leads and is ready for the committee report!"
            : "";

        return new(
            true,
            true,
            $"Saved and locked your marks on Tender #{tenderId}: {string.Join(", ", savedDetails)}.{winnerNote}",
            $"/EvaluationCommittee/Evaluations/{tenderId}",
            $"Open Evaluation #{tenderId}");
    }

    private async Task<AiChatToolExecutionResult> EvaluatorResolveTieAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        var bidId = GetInt(args, "bidId") ?? 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};", ct);

        var tender = await LoadEvaluationTenderQuery().SingleOrDefaultAsync(t => t.TenderId == tenderId, ct);
        if (tender == null || tender.Status != "UnderEvaluation")
            return new(false, false, $"Tender #{tenderId} is not UnderEvaluation.");

        var evals = await db.Evaluations.Where(e => e.TenderId == tenderId).ToListAsync(ct);
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = user.Id, Evaluations = evals };
        var member = page.Members.SingleOrDefault(m => m.UserId == user.Id);
        if (member == null) return new(false, false, "You are not assigned to this tender's Evaluation Committee.");

        var leaders = AllOfferScoring.Leaders(page);
        if (leaders.Count < 2 || !leaders.Any(l => l.BidId == bidId))
            return new(false, false, "Tie resolution is only available when two or more offers are tied for first place after all 4 members have scored.");

        var vote = tender.CommitteeTieVotes.SingleOrDefault(v => v.CommitteeAssignmentId == member.CommitteeAssignmentId);
        if (vote == null)
        {
            vote = new CommitteeTieVote { TenderId = tenderId, CommitteeAssignmentId = member.CommitteeAssignmentId };
            tender.CommitteeTieVotes.Add(vote);
        }
        vote.BidId = bidId;
        vote.ConfirmedAt = DateTime.UtcNow;
        AllOfferScoring.SelectWinner(page, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Recorded your tie-breaker vote for Offer #{bidId} on Tender #{tenderId}.",
            $"/EvaluationCommittee/Evaluations/{tenderId}",
            $"Open Evaluation #{tenderId}");
    }

    private async Task<AiChatToolExecutionResult> EvaluatorSubmitReportAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        var summaryNote = TrimTo(GetString(args, "summaryNote"), 800);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};", ct);

        var tender = await LoadEvaluationTenderQuery().SingleOrDefaultAsync(t => t.TenderId == tenderId, ct);
        if (tender == null || tender.Status != "UnderEvaluation")
            return new(false, false, $"Tender #{tenderId} is not currently UnderEvaluation.");

        var evals = await db.Evaluations.Where(e => e.TenderId == tenderId).ToListAsync(ct);
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = user.Id, Evaluations = evals };
        if (!page.IsDepartmentManager)
            return new(false, false, "Only Position 1 (Department Manager, eval1@tiryaq.com) can submit the final combined committee report.");
        if (!AllOfferScoring.Complete(page))
            return new(false, false, "All 4 committee members must finish scoring every eligible offer before generating the report.");

        AllOfferScoring.SelectWinner(page, DateTime.UtcNow);
        if (!page.SelectionReady || tender.EvaluationReport?.RecommendedBidId is not int winBidId)
            return new(false, false, "Resolve any score tie between leading offers before submitting the report.");
        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == tenderId || (c.Bid != null && c.Bid.TenderId == tenderId)), ct))
            return new(false, false, "Resolve open clarifications before submitting the final report.");

        var winner = page.Offers.Single(b => b.BidId == winBidId);
        var winnerName = winner.Invitation.SupplierProfile.CompanyName;
        var ranking = AllOfferScoring.Ranking(page);
        var topRow = ranking.First(r => r.BidId == winBidId);

        var header =
            $"Recommended offer\n**{winnerName}** (Offer #{winBidId}) is recommended with the highest four-member average score ({topRow.Score:0.##} / 100) at {CommitteeEvaluationPage.Total(winner):N2} {winner.Currency}.\n\n" +
            (string.IsNullOrWhiteSpace(summaryNote) ? "" : $"Conclusion\n{summaryNote}\n\n");

        tender.EvaluationReport.CommitteeSummary = header + AllOfferScoring.Report(page);
        tender.EvaluationReport.PreparedByUserId = user.Id;
        EvaluationCommitteeController.CompleteWeightApproval(page);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Generated and submitted CommitteeApproved Report #{tender.EvaluationReport.EvaluationReportId} for Tender #{tenderId} recommending {winnerName} (Offer #{winBidId}). Sent to Sourcing Officer for summary review.",
            $"/EvaluationCommittee/Reports/{tenderId}",
            $"Open Combined Report #{tenderId}");
    }

    // ---------------- SOURCING MANAGER ----------------

    private async Task<AiChatToolExecutionResult> ManagerDecideSubmissionAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var reportId = GetInt(args, "reportId");
        var tenderId = GetInt(args, "tenderId");
        var rawDecision = (GetString(args, "decision") ?? "").Trim();
        var notes = TrimTo(GetString(args, "notes"), 4000);

        var decision = rawDecision.ToLowerInvariant() switch
        {
            "approve" or "approved" => "Approved",
            "return" or "returned" or "returnedforreview" => "ReturnedForReview",
            "reject" or "rejected" => "Rejected",
            _ => ""
        };
        if (decision == "")
            return new(false, false, "Choose a valid decision: Approved, ReturnedForReview, or Rejected.");
        if (decision != "Approved" && string.IsNullOrWhiteSpace(notes))
            return new(false, false, $"Provide notes explaining why the submission is {decision}.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var report = await db.EvaluationReports
            .Include(r => r.Tender)
            .SingleOrDefaultAsync(r =>
                (reportId.HasValue && r.EvaluationReportId == reportId.Value) ||
                (!reportId.HasValue && tenderId.HasValue && r.TenderId == tenderId.Value), ct);

        if (report == null)
            return new(false, false, "Evaluation submission was not found.");
        if (report.Tender.Status != "AwaitingManagerReview" || report.Status is not ("OfficerSubmitted" or "CommitteeApproved"))
            return new(false, false, $"Submission #{report.EvaluationReportId} is currently '{report.Status}' (tender '{report.Tender.Status}') and not awaiting a manager decision.");

        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == report.TenderId || (c.Bid != null && c.Bid.TenderId == report.TenderId)), ct))
            return new(false, false, "Resolve open clarifications before recording a manager decision.");

        if (decision == "Approved")
        {
            report.Status = "ManagerApproved";
        }
        else if (decision == "Rejected")
        {
            report.Status = "ManagerRejected";
            report.Tender.Status = "Closed";
        }
        else
        {
            report.Status = "ReturnedForReview";
            report.Tender.Status = "UnderEvaluation";
            report.RecommendedBidId = null;
            report.CommitteeSummary = null;
            db.TenderApprovals.RemoveRange(await db.TenderApprovals.Where(a => a.TenderId == report.TenderId && a.Stage == "Evaluation").ToListAsync(ct));
            db.CommitteeTieVotes.RemoveRange(await db.CommitteeTieVotes.Where(v => v.TenderId == report.TenderId).ToListAsync(ct));
            foreach (var rev in await db.Evaluations.Where(e => e.TenderId == report.TenderId).ToListAsync(ct))
            {
                rev.Status = "Submitted";
                rev.ApprovedAt = null;
            }
        }

        report.ManagerSummary = notes;
        var approval = await db.TenderApprovals.SingleOrDefaultAsync(a => a.TenderId == report.TenderId && a.Stage == "Manager" && a.UserId == user.Id, ct);
        if (approval == null)
        {
            approval = new TenderApproval { TenderId = report.TenderId, Stage = "Manager", UserId = user.Id };
            db.TenderApprovals.Add(approval);
        }
        approval.Decision = decision;
        approval.Notes = notes;
        approval.DecidedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Recorded Sourcing Manager decision '{decision}' on Submission #{report.EvaluationReportId} (Tender #{report.TenderId}: '{report.Tender.Title}').",
            $"/SourcingManager/Submissions?id={report.EvaluationReportId}",
            $"Open Submission #{report.EvaluationReportId}");
    }

    private async Task<AiChatToolExecutionResult> ManagerForwardAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var reportId = GetInt(args, "reportId");
        var tenderId = GetInt(args, "tenderId");
        var reason = TrimTo(GetString(args, "reason"), 2000) ?? "Referred to CEO & Chiefs for final award decision in accordance with Tiryaq governance policy.";

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var report = await db.EvaluationReports
            .Include(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(pr => pr.Department)
            .Include(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(pr => pr.PurchaseRequestItems)
            .Include(r => r.Tender).ThenInclude(t => t.OfferExtensions)
            .SingleOrDefaultAsync(r =>
                (reportId.HasValue && r.EvaluationReportId == reportId.Value) ||
                (!reportId.HasValue && tenderId.HasValue && r.TenderId == tenderId.Value), ct);

        if (report == null)
            return new(false, false, "Evaluation submission was not found.");
        var tender = report.Tender;
        if (tender.Status != "AwaitingManagerReview" ||
            await db.TenderApprovals.AnyAsync(a => a.TenderId == tender.TenderId && a.Stage == "Top" && a.Decision == "Rejected", ct))
            return new(false, false, $"Tender #{tender.TenderId} is currently '{tender.Status}' and cannot be forwarded.");

        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" && (c.TenderId == tender.TenderId || (c.Bid != null && c.Bid.TenderId == tender.TenderId)), ct))
            return new(false, false, "Resolve open clarifications before forwarding to CEO & Chiefs.");

        var winningOffer = await db.Bids
            .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.BidItems)
            .SingleOrDefaultAsync(b => b.TenderId == tender.TenderId && b.BidId == report.RecommendedBidId, ct);
        if (winningOffer == null)
            return new(false, false, "Recommended offer is missing on this submission.");

        if (report.Status is "OfficerSubmitted" or "CommitteeApproved")
        {
            report.Status = "ManagerApproved";
            var mgrApproval = await db.TenderApprovals.SingleOrDefaultAsync(a => a.TenderId == tender.TenderId && a.Stage == "Manager" && a.UserId == user.Id, ct);
            if (mgrApproval == null)
            {
                mgrApproval = new TenderApproval { TenderId = tender.TenderId, Stage = "Manager", UserId = user.Id };
                db.TenderApprovals.Add(mgrApproval);
            }
            mgrApproval.Decision = "Approved";
            mgrApproval.Notes = reason;
            mgrApproval.DecidedAt = DateTime.UtcNow;
        }

        if (report.Status != "ManagerApproved")
            return new(false, false, $"Submission #{report.EvaluationReportId} has status '{report.Status}' and cannot be forwarded.");

        var topAssignments = await db.CommitteeAssignments
            .Where(a => a.TenderId == tender.TenderId && a.CommitteeType == "Top")
            .ToListAsync(ct);

        if (topAssignments.Count == 0)
        {
            var topIds = new List<string>();
            foreach (var posRole in RoleNames.TopPositions)
            {
                var candidate = await db.Users
                    .Where(u => u.IsActive &&
                        db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == posRole)) &&
                        db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee)))
                    .Select(u => u.Id)
                    .SingleOrDefaultAsync(ct);
                if (candidate == null)
                    return new(false, false, $"Admin must configure one active CEO & Chiefs account for {posRole}.");
                topIds.Add(candidate);
            }
            for (var pos = 1; pos <= 4; pos++)
            {
                db.CommitteeAssignments.Add(new CommitteeAssignment
                {
                    TenderId = tender.TenderId,
                    UserId = topIds[pos - 1],
                    CommitteeType = "Top",
                    Position = pos
                });
            }
            await db.SaveChangesAsync(ct);
        }

        tender.Status = "AwaitingCommitteeDecision";
        var existingNotes = string.IsNullOrWhiteSpace(report.ManagerSummary) ? $"Referral reason: {reason}" : $"{report.ManagerSummary}\n\nReferral reason: {reason}";
        report.ManagerSummary = ManagerReferralContext.Describe(tender, winningOffer) + "\n\n## Manager assessment\n" + existingNotes;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            true,
            true,
            $"Approved and forwarded Tender #{tender.TenderId} ('{tender.Title}', nominating {winningOffer.Invitation.SupplierProfile.CompanyName} — Offer #{winningOffer.BidId}) to CEO & Chiefs for final voting.",
            "/SourcingManager/Submissions",
            "Open Submissions");
    }

    // ---------------- TOP COMMITTEE (CEO & CHIEFS) ----------------

    private async Task<AiChatToolExecutionResult> TopCommitteeCastVoteAsync(ClaimsPrincipal principal, ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var tenderId = GetInt(args, "tenderId") ?? 0;
        var rawDecision = (GetString(args, "decision") ?? "").Trim();
        var notes = TrimTo(GetString(args, "notes"), 4000);

        var decision = rawDecision.ToLowerInvariant().Replace(" ", "").Replace("_", "") switch
        {
            "approve" or "approved" => "Approved",
            "requestedit" or "requestedits" or "askforedit" or "edit" => "RequestEdit",
            "reject" or "rejected" or "rejectcompletely" => "Rejected",
            _ => ""
        };
        if (tenderId <= 0 || decision == "")
            return new(false, false, "Provide tenderId and decision (Approved, RequestEdit, or Rejected).");
        if (decision is "Rejected" or "RequestEdit" && string.IsNullOrWhiteSpace(notes))
            return new(false, false, $"A written note is required when choosing {decision}.");

        var nominatedBidId = await db.EvaluationReports.AsNoTracking()
            .Where(r => r.TenderId == tenderId)
            .Select(r => r.RecommendedBidId)
            .SingleOrDefaultAsync(ct);
        if (!nominatedBidId.HasValue)
            return new(false, false, $"Tender #{tenderId} has no nominated bid.");

        try
        {
            var voteRes = await topCommitteeVoting.SubmitAsync(principal, new CommitteeVoteInput
            {
                TenderId = tenderId,
                ExpectedNominatedBidId = nominatedBidId.Value,
                Decision = decision,
                Notes = notes,
                Confirmed = true
            }, ct);

            var awardText = voteRes.IsAwarded
                ? $" All 4 executive approvals are now complete—Tender #{tenderId} has been automatically AWARDED to Offer #{nominatedBidId.Value}!"
                : "";

            return new(
                true,
                true,
                $"Recorded your CEO & Chiefs vote '{decision}' on Tender #{tenderId}.{awardText}",
                $"/TopCommittee/TenderDetails/{tenderId}",
                $"Open Tender #{tenderId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, false, ex.Message);
        }
    }

    // ---------------- SUPPLIER ----------------

    private async Task<AiChatToolExecutionResult> SupplierRespondInvitationAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var profile = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == user.Id && s.Status == "Approved", ct);
        if (profile == null) return new(false, false, "Your supplier profile is not approved.");

        var invId = GetInt(args, "invitationId");
        var tenderId = GetInt(args, "tenderId");
        var rawDecision = (GetString(args, "decision") ?? "").Trim();
        var decision = rawDecision.ToLowerInvariant() switch
        {
            "accept" or "accepted" => "Accepted",
            "decline" or "declined" => "Declined",
            _ => ""
        };
        if (decision == "") return new(false, false, "Specify decision as Accepted or Declined.");

        var inv = await db.Invitations.Include(i => i.Tender)
            .SingleOrDefaultAsync(i =>
                i.SupplierProfileId == profile.SupplierProfileId &&
                ((invId.HasValue && i.InvitationId == invId.Value) || (!invId.HasValue && tenderId.HasValue && i.TenderId == tenderId.Value)), ct);

        if (inv == null) return new(false, false, "Invitation was not found for your company.");
        if (inv.Status != "Pending" || !inv.Tender.IsAcceptingOffers(DateTime.UtcNow))
            return new(false, false, $"Invitation #{inv.InvitationId} is '{inv.Status}' and can no longer be answered.");

        inv.Status = decision;
        inv.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new(
            true,
            true,
            $"{decision} Invitation #{inv.InvitationId} for Tender #{inv.TenderId} ('{inv.Tender.Title}').",
            $"/Supplier/Invitation/{inv.InvitationId}",
            $"Open Invitation #{inv.InvitationId}");
    }

    private async Task<AiChatToolExecutionResult> SupplierUpsertOfferAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var profile = await db.SupplierProfiles.SingleOrDefaultAsync(s => s.UserId == user.Id && s.Status == "Approved", ct);
        if (profile == null) return new(false, false, "Your supplier profile is not approved.");

        var invId = GetInt(args, "invitationId");
        var tenderId = GetInt(args, "tenderId");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inv = await db.Invitations
            .Include(i => i.Tender).ThenInclude(t => t.TenderDocuments)
            .Include(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(i =>
                i.SupplierProfileId == profile.SupplierProfileId &&
                ((invId.HasValue && i.InvitationId == invId.Value) || (!invId.HasValue && tenderId.HasValue && i.TenderId == tenderId.Value)), ct);

        if (inv == null) return new(false, false, "Matching tender invitation was not found for your company.");
        var now = DateTime.UtcNow;
        if (!inv.Tender.IsAcceptingOffers(now))
            return new(false, false, $"Tender #{inv.TenderId} ('{inv.Tender.Title}') is no longer accepting offers.");

        if (inv.Status == "Declined")
            return new(false, false, $"Invitation #{inv.InvitationId} was declined.");
        if (inv.Status == "Pending")
        {
            inv.Status = "Accepted";
            inv.RespondedAt = now;
        }

        var existingBid = await db.Bids.Include(b => b.BidItems).Include(b => b.BidDocuments)
            .SingleOrDefaultAsync(b => b.InvitationId == inv.InvitationId, ct);
        var extended = await db.Set<TenderOfferExtension>().AnyAsync(e => e.TenderId == inv.TenderId, ct);

        if (existingBid != null && existingBid.Status != "Draft" && !(extended && existingBid.Status is "NonCompliant" or "Withdrawn"))
            return new(false, false, $"Offer #{existingBid.BidId} is already '{existingBid.Status}'. Withdraw it first if you wish to replace it before the deadline.");

        var wantSubmit = GetBool(args, "submit") ?? true;
        var mandatoryDocs = inv.Tender.TenderDocuments.Count(d => d.IsMandatory);
        var uploadedDocs = existingBid?.BidDocuments.Count ?? 0;
        var forcedDraftForDocs = wantSubmit && mandatoryDocs > uploadedDocs;
        if (forcedDraftForDocs) wantSubmit = false;

        var deliveryDays = Math.Max(0, GetInt(args, "deliveryDays") ?? existingBid?.DeliveryDays ?? 14);
        var paymentTerms = TrimTo(GetString(args, "paymentTerms"), 1000) ?? existingBid?.PaymentTerms ?? "30 days net after QC release";
        var technicalDetails = TrimTo(GetString(args, "technicalDetails"), 4000) ?? existingBid?.TechnicalDetails ?? "Conforms to tender pharmacopoeial and packaging specifications; batch CoA supplied.";
        var taxPct = Math.Clamp(GetDecimal(args, "taxPercentage") ?? 4m, 0m, 100m);
        var extraCosts = Math.Max(0m, GetDecimal(args, "additionalCosts") ?? existingBid?.AdditionalCosts ?? 0m);
        var fallbackPrice = Math.Max(1m, GetDecimal(args, "defaultUnitPrice") ?? 15.00m);

        var itemOverrides = new Dictionary<int, (decimal UnitPrice, string? Specs)>();
        if (args.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                var pId = GetInt(el, "purchaseRequestItemId") ?? 0;
                var uPrice = GetDecimal(el, "unitPrice");
                if (pId > 0 && uPrice is > 0)
                    itemOverrides[pId] = (decimal.Round(uPrice.Value, 2, MidpointRounding.AwayFromZero), TrimTo(GetString(el, "specifications"), 4000));
            }
        }

        var reqItems = inv.Tender.PurchaseRequest.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).ToList();
        var bid = existingBid ?? new Bid
        {
            InvitationId = inv.InvitationId,
            TenderId = inv.TenderId,
            CreatedAt = now,
            Status = "Draft"
        };

        if (existingBid != null)
        {
            db.BidItems.RemoveRange(bid.BidItems);
            bid.BidItems.Clear();
        }
        else
        {
            db.Bids.Add(bid);
        }

        decimal subtotal = 0m;
        for (var idx = 0; idx < reqItems.Count; idx++)
        {
            var reqItem = reqItems[idx];
            var unitPrice = itemOverrides.TryGetValue(reqItem.PurchaseRequestItemId, out var ov)
                ? ov.UnitPrice
                : fallbackPrice;
            var specs = itemOverrides.TryGetValue(reqItem.PurchaseRequestItemId, out var ovSpec) && !string.IsNullOrWhiteSpace(ovSpec.Specs)
                ? ovSpec.Specs!
                : reqItem.Specifications;

            subtotal += reqItem.Quantity * unitPrice;
            bid.BidItems.Add(new BidItem
            {
                PurchaseRequestItemId = reqItem.PurchaseRequestItemId,
                Quantity = reqItem.Quantity,
                UnitPrice = unitPrice,
                OfferedSpecifications = specs
            });
        }

        bid.Currency = "JOD";
        bid.DeliveryDays = deliveryDays;
        bid.PaymentTerms = paymentTerms;
        bid.TechnicalDetails = technicalDetails;
        bid.TaxAmount = decimal.Round(subtotal * taxPct / 100m, 2, MidpointRounding.AwayFromZero);
        bid.AdditionalCosts = extraCosts;

        if (wantSubmit)
        {
            bid.Status = "Submitted";
            bid.SubmittedAt = now;
            bid.ComplianceStatus = null;
            bid.ComplianceNotes = null;
            bid.ComplianceReviewedAt = null;
            bid.ComplianceReviewedByUserId = null;
            bid.OfficerOpenedAt = null;
        }
        else
        {
            bid.Status = "Draft";
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var total = subtotal + bid.TaxAmount + bid.AdditionalCosts;
        var docNote = forcedDraftForDocs
            ? " Saved as Draft because this tender requires mandatory supporting file uploads before final submission."
            : "";

        return new(
            true,
            true,
            $"{(wantSubmit ? "Submitted" : "Saved Draft")} Offer #{bid.BidId} on Tender #{inv.TenderId} ('{inv.Tender.Title}') totaling {total:N2} JOD (subtotal {subtotal:N2} + tax {bid.TaxAmount:N2} + extra {bid.AdditionalCosts:N2}, {bid.DeliveryDays}-day delivery).{docNote}",
            $"/Supplier/Offer/{bid.BidId}",
            $"Open Offer #{bid.BidId}");
    }

    private async Task<AiChatToolExecutionResult> SupplierWithdrawOfferAsync(ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var bidId = GetInt(args, "bidId") ?? 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bid = await db.Bids.Include(b => b.Invitation).ThenInclude(i => i.Tender)
            .SingleOrDefaultAsync(b => b.BidId == bidId && b.Invitation.SupplierProfile.UserId == user.Id, ct);
        if (bid == null)
            return new(false, false, $"Offer #{bidId} was not found for your company.");
        if (!bid.Invitation.Tender.IsAcceptingOffers(DateTime.UtcNow) || bid.SubmittedAt == null ||
            bid.Status is not ("Submitted" or "UnderReview" or "Compliant" or "NonCompliant"))
            return new(false, false, $"Offer #{bidId} cannot be withdrawn (status '{bid.Status}', tender closes {bid.Invitation.Tender.ClosesAt:yyyy-MM-dd HH:mm} UTC).");

        bid.Status = "Withdrawn";
        bid.ComplianceStatus = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(true, true, $"Withdrew Offer #{bid.BidId} on Tender #{bid.TenderId}.", $"/Supplier/Offer/{bid.BidId}", $"Open Offer #{bid.BidId}");
    }

    // ---------------- ADMIN ----------------

    private async Task<AiChatToolExecutionResult> AdminManageDepartmentAsync(JsonElement args, CancellationToken ct)
    {
        var deptId = GetInt(args, "departmentId");
        var name = TrimTo(GetString(args, "name"), 100);
        var isActive = GetBool(args, "isActive");

        if (deptId.HasValue && deptId.Value > 0)
        {
            var dept = await db.Departments.FindAsync([deptId.Value], ct);
            if (dept == null) return new(false, false, $"Department #{deptId.Value} was not found.");
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (await db.Departments.AnyAsync(d => d.Name == name && d.DepartmentId != dept.DepartmentId, ct))
                    return new(false, false, $"Another department named '{name}' already exists.");
                dept.Name = name;
            }
            if (isActive.HasValue) dept.IsActive = isActive.Value;
            await db.SaveChangesAsync(ct);
            return new(true, true, $"Updated Department #{dept.DepartmentId} ('{dept.Name}', Active: {dept.IsActive}).", "/Admin/Departments", "Open Departments");
        }

        if (string.IsNullOrWhiteSpace(name))
            return new(false, false, "Provide a department name (1–100 characters).");

        var existing = await db.Departments.SingleOrDefaultAsync(d => d.Name == name, ct);
        if (existing != null)
        {
            if (isActive.HasValue && existing.IsActive != isActive.Value)
            {
                existing.IsActive = isActive.Value;
                await db.SaveChangesAsync(ct);
                return new(true, true, $"Updated Department #{existing.DepartmentId} ('{existing.Name}', Active: {existing.IsActive}).", "/Admin/Departments", "Open Departments");
            }
            return new(false, false, $"Department '{name}' already exists (#{existing.DepartmentId}).");
        }

        var created = new Department { Name = name, IsActive = isActive ?? true };
        db.Departments.Add(created);
        await db.SaveChangesAsync(ct);
        return new(true, true, $"Created Department #{created.DepartmentId} ('{created.Name}', Active: {created.IsActive}).", "/Admin/Departments", "Open Departments");
    }

    private async Task<AiChatToolExecutionResult> AdminReviewSupplierAsync(ApplicationUser admin, JsonElement args, CancellationToken ct)
    {
        var id = GetInt(args, "supplierProfileId");
        var company = GetString(args, "companyName")?.Trim();
        var rawStatus = (GetString(args, "status") ?? "").Trim();
        var notes = TrimTo(GetString(args, "notes"), 2000) ?? "Updated via Tiryaq Admin Assistant.";

        var validStatuses = new[] { "Approved", "ChangesRequested", "Rejected", "Suspended", "Pending" };
        var status = validStatuses.FirstOrDefault(s => string.Equals(s, rawStatus, StringComparison.OrdinalIgnoreCase));
        if (status == null)
            return new(false, false, "Choose a valid supplier status: Approved, ChangesRequested, Rejected, or Suspended.");

        var supplier = await db.SupplierProfiles.FirstOrDefaultAsync(s =>
            (id.HasValue && s.SupplierProfileId == id.Value) ||
            (!id.HasValue && company != null && s.CompanyName.Contains(company)), ct);
        if (supplier == null)
            return new(false, false, "Matching supplier profile was not found.");

        supplier.Status = status;
        supplier.ReviewNotes = notes;
        supplier.ReviewedAt = DateTime.UtcNow;
        supplier.ReviewedByUserId = admin.Id;
        await db.SaveChangesAsync(ct);

        return new(
            true,
            true,
            $"Updated Supplier #{supplier.SupplierProfileId} ('{supplier.CompanyName}') to status '{supplier.Status}'.",
            $"/Admin/SupplierDetails/{supplier.SupplierProfileId}",
            $"Open Supplier #{supplier.SupplierProfileId}");
    }

    private async Task<AiChatToolExecutionResult> AdminUpdateUserAsync(ApplicationUser admin, JsonElement args, CancellationToken ct)
    {
        var email = GetString(args, "email")?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return new(false, false, "Provide the user's email address.");

        var target = await users.FindByEmailAsync(email);
        if (target == null)
            return new(false, false, $"No user account found with email '{email}'.");

        var isActive = GetBool(args, "isActive");
        if (target.Id == admin.Id && isActive == false)
            return new(false, false, "Your own Administrator account must remain active.");

        if (isActive == true && !target.IsActive)
        {
            var seatRoles = (await users.GetRolesAsync(target))
                .Intersect(RoleNames.EvaluationPositions.Concat(RoleNames.TopPositions));
            foreach (var seat in seatRoles)
            {
                var holder = await (from u in db.Users
                                    join ur in db.UserRoles on u.Id equals ur.UserId
                                    join r in db.Roles on ur.RoleId equals r.Id
                                    where u.IsActive && u.Id != target.Id && r.Name == seat
                                    select u.FullName).FirstOrDefaultAsync(ct);
                if (holder != null)
                    return new(false, false, $"Cannot activate {email}: committee position {seat} is already held by active user {holder}.");
            }
        }

        var fullName = TrimTo(GetString(args, "fullName"), 150);
        var phone = TrimTo(GetString(args, "phoneNumber"), 40);
        var deptId = GetInt(args, "departmentId");

        if (!string.IsNullOrWhiteSpace(fullName)) target.FullName = fullName;
        if (phone != null) target.PhoneNumber = phone;
        if (isActive.HasValue) target.IsActive = isActive.Value;
        if (deptId.HasValue && await users.IsInRoleAsync(target, RoleNames.Department))
        {
            if (!await db.Departments.AnyAsync(d => d.DepartmentId == deptId.Value && d.IsActive, ct))
                return new(false, false, $"Department #{deptId.Value} does not exist or is inactive.");
            target.DepartmentId = deptId.Value;
        }

        var res = await users.UpdateAsync(target);
        if (!res.Succeeded)
            return new(false, false, string.Join("; ", res.Errors.Select(e => e.Description)));

        return new(true, true, $"Updated user '{target.Email}' (Name: {target.FullName}, Active: {target.IsActive}).", "/Admin/Users", "Open Users");
    }

    private async Task<AiChatToolExecutionResult> AdminMarkContactMessagesAsync(ApplicationUser admin, JsonElement args, CancellationToken ct)
    {
        var messageId = GetInt(args, "contactMessageId");
        var markAllRead = GetBool(args, "markAllRead") ?? false;
        var targetStatus = string.Equals(GetString(args, "status"), ContactMessage.Unread, StringComparison.OrdinalIgnoreCase)
            ? ContactMessage.Unread
            : ContactMessage.Read;

        if (markAllRead)
        {
            var count = await db.ContactMessages
                .Where(m => m.Status == ContactMessage.Unread)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, ContactMessage.Read), ct);
            return new(true, count > 0, $"Marked {count} unread contact message(s) as Read.", "/Admin/ContactMessages", "Open Contact Messages");
        }

        if (!messageId.HasValue || messageId.Value <= 0)
            return new(false, false, "Specify contactMessageId or set markAllRead=true.");

        var msg = await db.ContactMessages.FindAsync([messageId.Value], ct);
        if (msg == null) return new(false, false, $"Contact Message #{messageId.Value} was not found.");
        msg.Status = targetStatus;
        await db.SaveChangesAsync(ct);

        return new(true, true, $"Marked Contact Message #{msg.ContactMessageId} ('{msg.Subject}') as {msg.Status}.", $"/Admin/ContactMessageDetails/{msg.ContactMessageId}", $"Open Message #{msg.ContactMessageId}");
    }

    // ---------------- PARSING HELPERS ----------------

    private static List<PurchaseRequestItem> ParseRequestItems(JsonElement args)
    {
        var result = new List<PurchaseRequestItem>();
        if (!args.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var el in arr.EnumerateArray())
        {
            var name = TrimTo(GetString(el, "itemName") ?? GetString(el, "name"), 200);
            var qty = GetDecimal(el, "quantity") ?? 1m;
            var unit = TrimTo(GetString(el, "unit"), 30) ?? "units";
            var specs = TrimTo(GetString(el, "specifications") ?? GetString(el, "specs"), 4000) ?? "Standard pharmacopoeial grade";
            if (string.IsNullOrWhiteSpace(name) || qty <= 0) continue;
            result.Add(new PurchaseRequestItem
            {
                ItemName = name,
                Quantity = decimal.Round(qty, 3, MidpointRounding.AwayFromZero),
                Unit = unit,
                Specifications = specs,
                LegalWarrantyMonths = GetInt(el, "legalWarrantyMonths") is int months and >= 1 and <= 120 ? months : null
            });
        }
        return result;
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    private static int? GetInt(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static decimal? GetDecimal(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static bool? GetBool(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
            _ => null
        };
    }

    private static DateOnly? ParseDateOnly(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && DateOnly.TryParse(raw.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string NormalizeChoice(string? raw, string[] allowed, string fallback) =>
        allowed.FirstOrDefault(a => string.Equals(a, raw?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;

    private static string? TrimTo(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        return t.Length <= max ? t : t[..max].TrimEnd();
    }
}
