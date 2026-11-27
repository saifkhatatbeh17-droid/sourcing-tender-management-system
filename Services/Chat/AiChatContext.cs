using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Services.Chat;

// Collects the facts Safi may use for one user (their requests, tenders, offers...), limited to what their role may see.
public sealed record AiChatWorkspaceSnapshot(
    object UserProfile,
    object WorkspaceData,
    object? CurrentRecord,
    string? CurrentRecordUrl,
    string? CurrentRecordLabel);

public sealed class AiChatContext(
    ApplicationDbContext db,
    ITopCommitteeQueries topCommittee,
    ITopCommitteeVotingReadiness votingReadiness)
{
    public async Task<AiChatWorkspaceSnapshot> BuildSnapshotAsync(
        string workspace,
        string? pageAction,
        int? recordId,
        ClaimsPrincipal principal,
        ApplicationUser user,
        CancellationToken ct)
    {
        var assignedRoles = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == user.Id && r.Name != null
            orderby r.Name
            select r.Name!).ToListAsync(ct);

        var departmentName = user.DepartmentId.HasValue
            ? await db.Departments.AsNoTracking()
                .Where(d => d.DepartmentId == user.DepartmentId.Value)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var userProfile = new
        {
            user.FullName,
            user.Email,
            ActiveWorkspace = RoleNames.DisplayName(workspace),
            WorkspaceCode = workspace,
            AssignedRoles = assignedRoles.Select(RoleNames.DisplayName).ToArray(),
            Department = departmentName
        };

        var recordType = recordId is > 0 ? AiChatPage.Record(workspace, pageAction) : null;
        object? currentRecord = null;
        string? currentUrl = null;
        string? currentLabel = null;

        if (recordType.HasValue && recordId is > 0)
        {
            currentRecord = await ReadRecordDetailAsync(recordType.Value, recordId.Value, principal, user.Id, user.DepartmentId, ct);
            if (currentRecord != null)
            {
                (currentUrl, currentLabel) = ResolveRecordLink(workspace, pageAction, recordType.Value, recordId.Value);
            }
        }

        var workspaceData = await BuildRoleWorkspaceDataAsync(workspace, principal, user, recordType, recordId, ct);
        return new AiChatWorkspaceSnapshot(userProfile, workspaceData, currentRecord, currentUrl, currentLabel);
    }

    private static (string Url, string Label) ResolveRecordLink(string workspace, string? pageAction, AiChatRecord recordType, int id) =>
        recordType switch
        {
            AiChatRecord.DepartmentRequest => ($"/Department/Details/{id}", $"Open Request #{id}"),
            AiChatRecord.OfficerRequest => ($"/SourcingOfficer/Requests?id={id}", $"Open Request #{id}"),
            AiChatRecord.OfficerTender => ($"/SourcingOfficer/Offers?tenderId={id}", $"Open Tender #{id}"),
            AiChatRecord.EvaluationTender => ($"/EvaluationCommittee/{(pageAction == "Reports" ? "Reports" : "Evaluations")}/{id}", $"Open Tender #{id}"),
            AiChatRecord.ManagerReport => ($"/SourcingManager/Submissions?id={id}", $"Open Submission #{id}"),
            AiChatRecord.TopTender => ($"/TopCommittee/TenderDetails/{id}", $"Open Tender #{id}"),
            AiChatRecord.SupplierInvitation => ($"/Supplier/Invitation/{id}", $"Open Invitation #{id}"),
            AiChatRecord.SupplierOffer => ($"/Supplier/Offer/{id}", $"Open Offer #{id}"),
            AiChatRecord.SupplierApplication => ($"/Admin/SupplierDetails/{id}", $"Open Supplier #{id}"),
            AiChatRecord.ContactMessage => ($"/Admin/ContactMessageDetails/{id}", $"Open Message #{id}"),
            _ => ($"/{workspace}/Dashboard", "Open workspace")
        };

    private async Task<object> BuildRoleWorkspaceDataAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        AiChatRecord? currentRecordType,
        int? currentRecordId,
        CancellationToken ct)
    {
        switch (workspace)
        {
            case RoleNames.Department:
            {
                if (!user.DepartmentId.HasValue)
                    return new { Error = "No department assigned to this account." };

                var baseQuery = db.PurchaseRequests.AsNoTracking().InDepartment(user.DepartmentId);
                var statusCounts = await baseQuery
                    .GroupBy(r => r.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

                var recent = await baseQuery
                    .AsSplitQuery()
                    .Include(r => r.PurchaseRequestItems)
                    .Include(r => r.Tenders)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(8)
                    .ToListAsync(ct);

                return new
                {
                    TotalRequests = statusCounts.Values.Sum(),
                    StatusCounts = statusCounts,
                    RecentRequests = recent.Select(r => new
                    {
                        r.PurchaseRequestId,
                        r.Title,
                        r.Status,
                        r.Priority,
                        TargetDate = r.TargetDate.ToString("yyyy-MM-dd"),
                        ReturnOrReviewNotes = Cap(r.ReviewNotes, 160),
                        CanEditOrResubmit = r.RequestedByUserId == user.Id && (r.Status == "Returned" || r.Status == "Submitted"),
                        LinkedTenderStatuses = r.Tenders.Select(t => new { t.TenderId, t.Status }).ToArray(),
                        Items = r.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).Select(i => new
                        {
                            i.PurchaseRequestItemId,
                            i.ItemName,
                            i.Quantity,
                            i.Unit
                        }).ToArray()
                    }).ToArray()
                };
            }

            case RoleNames.SourcingOfficer:
            {
                var reviewable = await db.PurchaseRequests.AsNoTracking()
                    .ReviewableBy(user.Id)
                    .AsSplitQuery()
                    .Include(r => r.Department)
                    .Include(r => r.PurchaseRequestItems)
                    .Include(r => r.Tenders)
                    .OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt)
                    .Take(8)
                    .ToListAsync(ct);

                var ownTenders = await db.Tenders.AsNoTracking()
                    .Where(t => t.CreatedByUserId == user.Id)
                    .AsSplitQuery()
                    .Include(t => t.OfferExtensions)
                    .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
                    .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems)
                    .Include(t => t.EvaluationReport)
                    .OrderByDescending(t => t.Status == "Published" || t.Status == "Closed" || t.Status == "UnderEvaluation")
                    .ThenByDescending(t => t.CreatedAt)
                    .Take(8)
                    .ToListAsync(ct);

                var approvedSuppliers = await db.SupplierProfiles.AsNoTracking()
                    .Where(s => s.Status == "Approved")
                    .OrderBy(s => s.SupplierProfileId)
                    .Take(15)
                    .Select(s => new { s.SupplierProfileId, s.CompanyName, s.Category })
                    .ToListAsync(ct);

                var now = DateTime.UtcNow;
                var activeOfferListCount = 0;
                return new
                {
                    ApprovedSuppliers = approvedSuppliers,
                    SupportedCategories = ProcurementCategoryInput.Options,
                    ReviewableRequests = reviewable.Select(r => new
                    {
                        r.PurchaseRequestId,
                        r.Title,
                        Department = r.Department.Name,
                        r.Status,
                        r.Priority,
                        TargetDate = r.TargetDate.ToString("yyyy-MM-dd"),
                        HasTender = r.Tenders.Count > 0,
                        Items = r.PurchaseRequestItems.Select(i => $"{i.ItemName} ({i.Quantity:0.##} {i.Unit})").ToArray()
                    }).ToArray(),
                    Tenders = ownTenders.Select(t =>
                    {
                        var submittedBids = t.Invitations.SelectMany(i => i.Bids)
                            .Where(b => b.SubmittedAt != null && b.Status != "Withdrawn")
                            .OrderBy(b => b.BidId)
                            .ToList();
                        var isCurrent = currentRecordType == AiChatRecord.OfficerTender && currentRecordId == t.TenderId;
                        var includeOffers = !isCurrent && t.Status is "Published" or "Closed" && activeOfferListCount < 4;
                        if (includeOffers) activeOfferListCount++;
                        return new
                        {
                            t.TenderId,
                            t.PurchaseRequestId,
                            t.Title,
                            t.Category,
                            t.Status,
                            t.ClosesAt,
                            DeadlinePassed = t.ClosesAt <= now,
                            Round = 1 + t.OfferExtensions.Count,
                            InvitedCount = t.Invitations.Count,
                            SubmittedOffersCount = submittedBids.Count,
                            CompliantSuppliersCount = submittedBids
                                .Where(b => b.ComplianceStatus == "Compliant")
                                .Select(b => b.Invitation.SupplierProfileId)
                                .Distinct()
                                .Count(),
                            Report = t.EvaluationReport == null ? null : new
                            {
                                t.EvaluationReport.EvaluationReportId,
                                t.EvaluationReport.Status,
                                t.EvaluationReport.RecommendedBidId
                            },
                            Offers = includeOffers ? submittedBids.Select(b => new
                            {
                                b.BidId,
                                Supplier = b.Invitation.SupplierProfile.CompanyName,
                                b.Status,
                                b.ComplianceStatus,
                                Total = CommitteeEvaluationPage.Total(b),
                                b.Currency,
                                b.DeliveryDays
                            }).ToArray() : null
                        };
                    }).ToArray()
                };
            }

            case RoleNames.EvaluationCommittee:
            {
                var baseEvalQuery = db.Tenders.AsNoTracking().InEvaluationWorkspace(user.Id);
                var totalEvalTendersCount = await baseEvalQuery.CountAsync(ct);
                var tenders = await baseEvalQuery
                    .AsSplitQuery()
                    .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
                    .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
                    .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant")).ThenInclude(b => b.BidItems)
                    .Include(t => t.CommitteeWeightProposals)
                    .Include(t => t.CommitteeTieVotes)
                    .Include(t => t.EvaluationCriteria)
                    .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
                    .Include(t => t.TenderApprovals)
                    .Include(t => t.EvaluationReport)
                    .OrderByDescending(t => t.Status == "UnderEvaluation")
                    .ThenByDescending(t => t.CreatedAt)
                    .Take(8)
                    .ToListAsync(ct);

                var tenderIds = tenders.Select(t => t.TenderId).ToArray();
                var allEvaluations = await db.Evaluations.AsNoTracking()
                    .Where(e => tenderIds.Contains(e.TenderId))
                    .Include(e => e.EvaluationScores)
                    .ToListAsync(ct);

                var expandedOffersCount = 0;
                return new
                {
                    TendersInWorkspaceCount = totalEvalTendersCount,
                    Tenders = tenders.Select(t =>
                    {
                        var page = new CommitteeEvaluationPage
                        {
                            Tender = t,
                            UserId = user.Id,
                            Evaluations = allEvaluations.Where(e => e.TenderId == t.TenderId).ToList()
                        };
                        var me = page.Members.SingleOrDefault(m => m.UserId == user.Id);
                        var myWeight = me == null ? null : page.WeightProposals.SingleOrDefault(p => p.CommitteeAssignmentId == me.CommitteeAssignmentId)?.FinancialWeight;
                        var ranking = AllOfferScoring.Ranking(page);
                        var isCurrent = currentRecordType == AiChatRecord.EvaluationTender && currentRecordId == t.TenderId;
                        var includeOffers = !isCurrent && t.Status == "UnderEvaluation" && expandedOffersCount < 4;
                        if (includeOffers) expandedOffersCount++;
                        var tiedLeaders = AllOfferScoring.Leaders(page);

                        return new
                        {
                            t.TenderId,
                            t.Title,
                            t.Status,
                            StatusDisplay = CommitteeEvaluationPage.StatusLabel(t.Status),
                            MySeatPosition = me?.Position,
                            MySeatTitle = me == null ? "Unassigned" : CommitteeEvaluationPage.MemberName(me.Position),
                            CanWriteFinalReport = page.IsDepartmentManager,
                            WeightsConfirmedCount = page.WeightProposals.Count,
                            ConsensusLocked = page.ConsensusLocked,
                            FinancialWeightCap = page.FinancialWeight,
                            TechnicalWeightCap = page.TechnicalWeight,
                            MyConfirmedFinancialWeight = myWeight,
                            AllScoringComplete = AllOfferScoring.Complete(page),
                            WinningBidId = AllOfferScoring.Winner(page),
                            TiedLeaderBidIds = tiedLeaders.Count > 1 ? tiedLeaders.Select(l => l.BidId).ToArray() : null,
                            ReportStatus = t.EvaluationReport?.Status,
                            RecommendedBidId = t.EvaluationReport?.RecommendedBidId,
                            CompliantOffersCount = page.Offers.Count,
                            CompliantOffers = includeOffers ? page.Offers.Select(b =>
                            {
                                var myEval = me == null ? null : page.Evaluation(me, b);
                                var rankRow = ranking.SingleOrDefault(r => r.BidId == b.BidId);
                                return new
                                {
                                    b.BidId,
                                    Supplier = b.Invitation.SupplierProfile.CompanyName,
                                    Total = CommitteeEvaluationPage.Total(b),
                                    b.Currency,
                                    b.DeliveryDays,
                                    ReviewsCompletedCount = AllOfferScoring.Count(page, b),
                                    MyReviewApproved = page.MyReviewApproved(b),
                                    MyFinancialMark = myEval?.FinancialMark,
                                    MyTechnicalMark = myEval?.TechnicalMark,
                                    AverageOverallScore = rankRow?.Score
                                };
                            }).ToArray() : null
                        };
                    }).ToArray()
                };
            }

            case RoleNames.SourcingManager:
            {
                var reports = await db.EvaluationReports.AsNoTracking()
                    .InManagerReview()
                    .AsSplitQuery()
                    .Include(r => r.Tender)
                    .Include(r => r.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
                    .Include(r => r.Bid).ThenInclude(b => b!.BidItems)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ThenByDescending(r => r.EvaluationReportId)
                    .Take(12)
                    .ToListAsync(ct);

                var withTopCommittee = await db.Tenders.AsNoTracking()
                    .Where(t => t.Status == "AwaitingCommitteeDecision")
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new { t.TenderId, t.Title, t.Status })
                    .Take(8)
                    .ToListAsync(ct);

                var awarded = await db.Tenders.AsNoTracking()
                    .Where(t => t.Status == "Awarded")
                    .Include(t => t.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
                    .OrderByDescending(t => t.AwardedAt)
                    .Take(6)
                    .Select(t => new
                    {
                        t.TenderId,
                        t.Title,
                        t.AwardedBidId,
                        Winner = t.Bid == null ? null : t.Bid.Invitation.SupplierProfile.CompanyName,
                        t.AwardedAt
                    })
                    .ToListAsync(ct);

                return new
                {
                    SubmissionsInManagerQueue = reports.Select(r => new
                    {
                        r.EvaluationReportId,
                        r.TenderId,
                        TenderTitle = r.Tender.Title,
                        TenderStatus = r.Tender.Status,
                        ReportStatus = r.Status,
                        r.RecommendedBidId,
                        RecommendedSupplier = r.Bid?.Invitation.SupplierProfile.CompanyName,
                        WinningTotal = r.Bid == null ? (decimal?)null : CommitteeEvaluationPage.Total(r.Bid),
                        Currency = r.Bid?.Currency,
                        CanDecide = r.Status is "OfficerSubmitted" or "CommitteeApproved",
                        CanForwardToCeoAndChiefs = r.Status == "ManagerApproved" && r.Tender.Status == "AwaitingManagerReview",
                        r.SubmittedAt
                    }).ToArray(),
                    WithCeoAndChiefs = withTopCommittee,
                    RecentlyAwarded = awarded
                };
            }

            case RoleNames.TopCommittee:
            {
                var dashboard = await topCommittee.GetDashboardAsync(principal, ct);
                var pendingDetails = new List<object>();
                foreach (var row in dashboard.PendingTenders)
                {
                    try
                    {
                        var detail = await topCommittee.GetTenderDetailsAsync(principal, row.TenderId, ct);
                        var dbReady = await votingReadiness.IsDatabaseReadyAsync(principal, row.TenderId, ct);
                        var nominated = detail.Bids.FirstOrDefault(b => b.IsNominated);
                        pendingDetails.Add(new
                        {
                            detail.TenderId,
                            detail.Title,
                            detail.Status,
                            NominatedBidId = nominated?.BidId ?? row.NominatedBidId,
                            NominatedSupplier = nominated?.SupplierName ?? row.NominatedSupplier,
                            NominatedAmount = nominated?.Total,
                            Currency = nominated?.Currency,
                            ApprovalsRecorded = row.RecordedApprovals,
                            CanCurrentMemberVote = detail.Readiness.CanCurrentMemberVote && dbReady,
                            BlockingReason = detail.Readiness.BlockingReason,
                            detail.OpenClarifications
                        });
                    }
                    catch
                    {
                        // Skip inaccessible tender row safely
                    }
                }

                return new
                {
                    Summary = new
                    {
                        dashboard.AssignedTenders,
                        dashboard.AwaitingDecision,
                        dashboard.AwardedTenders,
                        dashboard.RejectedTenders,
                        dashboard.OpenClarifications
                    },
                    PendingVotes = pendingDetails,
                    RecentDecisions = dashboard.RecentDecisions
                };
            }

            case RoleNames.Supplier:
            {
                var profile = await db.SupplierProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(s => s.UserId == user.Id && s.Status == "Approved", ct);
                if (profile == null)
                    return new { Error = "Supplier profile is not approved." };

                var now = DateTime.UtcNow;
                var invitations = await db.Invitations.AsNoTracking()
                    .Where(i => i.SupplierProfileId == profile.SupplierProfileId)
                    .AsSplitQuery()
                    .Include(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
                    .Include(i => i.Tender).ThenInclude(t => t.TenderDocuments)
                    .Include(i => i.Bids).ThenInclude(b => b.BidItems)
                    .OrderByDescending(i => i.SentAt)
                    .Take(8)
                    .ToListAsync(ct);

                return new
                {
                    Company = new { profile.SupplierProfileId, profile.CompanyName, profile.Category, profile.Status },
                    ConfidentialityRule = "Competitor identities, competitor bids, and internal committee marks are strictly hidden from Supplier accounts.",
                    Invitations = invitations.Select(i =>
                    {
                        var myBid = i.Bids.OrderByDescending(b => b.BidId).FirstOrDefault();
                        var isCurrent = currentRecordType == AiChatRecord.SupplierInvitation && currentRecordId == i.InvitationId;
                        return new
                        {
                            i.InvitationId,
                            i.TenderId,
                            TenderTitle = i.Tender.Title,
                            i.Tender.Category,
                            InvitationStatus = i.Status,
                            TenderStatus = i.Tender.Status,
                            i.Tender.ClosesAt,
                            AcceptingOffersNow = i.Tender.IsAcceptingOffers(now),
                            MandatoryTenderDocumentsCount = i.Tender.TenderDocuments.Count(d => d.IsMandatory),
                            RequestedItems = isCurrent ? null : i.Tender.PurchaseRequest.PurchaseRequestItems
                                .OrderBy(x => x.PurchaseRequestItemId)
                                .Select(x => new
                                {
                                    x.PurchaseRequestItemId,
                                    x.ItemName,
                                    x.Quantity,
                                    x.Unit
                                }).ToArray(),
                            MyOffer = myBid == null ? null : new
                            {
                                myBid.BidId,
                                myBid.Status,
                                myBid.ComplianceStatus,
                                Subtotal = myBid.BidItems.Sum(x => x.Quantity * x.UnitPrice),
                                myBid.TaxAmount,
                                myBid.AdditionalCosts,
                                Total = CommitteeEvaluationPage.Total(myBid),
                                myBid.Currency,
                                myBid.DeliveryDays,
                                myBid.SubmittedAt
                            }
                        };
                    }).ToArray()
                };
            }

            case RoleNames.Admin:
            {
                var departments = await db.Departments.AsNoTracking()
                    .OrderBy(d => d.DepartmentId)
                    .Select(d => new { d.DepartmentId, d.Name, d.IsActive })
                    .ToListAsync(ct);

                var totalSuppliersCount = await db.SupplierProfiles.AsNoTracking().CountAsync(ct);
                var pendingSuppliersCount = await db.SupplierProfiles.AsNoTracking().CountAsync(s => s.Status == "Pending", ct);
                var suppliers = await db.SupplierProfiles.AsNoTracking()
                    .OrderByDescending(s => s.Status == "Pending" || s.Status == "ChangesRequested")
                    .ThenByDescending(s => s.SupplierProfileId)
                    .Take(12)
                    .Select(s => new { s.SupplierProfileId, s.CompanyName, s.Category, s.Status })
                    .ToListAsync(ct);

                var roleLinks = from ur in db.UserRoles.AsNoTracking()
                                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                                where r.Name != null
                                select new { ur.UserId, RoleName = r.Name! };

                var totalUsersCount = await db.Users.AsNoTracking().CountAsync(ct);
                var accounts = await db.Users.AsNoTracking()
                    .OrderBy(u => u.Email)
                    .Take(15)
                    .Select(u => new
                    {
                        u.Email,
                        u.FullName,
                        u.IsActive,
                        u.DepartmentId,
                        Roles = roleLinks.Where(l => l.UserId == u.Id).Select(l => l.RoleName).ToList()
                    })
                    .ToListAsync(ct);

                var messages = await db.ContactMessages.AsNoTracking()
                    .OrderByDescending(m => m.Status == ContactMessage.Unread)
                    .ThenByDescending(m => m.SubmittedAt)
                    .Take(5)
                    .Select(m => new { m.ContactMessageId, m.FullName, m.Email, m.Subject, m.Status, m.SubmittedAt })
                    .ToListAsync(ct);

                var tenders = await db.Tenders.AsNoTracking()
                    .OrderByDescending(t => t.TenderId)
                    .Take(6)
                    .Select(t => new { t.TenderId, t.Title, t.Category, t.Status, t.ClosesAt })
                    .ToListAsync(ct);

                var requestsCount = await db.PurchaseRequests.AsNoTracking().CountAsync(ct);

                return new
                {
                    Counts = new
                    {
                        Users = totalUsersCount,
                        Departments = departments.Count,
                        Suppliers = totalSuppliersCount,
                        PendingSuppliers = pendingSuppliersCount,
                        PurchaseRequests = requestsCount,
                        Tenders = await db.Tenders.AsNoTracking().CountAsync(ct),
                        UnreadMessages = await db.ContactMessages.AsNoTracking().CountAsync(m => m.Status == ContactMessage.Unread, ct)
                    },
                    Departments = departments,
                    Suppliers = suppliers,
                    Users = accounts,
                    RecentContactMessages = messages,
                    RecentTenders = tenders
                };
            }

            default:
                return new { };
        }
    }

    private async Task<object?> ReadRecordDetailAsync(
        AiChatRecord record,
        int id,
        ClaimsPrincipal user,
        string userId,
        int? departmentId,
        CancellationToken ct)
    {
        switch (record)
        {
            case AiChatRecord.DepartmentRequest when departmentId.HasValue:
                return await db.PurchaseRequests.AsNoTracking()
                    .InDepartment(departmentId)
                    .Where(r => r.PurchaseRequestId == id)
                    .Select(r => new
                    {
                        r.PurchaseRequestId,
                        r.Title,
                        r.Reason,
                        r.Status,
                        r.Priority,
                        TargetDate = r.TargetDate.ToString("yyyy-MM-dd"),
                        r.ReviewNotes,
                        CanRequesterEdit = r.RequestedByUserId == userId && (r.Status == "Returned" || r.Status == "Submitted"),
                        Items = r.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).Select(i => new
                        {
                            i.PurchaseRequestItemId,
                            i.ItemName,
                            i.Quantity,
                            i.Unit,
                            i.Specifications
                        }).ToList()
                    }).SingleOrDefaultAsync(ct);

            case AiChatRecord.OfficerRequest:
                return await db.PurchaseRequests.AsNoTracking()
                    .ReviewableBy(userId)
                    .Where(r => r.PurchaseRequestId == id)
                    .Select(r => new
                    {
                        r.PurchaseRequestId,
                        r.Title,
                        r.Reason,
                        Department = r.Department.Name,
                        r.Status,
                        r.Priority,
                        TargetDate = r.TargetDate.ToString("yyyy-MM-dd"),
                        r.ReviewNotes,
                        Items = r.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).Select(i => new
                        {
                            i.PurchaseRequestItemId,
                            i.ItemName,
                            i.Quantity,
                            i.Unit,
                            i.Specifications
                        }).ToList()
                    }).SingleOrDefaultAsync(ct);

            case AiChatRecord.OfficerTender:
            {
                var tender = await db.Tenders.AsNoTracking()
                    .AsSplitQuery()
                    .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
                    .Include(t => t.OfferExtensions)
                    .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
                    .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems)
                    .Include(t => t.EvaluationReport)
                    .SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId, ct);
                if (tender == null) return null;
                return new
                {
                    tender.TenderId,
                    tender.Title,
                    tender.Category,
                    tender.Description,
                    tender.Terms,
                    tender.Status,
                    tender.OpensAt,
                    tender.ClosesAt,
                    Round = 1 + tender.OfferExtensions.Count,
                    Report = tender.EvaluationReport == null ? null : new
                    {
                        tender.EvaluationReport.EvaluationReportId,
                        tender.EvaluationReport.Status,
                        tender.EvaluationReport.RecommendedBidId,
                        tender.EvaluationReport.CommitteeSummary,
                        tender.EvaluationReport.OfficerSummary
                    },
                    InvitationsAndOffers = tender.Invitations.Select(i => new
                    {
                        i.InvitationId,
                        Supplier = i.SupplierProfile.CompanyName,
                        InvitationStatus = i.Status,
                        Offers = i.Bids.Select(b => new
                        {
                            b.BidId,
                            b.Status,
                            b.ComplianceStatus,
                            b.ComplianceNotes,
                            Total = CommitteeEvaluationPage.Total(b),
                            b.Currency,
                            b.DeliveryDays,
                            b.PaymentTerms
                        }).ToArray()
                    }).ToArray()
                };
            }

            case AiChatRecord.EvaluationTender:
            {
                var tender = await db.Tenders.AsNoTracking()
                    .InEvaluationWorkspace(userId)
                    .AsSplitQuery()
                    .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
                    .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
                    .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant")).ThenInclude(b => b.BidItems)
                    .Include(t => t.CommitteeWeightProposals)
                    .Include(t => t.CommitteeTieVotes)
                    .Include(t => t.EvaluationCriteria)
                    .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
                    .Include(t => t.TenderApprovals)
                    .Include(t => t.EvaluationReport)
                    .SingleOrDefaultAsync(t => t.TenderId == id, ct);
                if (tender == null) return null;
                var evals = await db.Evaluations.AsNoTracking().Where(e => e.TenderId == id).ToListAsync(ct);
                var page = new CommitteeEvaluationPage { Tender = tender, UserId = userId, Evaluations = evals };
                var me = page.Members.SingleOrDefault(m => m.UserId == userId);
                return new
                {
                    tender.TenderId,
                    tender.Title,
                    Description = Cap(tender.Description, 220),
                    Terms = Cap(tender.Terms, 220),
                    tender.Status,
                    page.ConsensusLocked,
                    page.FinancialWeight,
                    page.TechnicalWeight,
                    WeightsConfirmed = page.WeightProposals.Count,
                    ReportStatus = tender.EvaluationReport?.Status,
                    RecommendedBidId = tender.EvaluationReport?.RecommendedBidId,
                    CommitteeSummary = Cap(tender.EvaluationReport?.CommitteeSummary, 400),
                    RequestedItems = tender.PurchaseRequest.PurchaseRequestItems.Take(12).Select(i => new
                    {
                        i.PurchaseRequestItemId,
                        i.ItemName,
                        i.Quantity,
                        i.Unit,
                        Specifications = Cap(i.Specifications, 180)
                    }).ToArray(),
                    Offers = page.Offers.Select(b =>
                    {
                        var myEval = me == null ? null : page.Evaluation(me, b);
                        return new
                        {
                            b.BidId,
                            Supplier = b.Invitation.SupplierProfile.CompanyName,
                            Total = CommitteeEvaluationPage.Total(b),
                            b.Currency,
                            b.DeliveryDays,
                            PaymentTerms = Cap(b.PaymentTerms, 120),
                            TechnicalDetails = Cap(b.TechnicalDetails, 180),
                            Items = b.BidItems.Take(12).Select(bi => new
                            {
                                bi.PurchaseRequestItemId,
                                bi.Quantity,
                                bi.UnitPrice,
                                OfferedSpecifications = Cap(bi.OfferedSpecifications, 160)
                            }).ToArray(),
                            ReviewsCount = AllOfferScoring.Count(page, b),
                            MyFinancialMark = myEval?.FinancialMark,
                            MyTechnicalMark = myEval?.TechnicalMark,
                            MyNotes = Cap(myEval?.Notes, 140)
                        };
                    }).ToArray()
                };
            }

            case AiChatRecord.ManagerReport:
            {
                var report = await db.EvaluationReports.AsNoTracking()
                    .InManagerReview()
                    .Where(r => r.EvaluationReportId == id)
                    .Select(r => new
                    {
                        r.EvaluationReportId,
                        r.TenderId,
                        TenderTitle = r.Tender.Title,
                        TenderStatus = r.Tender.Status,
                        ReportStatus = r.Status,
                        r.RecommendedBidId,
                        RecommendedSupplier = r.Bid == null ? null : r.Bid.Invitation.SupplierProfile.CompanyName,
                        r.CommitteeSummary,
                        r.OfficerSummary,
                        r.ManagerSummary,
                        r.SubmittedAt
                    }).SingleOrDefaultAsync(ct);

                return report == null ? null : new
                {
                    report.EvaluationReportId,
                    report.TenderId,
                    report.TenderTitle,
                    report.TenderStatus,
                    report.ReportStatus,
                    report.RecommendedBidId,
                    report.RecommendedSupplier,
                    CommitteeSummary = Cap(report.CommitteeSummary, 400),
                    OfficerSummary = Cap(report.OfficerSummary, 400),
                    ManagerSummary = Cap(report.ManagerSummary, 400),
                    report.SubmittedAt
                };
            }

            case AiChatRecord.TopTender:
                try
                {
                    var tender = await topCommittee.GetTenderDetailsAsync(user, id, ct);
                    var nominated = tender.Bids.FirstOrDefault(b => b.IsNominated);
                    return new
                    {
                        tender.TenderId,
                        tender.Title,
                        tender.Status,
                        tender.OpensAt,
                        tender.ClosesAt,
                        tender.OpenClarifications,
                        tender.AwardedAt,
                        NominatedOffer = nominated == null ? null : new
                        {
                            nominated.BidId,
                            nominated.SupplierName,
                            nominated.Total,
                            nominated.Currency,
                            nominated.DeliveryDays,
                            nominated.PaymentTerms
                        },
                        Votes = tender.Votes,
                        CanCurrentMemberVote = tender.Readiness.CanCurrentMemberVote &&
                            await votingReadiness.IsDatabaseReadyAsync(user, id, ct),
                        tender.Readiness.BlockingReason
                    };
                }
                catch (CommitteeTenderNotFoundException) { return null; }
                catch (CommitteeAccessDeniedException) { return null; }

            case AiChatRecord.SupplierInvitation:
                return await db.Invitations.AsNoTracking()
                    .Where(i => i.InvitationId == id && i.SupplierProfile.UserId == userId && i.SupplierProfile.Status == "Approved")
                    .Select(i => new
                    {
                        i.InvitationId,
                        i.TenderId,
                        TenderTitle = i.Tender.Title,
                        i.Tender.Description,
                        i.Tender.Terms,
                        InvitationStatus = i.Status,
                        TenderStatus = i.Tender.Status,
                        i.Tender.OpensAt,
                        i.Tender.ClosesAt,
                        Items = i.Tender.PurchaseRequest.PurchaseRequestItems.Select(x => new
                        {
                            x.PurchaseRequestItemId,
                            x.ItemName,
                            x.Quantity,
                            x.Unit,
                            x.Specifications
                        }).ToList()
                    }).SingleOrDefaultAsync(ct);

            case AiChatRecord.SupplierOffer:
            {
                var bid = await db.Bids.AsNoTracking()
                    .AsSplitQuery()
                    .Include(b => b.Invitation).ThenInclude(i => i.Tender)
                    .Include(b => b.BidItems).ThenInclude(bi => bi.PurchaseRequestItem)
                    .SingleOrDefaultAsync(b => b.BidId == id &&
                        b.Invitation.SupplierProfile.UserId == userId &&
                        b.Invitation.SupplierProfile.Status == "Approved", ct);
                if (bid == null) return null;
                return new
                {
                    bid.BidId,
                    bid.InvitationId,
                    bid.TenderId,
                    TenderTitle = bid.Invitation.Tender.Title,
                    OfferStatus = bid.Status,
                    bid.ComplianceStatus,
                    TenderStatus = bid.Invitation.Tender.Status,
                    bid.SubmittedAt,
                    bid.DeliveryDays,
                    bid.PaymentTerms,
                    bid.TechnicalDetails,
                    Subtotal = bid.BidItems.Sum(x => x.Quantity * x.UnitPrice),
                    bid.TaxAmount,
                    bid.AdditionalCosts,
                    Total = CommitteeEvaluationPage.Total(bid),
                    bid.Currency,
                    bid.Invitation.Tender.ClosesAt,
                    Items = bid.BidItems.Select(x => new
                    {
                        x.PurchaseRequestItemId,
                        x.PurchaseRequestItem.ItemName,
                        x.Quantity,
                        x.UnitPrice,
                        x.OfferedSpecifications
                    }).ToArray()
                };
            }

            case AiChatRecord.SupplierApplication:
                return await db.SupplierProfiles.AsNoTracking()
                    .Where(s => s.SupplierProfileId == id)
                    .Select(s => new { s.SupplierProfileId, s.CompanyName, s.RegistrationNumber, s.TaxNumber, s.Status, s.Category, s.ReviewNotes, s.ReviewedAt })
                    .SingleOrDefaultAsync(ct);

            case AiChatRecord.ContactMessage:
                return await db.ContactMessages.AsNoTracking()
                    .Where(m => m.ContactMessageId == id)
                    .Select(m => new { m.ContactMessageId, m.FullName, m.Email, m.Subject, m.Message, m.Status, m.SubmittedAt })
                    .SingleOrDefaultAsync(ct);

            default:
                return null;
        }
    }

    private static string? Cap(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "…";
    }
}
