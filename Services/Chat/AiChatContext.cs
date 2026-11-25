using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

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
