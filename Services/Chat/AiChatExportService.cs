using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Services.Chat;

// Builds the PDF and Excel files that Safi offers for download.
public sealed record AiChatExportTable(string Title, string[] Headers, IReadOnlyList<string[]> Rows);

public sealed record AiChatExportDocument(
    string Title,
    string Subtitle,
    string FileSlug,
    IReadOnlyList<(string Label, string Value)> Metrics,
    string? ExecutiveSummary,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<AiChatExportTable> Tables);

public sealed partial class AiChatExportService(
    ApplicationDbContext db,
    ITopCommitteeQueries topCommittee,
    AiChatDownloadStore downloads)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AiChatToolExecutionResult> InspectRecordAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        JsonElement args,
        CancellationToken ct)
    {
        var recordType = (GetString(args, "recordType") ?? "tender").Trim().ToLowerInvariant();
        var id = GetInt(args, "id")
            ?? GetInt(args, "tenderId")
            ?? GetInt(args, "requestId")
            ?? GetInt(args, "supplierId");
        var search = GetString(args, "search")?.Trim();

        if (recordType.Contains("request"))
        {
            var req = await LoadAuthorizedRequestAsync(workspace, user, id, search, ct);
            if (req == null)
                return new(false, false, "No matching Purchase Request was found in your authorized workspace.");

            var detail = new
            {
                req.PurchaseRequestId,
                req.Title,
                Department = req.Department.Name,
                Requester = req.RequestedByUser.FullName,
                req.Priority,
                req.Status,
                TargetDate = req.TargetDate.ToString("yyyy-MM-dd"),
                req.Reason,
                req.ReviewNotes,
                CreatedUtc = FormatUtc(req.CreatedAt),
                SubmittedUtc = FormatUtc(req.SubmittedAt),
                Items = req.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).Select(i => new
                {
                    i.PurchaseRequestItemId,
                    i.ItemName,
                    i.Quantity,
                    i.Unit,
                    i.Specifications
                }).ToArray(),
                LinkedTenders = req.Tenders.OrderByDescending(t => t.TenderId).Select(t => new
                {
                    t.TenderId,
                    t.Title,
                    t.Status,
                    ClosesUtc = FormatUtc(t.ClosesAt)
                }).ToArray()
            };

            return new(
                true,
                false,
                $"Loaded Purchase Request #{req.PurchaseRequestId} ('{req.Title}'): {JsonSerializer.Serialize(detail, Json)}",
                ResolveRequestUrl(workspace, req.PurchaseRequestId),
                $"Open Request #{req.PurchaseRequestId}");
        }

        if (recordType.Contains("supplier") && workspace != RoleNames.Supplier && workspace != RoleNames.Department)
        {
            var query = db.SupplierProfiles.AsNoTracking()
                .Include(s => s.Invitations).ThenInclude(i => i.Tender)
                .Include(s => s.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems)
                .AsQueryable();

            SupplierProfile? supplier = null;
            if (id is > 0)
                supplier = await query.FirstOrDefaultAsync(s => s.SupplierProfileId == id.Value, ct);
            else if (!string.IsNullOrWhiteSpace(search))
                supplier = await query.FirstOrDefaultAsync(s => s.CompanyName.Contains(search), ct);

            if (supplier == null)
                return new(false, false, "No matching Supplier profile was found.");

            var bids = supplier.Invitations.SelectMany(i => i.Bids).Where(b => b.SubmittedAt != null).ToList();
            var detail = new
            {
                supplier.SupplierProfileId,
                supplier.CompanyName,
                supplier.RegistrationNumber,
                supplier.Category,
                supplier.Status,
                InvitationsCount = supplier.Invitations.Count,
                SubmittedOffersCount = bids.Count,
                AwardedCount = supplier.Invitations.Count(i => i.Tender.Status == "Awarded"
                    && i.Bids.Any(b => b.BidId == i.Tender.AwardedBidId)),
                RecentOffers = bids.OrderByDescending(b => b.SubmittedAt).Take(8).Select(b => new
                {
                    b.TenderId,
                    TenderTitle = supplier.Invitations.First(i => i.InvitationId == b.InvitationId).Tender.Title,
                    b.BidId,
                    b.Status,
                    b.ComplianceStatus,
                    Total = CommitteeEvaluationPage.Total(b),
                    b.Currency,
                    b.DeliveryDays
                }).ToArray()
            };

            return new(
                true,
                false,
                $"Loaded Supplier #{supplier.SupplierProfileId} ('{supplier.CompanyName}'): {JsonSerializer.Serialize(detail, Json)}",
                workspace == RoleNames.Admin ? $"/Admin/SupplierDetails/{supplier.SupplierProfileId}" : null,
                workspace == RoleNames.Admin ? $"Open Supplier #{supplier.SupplierProfileId}" : null);
        }

        var tender = await LoadAuthorizedTenderAsync(workspace, principal, user, id, search, ct);
        if (tender == null)
            return new(false, false, "No matching Tender was found in your authorized workspace.");

        var snapshotJson = JsonSerializer.Serialize(await BuildAuthorizedTenderSnapshotAsync(workspace, user, tender, ct), Json);
        return new(
            true,
            false,
            $"Loaded Tender #{tender.TenderId} ('{tender.Title}'): {snapshotJson}",
            ResolveTenderUrl(workspace, tender.TenderId),
            $"Open Tender #{tender.TenderId}");
    }

    public async Task<AiChatToolExecutionResult> GenerateDownloadAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        JsonElement args,
        CancellationToken ct)
    {
        var rawScope = (GetString(args, "scope") ?? "").Trim().ToLowerInvariant();
        var rawFormat = (GetString(args, "format") ?? "both").Trim().ToLowerInvariant();
        var tenderId = GetInt(args, "tenderId");
        var requestId = GetInt(args, "requestId");
        var search = GetString(args, "search")?.Trim();
        var customTitle = GetString(args, "title")?.Trim();
        var executiveSummary = GetString(args, "executiveSummary")?.Trim();
        var highlights = GetStringArray(args, "highlights");
        var customColumns = GetStringArray(args, "columns");
        var customRows = GetStringMatrix(args, "rows");

        AiChatExportDocument? docModel = null;
        string? targetUrl = null;
        string? targetLabel = null;

        if (rawScope == "tender" || tenderId is > 0)
        {
            var tender = await LoadAuthorizedTenderAsync(workspace, principal, user, tenderId, search
                ?? customTitle, ct);
            if (tender == null)
                return new(false, false, "Could not find an authorized Tender matching that ID or title to export.");

            docModel = await BuildTenderExportDocumentAsync(workspace, user, tender, customTitle, executiveSummary, highlights, customColumns, customRows, ct);
            targetUrl = ResolveTenderUrl(workspace, tender.TenderId);
            targetLabel = $"Open Tender #{tender.TenderId}";
        }
        else if (rawScope == "request" || requestId is > 0)
        {
            var req = await LoadAuthorizedRequestAsync(workspace, user, requestId, search ?? customTitle, ct);
            if (req == null)
                return new(false, false, "Could not find an authorized Purchase Request matching that ID or title to export.");

            docModel = BuildRequestExportDocument(req, customTitle, executiveSummary, highlights, customColumns, customRows);
            targetUrl = ResolveRequestUrl(workspace, req.PurchaseRequestId);
            targetLabel = $"Open Request #{req.PurchaseRequestId}";
        }
        else if (rawScope == "workspace")
        {
            docModel = await BuildWorkspaceExportDocumentAsync(workspace,
                principal,
                user,
                customTitle,
                executiveSummary,
                highlights,
                customColumns,
                customRows,
                ct);
        }
        else
        {
            docModel = BuildCustomExportDocument(workspace, user, customTitle, executiveSummary, highlights, customColumns, customRows);
        }

        var wantPdf = rawFormat is "pdf" or "both" or "all" || (rawFormat != "excel" && rawFormat != "xlsx");
        var wantExcel = rawFormat is "excel" or "xlsx" or "both" or "all";

        var links = new List<AiChatDownloadLink>();
        var roleLabel = RoleNames.DisplayName(workspace);

        if (wantPdf)
        {
            var pdfBytes = RenderPdfDocument(docModel, roleLabel);
            links.Add(downloads.Save(
                user.Id,
                workspace,
                $"{docModel.FileSlug}.pdf",
                "application/pdf",
                "Download PDF",
                "pdf",
                pdfBytes));
        }

        if (wantExcel)
        {
            var xlsxBytes = RenderExcelDocument(docModel, roleLabel);
            links.Add(downloads.Save(
                user.Id,
                workspace,
                $"{docModel.FileSlug}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "Download Excel (.xlsx)",
                "excel",
                xlsxBytes));
        }

        var formatsList = string.Join(" & ", links.Select(l => l.FileName));
        return new AiChatToolExecutionResult(
            true,
            false,
            $"Generated downloadable file(s) ({formatsList}) for '{docModel.Title}'. Clickable download " +
                    $"buttons are now attached directly below your chat reply.",
            targetUrl,
            targetLabel,
            null,
            links);
    }

    // ---------------- AUTHORIZED DATA LOADERS ----------------

    private async Task<Tender?> LoadAuthorizedTenderAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        int? tenderId,
        string? search,
        CancellationToken ct)
    {
        var baseQuery = db.Tenders.AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.PurchaseRequest).ThenInclude(r => r.Department)
            .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(t => t.OfferExtensions)
            .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
            .Include(t => t.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems)
            .Include(t => t.CommitteeWeightProposals)
            .Include(t => t.CommitteeAssignments).ThenInclude(a => a.User)
            .Include(t => t.TenderApprovals).ThenInclude(a => a.User)
            .Include(t => t.EvaluationReport)
            .AsQueryable();

        IQueryable<Tender> scoped = workspace switch
        {
            RoleNames.Admin => baseQuery,
            RoleNames.SourcingOfficer => baseQuery.Where(t => t.CreatedByUserId == user.Id),
            RoleNames.EvaluationCommittee => baseQuery.InEvaluationWorkspace(user.Id),
            RoleNames.SourcingManager => baseQuery.Where(t =>
                t.EvaluationReport != null ||
                t.Status == "AwaitingManagerReview" ||
                t.Status == "AwaitingCommitteeDecision" ||
                t.Status == "Awarded"),
            RoleNames.TopCommittee => baseQuery.Where(t =>
                t.Status == "AwaitingCommitteeDecision" ||
                t.Status == "Awarded" ||
                t.TenderApprovals.Any(a => a.Stage == "Top") ||
                t.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == user.Id)),
            RoleNames.Department when user.DepartmentId.HasValue =>
                baseQuery.Where(t => t.PurchaseRequest.DepartmentId == user.DepartmentId.Value),
            RoleNames.Supplier =>
                baseQuery.Where(t => t.Invitations.Any(i => i.SupplierProfile.UserId == user.Id
                    && i.SupplierProfile.Status == "Approved")),
            _ => baseQuery.Where(_ => false)
        };

        Tender? found = null;
        if (tenderId is > 0)
        {
            found = await scoped.FirstOrDefaultAsync(t => t.TenderId == tenderId.Value, ct);
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            found = await scoped
                .OrderByDescending(t => t.TenderId)
                .FirstOrDefaultAsync(t => t.Title.Contains(search), ct);
        }
        else
        {
            found = await scoped.OrderByDescending(t => t.TenderId).FirstOrDefaultAsync(ct);
        }

        if (found != null && workspace == RoleNames.TopCommittee)
        {
            try
            {
                _ = await topCommittee.GetTenderDetailsAsync(principal, found.TenderId, ct);
            }
            catch
            {
                return null;
            }
        }

        return found;
    }

    private async Task<PurchaseRequest?> LoadAuthorizedRequestAsync(
        string workspace,
        ApplicationUser user,
        int? requestId,
        string? search,
        CancellationToken ct)
    {
        var baseQuery = db.PurchaseRequests.AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Department)
            .Include(r => r.RequestedByUser)
            .Include(r => r.PurchaseRequestItems)
            .Include(r => r.Tenders)
            .AsQueryable();

        IQueryable<PurchaseRequest> scoped = workspace switch
        {
            RoleNames.Admin => baseQuery,
            RoleNames.Department when user.DepartmentId.HasValue => baseQuery.InDepartment(user.DepartmentId),
            RoleNames.SourcingOfficer => baseQuery.ReviewableBy(user.Id),
            RoleNames.SourcingManager => baseQuery.Where(r => r.Tenders.Any()),
            _ => baseQuery.Where(_ => false)
        };

        if (requestId is > 0)
            return await scoped.FirstOrDefaultAsync(r => r.PurchaseRequestId == requestId.Value, ct);

        if (!string.IsNullOrWhiteSpace(search))
            return await scoped.OrderByDescending(r => r.PurchaseRequestId)
                .FirstOrDefaultAsync(r => r.Title.Contains(search), ct);

        return await scoped.OrderByDescending(r => r.PurchaseRequestId).FirstOrDefaultAsync(ct);
    }

    private async Task<object> BuildAuthorizedTenderSnapshotAsync(
        string workspace,
        ApplicationUser user,
        Tender tender,
        CancellationToken ct)
    {
        var items = tender.PurchaseRequest.PurchaseRequestItems
            .OrderBy(i => i.PurchaseRequestItemId)
            .Select(i => new { i.PurchaseRequestItemId, i.ItemName, i.Quantity, i.Unit, i.Specifications })
            .ToArray();

        if (workspace == RoleNames.Department)
        {
            return new
            {
                tender.TenderId,
                tender.Title,
                tender.Category,
                tender.Status,
                Department = tender.PurchaseRequest.Department.Name,
                OpensUtc = FormatUtc(tender.OpensAt),
                ClosesUtc = FormatUtc(tender.ClosesAt),
                AwardedUtc = FormatUtc(tender.AwardedAt),
                tender.Description,
                RequestedItems = items
            };
        }

        if (workspace == RoleNames.Supplier)
        {
            var myInv = tender.Invitations.FirstOrDefault(i => i.SupplierProfile.UserId == user.Id);
            var myBid = myInv?.Bids.OrderByDescending(b => b.BidId).FirstOrDefault();
            return new
            {
                tender.TenderId,
                tender.Title,
                tender.Category,
                tender.Status,
                OpensUtc = FormatUtc(tender.OpensAt),
                ClosesUtc = FormatUtc(tender.ClosesAt),
                tender.Description,
                tender.Terms,
                InvitationStatus = myInv?.Status,
                RequestedItems = items,
                MyOffer = myBid == null ? null : new
                {
                    myBid.BidId,
                    myBid.Status,
                    myBid.ComplianceStatus,
                    Total = CommitteeEvaluationPage.Total(myBid),
                    myBid.Currency,
                    myBid.DeliveryDays,
                    myBid.PaymentTerms
                }
            };
        }

        var submittedBids = tender.Invitations.SelectMany(i => i.Bids)
            .Where(b => b.SubmittedAt != null && b.Status != "Withdrawn")
            .Where(b => workspace != RoleNames.EvaluationCommittee || b.ComplianceStatus == "Compliant")
            .OrderBy(b => b.BidId)
            .ToList();

        var evals = await db.Evaluations.AsNoTracking()
            .Where(e => e.TenderId == tender.TenderId)
            .ToListAsync(ct);

        var evalPage = new CommitteeEvaluationPage
        {
            Tender = tender,
            UserId = user.Id,
            Evaluations = evals
        };
        var ranking = AllOfferScoring.Ranking(evalPage);

        return new
        {
            tender.TenderId,
            tender.PurchaseRequestId,
            Department = tender.PurchaseRequest.Department.Name,
            tender.Title,
            tender.Category,
            tender.Status,
            Round = 1 + tender.OfferExtensions.Count,
            OpensUtc = FormatUtc(tender.OpensAt),
            ClosesUtc = FormatUtc(tender.ClosesAt),
            AwardedUtc = FormatUtc(tender.AwardedAt),
            tender.Description,
            tender.Terms,
            FinancialWeight = evalPage.ConsensusLocked ? evalPage.FinancialWeight : (decimal?)null,
            TechnicalWeight = evalPage.ConsensusLocked ? evalPage.TechnicalWeight : (decimal?)null,
            InvitedSuppliersCount = tender.Invitations.Count,
            SubmittedOffersCount = submittedBids.Count,
            CompliantOffersCount = submittedBids.Count(b => b.ComplianceStatus == "Compliant"),
            RequestedItems = items,
            Offers = submittedBids.Select(b =>
            {
                var rank = ranking.FirstOrDefault(r => r.BidId == b.BidId);
                return new
                {
                    b.BidId,
                    Supplier = b.Invitation.SupplierProfile.CompanyName,
                    b.Status,
                    b.ComplianceStatus,
                    Subtotal = b.BidItems.Sum(x => x.Quantity * x.UnitPrice),
                    b.TaxAmount,
                    b.AdditionalCosts,
                    Total = CommitteeEvaluationPage.Total(b),
                    b.Currency,
                    b.DeliveryDays,
                    b.PaymentTerms,
                    AverageScore = rank?.Score
                };
            }).ToArray(),
            Report = tender.EvaluationReport == null ? null : new
            {
                tender.EvaluationReport.EvaluationReportId,
                tender.EvaluationReport.Status,
                tender.EvaluationReport.RecommendedBidId,
                tender.EvaluationReport.OfficerSummary,
                tender.EvaluationReport.ManagerSummary
            }
        };
    }

    // ---------------- DOCUMENT BUILDERS ----------------

    private async Task<AiChatExportDocument> BuildTenderExportDocumentAsync(
        string workspace,
        ApplicationUser user,
        Tender tender,
        string? customTitle,
        string? executiveSummary,
        IReadOnlyList<string> highlights,
        IReadOnlyList<string> customColumns,
        IReadOnlyList<string[]> customRows,
        CancellationToken ct)
    {
        var title = !string.IsNullOrWhiteSpace(customTitle)
            ? customTitle
            : $"Tender #{tender.TenderId} Summary — {tender.Title}";

        var metrics = new List<(string Label, string Value)>
        {
            ("Tender ID", $"#{tender.TenderId}"),
            ("Status", tender.Status),
            ("Category", tender.Category ?? "General"),
            ("Offer Round", (1 + tender.OfferExtensions.Count).ToString(CultureInfo.InvariantCulture)),
            ("Opens (UTC)", FormatUtc(tender.OpensAt)),
            ("Closes (UTC)", FormatUtc(tender.ClosesAt))
        };

        if (tender.AwardedAt.HasValue)
            metrics.Add(("Awarded (UTC)", FormatUtc(tender.AwardedAt)));

        if (workspace != RoleNames.Supplier)
        {
            metrics.Add(("Purchase Request", $"#{tender.PurchaseRequestId} ({tender.PurchaseRequest.Department.Name})"));
        }

        var tables = new List<AiChatExportTable>();

        // 1. Requested Items Table
        var reqItems = tender.PurchaseRequest.PurchaseRequestItems
            .OrderBy(i => i.PurchaseRequestItemId)
            .Select(i => new[]
            {
                $"#{i.PurchaseRequestItemId}",
                i.ItemName,
                $"{i.Quantity:0.###} {i.Unit}",
                string.IsNullOrWhiteSpace(i.Specifications) ? "Standard specification" : i.Specifications
            })
            .ToList();

        tables.Add(new AiChatExportTable(
            "Requested Items",
            ["Item ID", "Item Name", "Quantity", "Requested Specifications"],
            reqItems));

        // 2. Role-Scoped Offers / Invitations Table
        if (workspace == RoleNames.Supplier)
        {
            var myInv = tender.Invitations.FirstOrDefault(i => i.SupplierProfile.UserId == user.Id);
            var myBids = myInv?.Bids.OrderByDescending(b => b.BidId).ToList() ?? [];
            metrics.Add(("Invitation Status", myInv?.Status ?? "Invited"));
            tables.Add(new AiChatExportTable(
                "My Quotations",
                ["Offer ID", "Status", "Compliance", "Subtotal", "Tax", "Add. Costs", "Total", "Delivery", "Payment Terms"],
                myBids.Select(b => new[]
                {
                    $"#{b.BidId}",
                    b.Status,
                    b.ComplianceStatus ?? "Pending",
                    $"{b.BidItems.Sum(x => x.Quantity * x.UnitPrice):N2} {b.Currency}",
                    $"{b.TaxAmount:N2} {b.Currency}",
                    $"{b.AdditionalCosts:N2} {b.Currency}",
                    $"{CommitteeEvaluationPage.Total(b):N2} {b.Currency}",
                    $"{b.DeliveryDays} days",
                    b.PaymentTerms ?? "Not specified"
                }).ToList()));
        }
        else if (workspace != RoleNames.Department)
        {
            var evals = await db.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == tender.TenderId)
                .ToListAsync(ct);
            var evalPage = new CommitteeEvaluationPage { Tender = tender, UserId = user.Id, Evaluations = evals };
            var ranking = AllOfferScoring.Ranking(evalPage);

            var visibleBids = tender.Invitations.SelectMany(i => i.Bids)
                .Where(b => b.SubmittedAt != null && b.Status != "Withdrawn")
                .Where(b => workspace != RoleNames.EvaluationCommittee || b.ComplianceStatus == "Compliant")
                .OrderBy(b => CommitteeEvaluationPage.Total(b))
                .ToList();

            metrics.Add(("Invited Suppliers", tender.Invitations.Count.ToString(CultureInfo.InvariantCulture)));
            metrics.Add(("Submitted Offers", visibleBids.Count.ToString(CultureInfo.InvariantCulture)));
            metrics.Add(("Compliant Offers", visibleBids.Count(b => b.ComplianceStatus == "Compliant")
                .ToString(CultureInfo.InvariantCulture)));

            if (evalPage.ConsensusLocked)
            {
                metrics.Add(("Locked Weights", $"{evalPage.FinancialWeight:0.##}% Financial / {evalPage.TechnicalWeight:0.##}% Technical"));
            }

            tables.Add(new AiChatExportTable(
                "Submitted Supplier Offers",
                ["Offer", "Supplier", "Compliance", "Total", "Delivery", "Payment Terms", "Avg Score", "Status"],
                visibleBids.Select(b =>
                {
                    var rank = ranking.FirstOrDefault(r => r.BidId == b.BidId);
                    return new[]
                    {
                        $"#{b.BidId}",
                        b.Invitation.SupplierProfile.CompanyName,
                        b.ComplianceStatus ?? "Pending",
                        $"{CommitteeEvaluationPage.Total(b):N2} {b.Currency}",
                        $"{b.DeliveryDays} days",
                        b.PaymentTerms ?? "Not specified",
                        rank != null ? $"{rank.Score:0.##} / 100" : "N/A",
                        b.BidId == tender.AwardedBidId ? "Awarded Winner" : b.Status
                    };
                }).ToList()));

            // Itemized quotation lines sheet/table when offers exist
            var lineRows = visibleBids.SelectMany(b => b.BidItems.OrderBy(bi => bi.BidItemId).Select(bi =>
            {
                var reqItem = tender.PurchaseRequest.PurchaseRequestItems.FirstOrDefault(ri => ri.PurchaseRequestItemId == bi.PurchaseRequestItemId);
                return new[]
                {
                    $"#{b.BidId} ({b.Invitation.SupplierProfile.CompanyName})",
                    reqItem?.ItemName ?? $"Item #{bi.PurchaseRequestItemId}",
                    $"{bi.Quantity:0.###} {reqItem?.Unit ?? ""}".Trim(),
                    $"{bi.UnitPrice:N2} {b.Currency}",
                    $"{bi.Quantity * bi.UnitPrice:N2} {b.Currency}",
                    bi.OfferedSpecifications ?? ""
                };
            })).ToList();

            if (lineRows.Count > 0)
            {
                tables.Add(new AiChatExportTable(
                    "Itemized Offer Pricing",
                    ["Offer & Supplier", "Item", "Quantity", "Unit Price", "Line Subtotal", "Offered Specifications"],
                    lineRows));
            }

            if (tender.EvaluationReport is { } rep)
            {
                var recSupplier = visibleBids.FirstOrDefault(b => b.BidId == rep.RecommendedBidId)?.Invitation.SupplierProfile.CompanyName
                                  ?? (rep.RecommendedBidId.HasValue ? $"Offer #{rep.RecommendedBidId}" : "None");
                tables.Add(new AiChatExportTable(
                    "Evaluation & Approval Summary",
                    ["Report ID", "Report Status", "Recommended Offer", "Officer Review", "Manager Review"],
                    [
                        [
                            $"#{rep.EvaluationReportId}",
                            rep.Status,
                            recSupplier,
                            rep.OfficerSummary ?? "Pending",
                            rep.ManagerSummary ?? "Pending"
                        ]
                    ]));
            }
        }

        if (customColumns.Count > 0 && customRows.Count > 0)
        {
            tables.Add(NormalizeCustomTable("Custom Analysis", customColumns, customRows));
        }

        var summaryText = !string.IsNullOrWhiteSpace(executiveSummary)
            ? executiveSummary
            : $"Live Tiryaq summary for Tender #{tender.TenderId} ({tender.Title}). Scope: {tender.Description}" +
              (string.IsNullOrWhiteSpace(tender.Terms) ? "" : $" Terms: {tender.Terms}");

        return new AiChatExportDocument(
            title,
            $"Tender #{tender.TenderId} · {RoleNames.DisplayName(workspace)} Workspace",
            SanitizeSlug($"Tender-{tender.TenderId}-Summary"),
            metrics,
            summaryText,
            highlights,
            tables);
    }

    private static AiChatExportDocument BuildRequestExportDocument(
        PurchaseRequest req,
        string? customTitle,
        string? executiveSummary,
        IReadOnlyList<string> highlights,
        IReadOnlyList<string> customColumns,
        IReadOnlyList<string[]> customRows)
    {
        var title = !string.IsNullOrWhiteSpace(customTitle)
            ? customTitle
            : $"Purchase Request #{req.PurchaseRequestId} — {req.Title}";

        var metrics = new List<(string Label, string Value)>
        {
            ("Request ID", $"#{req.PurchaseRequestId}"),
            ("Department", req.Department.Name),
            ("Requester", req.RequestedByUser.FullName),
            ("Priority", req.Priority),
            ("Status", req.Status),
            ("Target Date", req.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Created (UTC)", FormatUtc(req.CreatedAt))
        };

        var tables = new List<AiChatExportTable>
        {
            new(
                "Requested Items",
                ["Item ID", "Item Name", "Quantity", "Unit", "Specifications"],
                req.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).Select(i => new[]
                {
                    $"#{i.PurchaseRequestItemId}",
                    i.ItemName,
                    i.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                    i.Unit,
                    i.Specifications ?? ""
                }).ToList())
        };

        if (req.Tenders.Count > 0)
        {
            tables.Add(new AiChatExportTable(
                "Linked Tenders",
                ["Tender ID", "Title", "Category", "Status", "Closes (UTC)"],
                req.Tenders.OrderByDescending(t => t.TenderId).Select(t => new[]
                {
                    $"#{t.TenderId}",
                    t.Title,
                    t.Category ?? "General",
                    t.Status,
                    FormatUtc(t.ClosesAt)
                }).ToList()));
        }

        if (customColumns.Count > 0 && customRows.Count > 0)
            tables.Add(NormalizeCustomTable("Custom Notes", customColumns, customRows));

        var summary = !string.IsNullOrWhiteSpace(executiveSummary)
            ? executiveSummary
            : $"Justification: {req.Reason}" + (string.IsNullOrWhiteSpace(req.ReviewNotes) ? "" : $"\nReview Notes: {req.ReviewNotes}");

        return new AiChatExportDocument(
            title,
            $"Purchase Request #{req.PurchaseRequestId} · {req.Department.Name}",
            SanitizeSlug($"Request-{req.PurchaseRequestId}-Summary"),
            metrics,
            summary,
            highlights,
            tables);
    }

    private async Task<AiChatExportDocument> BuildWorkspaceExportDocumentAsync(
        string workspace,
        ClaimsPrincipal principal,
        ApplicationUser user,
        string? customTitle,
        string? executiveSummary,
        IReadOnlyList<string> highlights,
        IReadOnlyList<string> customColumns,
        IReadOnlyList<string[]> customRows,
        CancellationToken ct)
    {
        var roleDisplay = RoleNames.DisplayName(workspace);
        var title = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : $"{roleDisplay} Workspace Summary Report";
        var metrics = new List<(string Label, string Value)>
        {
            ("Prepared For", $"{user.FullName} ({user.Email})"),
            ("Workspace", roleDisplay)
        };
        var tables = new List<AiChatExportTable>();

        switch (workspace)
        {
            case RoleNames.SourcingOfficer:
            {
                var requests = await db.PurchaseRequests.AsNoTracking()
                    .ReviewableBy(user.Id)
                    .Include(r => r.Department)
                    .Include(r => r.PurchaseRequestItems)
                    .OrderByDescending(r => r.PurchaseRequestId)
                    .Take(30)
                    .ToListAsync(ct);

                var tenders = await db.Tenders.AsNoTracking()
                    .Where(t => t.CreatedByUserId == user.Id)
                    .AsSplitQuery()
                    .Include(t => t.Invitations).ThenInclude(i => i.Bids)
                    .Include(t => t.EvaluationReport)
                    .OrderByDescending(t => t.TenderId)
                    .Take(30)
                    .ToListAsync(ct);

                metrics.Add(("Reviewable Requests", requests.Count.ToString(CultureInfo.InvariantCulture)));
                metrics.Add(("Managed Tenders", tenders.Count.ToString(CultureInfo.InvariantCulture)));

                tables.Add(new AiChatExportTable(
                    "Managed Tenders",
                    ["Tender ID", "Title", "Category", "Status", "Invited", "Submitted Offers", "Compliant", "Closes (UTC)"],
                    tenders.Select(t =>
                    {
                        var bids = t.Invitations.SelectMany(i => i.Bids)
                            .Where(b => b.SubmittedAt != null && b.Status != "Withdrawn")
                            .ToList();
                        return new[]
                        {
                            $"#{t.TenderId}",
                            t.Title,
                            t.Category ?? "General",
                            t.Status,
                            t.Invitations.Count.ToString(CultureInfo.InvariantCulture),
                            bids.Count.ToString(CultureInfo.InvariantCulture),
                            bids.Count(b => b.ComplianceStatus == "Compliant").ToString(CultureInfo.InvariantCulture),
                            FormatUtc(t.ClosesAt)
                        };
                    }).ToList()));

                tables.Add(new AiChatExportTable(
                    "Incoming Purchase Requests",
                    ["Request ID", "Title", "Department", "Priority", "Status", "Items", "Target Date"],
                    requests.Select(r => new[]
                    {
                        $"#{r.PurchaseRequestId}",
                        r.Title,
                        r.Department.Name,
                        r.Priority,
                        r.Status,
                        r.PurchaseRequestItems.Count.ToString(CultureInfo.InvariantCulture),
                        r.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }).ToList()));
                break;
            }

            case RoleNames.Department when user.DepartmentId.HasValue:
            {
                var requests = await db.PurchaseRequests.AsNoTracking()
                    .InDepartment(user.DepartmentId)
                    .Include(r => r.PurchaseRequestItems)
                    .Include(r => r.Tenders)
                    .OrderByDescending(r => r.PurchaseRequestId)
                    .Take(40)
                    .ToListAsync(ct);

                metrics.Add(("Department Requests", requests.Count.ToString(CultureInfo.InvariantCulture)));
                tables.Add(new AiChatExportTable(
                    "Department Requests",
                    ["Request ID", "Title", "Priority", "Status", "Items", "Target Date", "Linked Tender"],
                    requests.Select(r => new[]
                    {
                        $"#{r.PurchaseRequestId}",
                        r.Title,
                        r.Priority,
                        r.Status,
                        r.PurchaseRequestItems.Count.ToString(CultureInfo.InvariantCulture),
                        r.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        r.Tenders.Any() ? string
                            .Join(", ", r.Tenders.Select(t => $"#{t.TenderId} ({t.Status})")) : "None"
                    }).ToList()));
                break;
            }

            case RoleNames.EvaluationCommittee:
            {
                var tenders = await db.Tenders.AsNoTracking()
                    .InEvaluationWorkspace(user.Id)
                    .AsSplitQuery()
                    .Include(t => t.Invitations).ThenInclude(i => i.Bids.Where(b => b.ComplianceStatus == "Compliant"))
                    .Include(t => t.CommitteeWeightProposals)
                    .Include(t => t.EvaluationReport)
                    .OrderByDescending(t => t.TenderId)
                    .Take(30)
                    .ToListAsync(ct);

                metrics.Add(("Evaluation Tenders", tenders.Count.ToString(CultureInfo.InvariantCulture)));
                tables.Add(new AiChatExportTable(
                    "Evaluation Queue",
                    ["Tender ID", "Title", "Status", "Compliant Offers", "Weights Confirmed", "Report Status"],
                    tenders.Select(t => new[]
                    {
                        $"#{t.TenderId}",
                        t.Title,
                        t.Status,
                        t.Invitations.SelectMany(i => i.Bids).Count().ToString(CultureInfo.InvariantCulture),
                        $"{t.CommitteeWeightProposals.Count} / 4",
                        t.EvaluationReport?.Status ?? "Not generated"
                    }).ToList()));
                break;
            }

            case RoleNames.SourcingManager:
            {
                var submissions = await db.EvaluationReports.AsNoTracking()
                    .Include(r => r.Tender)
                    .Include(r => r.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
                    .Include(r => r.Bid).ThenInclude(b => b!.BidItems)
                    .OrderByDescending(r => r.EvaluationReportId)
                    .Take(35)
                    .ToListAsync(ct);

                metrics.Add(("Committee Reports", submissions.Count.ToString(CultureInfo.InvariantCulture)));
                tables.Add(new AiChatExportTable(
                    "Submissions & Recommendations",
                    ["Report ID", "Tender", "Tender Status", "Report Status", "Recommended Supplier", "Total"],
                    submissions.Select(r => new[]
                    {
                        $"#{r.EvaluationReportId}",
                        $"#{r.TenderId} - {r.Tender.Title}",
                        r.Tender.Status,
                        r.Status,
                        r.Bid?.Invitation.SupplierProfile.CompanyName ?? "None",
                        r.Bid != null ? $"{CommitteeEvaluationPage.Total(r.Bid):N2} {r.Bid.Currency}" : "N/A"
                    }).ToList()));
                break;
            }

            case RoleNames.TopCommittee:
            {
                var dash = await topCommittee.GetDashboardAsync(principal, ct);
                metrics.Add(("Assigned Tenders", dash.AssignedTenders.ToString(CultureInfo.InvariantCulture)));
                metrics.Add(("Awaiting Decision", dash.AwaitingDecision.ToString(CultureInfo.InvariantCulture)));
                metrics.Add(("Awarded", dash.AwardedTenders.ToString(CultureInfo.InvariantCulture)));
                tables.Add(new AiChatExportTable(
                    "Pending Executive Votes",
                    ["Tender ID", "Title", "Nominated Supplier", "Approvals / 4"],
                    dash.PendingTenders.Select(p => new[]
                    {
                        $"#{p.TenderId}",
                        p.Title,
                        p.NominatedSupplier ?? "N/A",
                        $"{p.RecordedApprovals} / 4"
                    }).ToList()));
                break;
            }

            case RoleNames.Supplier:
            {
                var profile = await db.SupplierProfiles.AsNoTracking()
                    .Include(s => s.Invitations).ThenInclude(i => i.Tender)
                    .Include(s => s.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems)
                    .FirstOrDefaultAsync(s => s.UserId == user.Id && s.Status == "Approved", ct);

                if (profile != null)
                {
                    metrics.Add(("Company", profile.CompanyName));
                    tables.Add(new AiChatExportTable(
                        "My Invitations & Quotations",
                        ["Invitation", "Tender", "Invitation Status", "Tender Status", "My Offer", "Quoted Total", "Closes (UTC)"],
                        profile.Invitations.OrderByDescending(i => i.InvitationId).Select(i =>
                        {
                            var b = i.Bids.OrderByDescending(x => x.BidId).FirstOrDefault();
                            return new[]
                            {
                                $"#{i.InvitationId}",
                                $"#{i.TenderId} - {i.Tender.Title}",
                                i.Status,
                                i.Tender.Status,
                                b != null ? $"#{b.BidId} ({b.Status})" : "None",
                                b != null ? $"{CommitteeEvaluationPage.Total(b):N2} {b.Currency}" : "N/A",
                                FormatUtc(i.Tender.ClosesAt)
                            };
                        }).ToList()));
                }
                break;
            }

            case RoleNames.Admin:
            {
                var tenders = await db.Tenders.AsNoTracking()
                    .OrderByDescending(t => t.TenderId)
                    .Take(30)
                    .ToListAsync(ct);
                var suppliers = await db.SupplierProfiles.AsNoTracking()
                    .OrderByDescending(s => s.SupplierProfileId)
                    .Take(40)
                    .ToListAsync(ct);
                metrics.Add(("Total Users", (await db.Users.CountAsync(ct)).ToString(CultureInfo.InvariantCulture)));
                metrics.Add(("Total Suppliers", suppliers.Count.ToString(CultureInfo.InvariantCulture)));
                metrics.Add(("Recent Tenders", tenders.Count.ToString(CultureInfo.InvariantCulture)));

                tables.Add(new AiChatExportTable(
                    "Recent Tenders",
                    ["Tender ID", "Title", "Category", "Status", "Opens (UTC)", "Closes (UTC)"],
                    tenders.Select(t => new[]
                    {
                        $"#{t.TenderId}",
                        t.Title,
                        t.Category ?? "General",
                        t.Status,
                        FormatUtc(t.OpensAt),
                        FormatUtc(t.ClosesAt)
                    }).ToList()));

                tables.Add(new AiChatExportTable(
                    "Suppliers Directory",
                    ["Supplier ID", "Company Name", "Registration #", "Category", "Status"],
                    suppliers.Select(s => new[]
                    {
                        $"#{s.SupplierProfileId}",
                        s.CompanyName,
                        s.RegistrationNumber,
                        s.Category ?? "Not specified",
                        s.Status
                    }).ToList()));
                break;
            }
        }

        if (customColumns.Count > 0 && customRows.Count > 0)
            tables.Add(NormalizeCustomTable("Custom Breakdown", customColumns, customRows));

        return new AiChatExportDocument(
            title,
            $"Tiryaq {roleDisplay} Live Snapshot",
            SanitizeSlug($"{roleDisplay}-Workspace-Summary"),
            metrics,
            executiveSummary ?? $"Live summary generated by Safi for {user.FullName} in the {roleDisplay} workspace.",
            highlights,
            tables);
    }

    private static AiChatExportDocument BuildCustomExportDocument(
        string workspace,
        ApplicationUser user,
        string? customTitle,
        string? executiveSummary,
        IReadOnlyList<string> highlights,
        IReadOnlyList<string> customColumns,
        IReadOnlyList<string[]> customRows)
    {
        var roleDisplay = RoleNames.DisplayName(workspace);
        var title = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : $"Safi Summary — {roleDisplay}";
        var metrics = new List<(string Label, string Value)>
        {
            ("Prepared For", $"{user.FullName} ({user.Email})"),
            ("Workspace", roleDisplay),
            ("Generated (UTC)", FormatUtc(DateTime.UtcNow))
        };

        var tables = new List<AiChatExportTable>();
        if (customColumns.Count > 0 && customRows.Count > 0)
        {
            tables.Add(NormalizeCustomTable("Structured Data", customColumns, customRows));
        }

        return new AiChatExportDocument(
            title,
            $"Prepared by Safi · Tiryaq {roleDisplay}",
            SanitizeSlug(title),
            metrics,
            executiveSummary ?? "Summary exported from your Tiryaq conversation with Safi.",
            highlights,
            tables);
    }

    // ---------------- PDF & EXCEL RENDERERS ----------------

    internal static byte[] RenderPdfDocument(AiChatExportDocument model, string ownerWorkspace)
    {
        var doc = TopCommitteeReportExport.CreatePdfDocument(model.Title);
        var section = doc.LastSection;

        var titlePara = section.AddParagraph(model.Title);
        titlePara.Format.Font.Size = 18;
        titlePara.Format.Font.Bold = true;

        TopCommitteeReportExport.AddPdfText(section, $"{model.Subtitle} | Generated {FormatUtc(DateTime.UtcNow)} UTC").Format.SpaceAfter = 8;

        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText($"Tiryaq · {ownerWorkspace} · Safi Export | Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        if (model.Metrics.Count > 0)
        {
            TopCommitteeReportExport.Heading(section, "Key Metadata & Snapshot");
            var metricTable = TopCommitteeReportExport.Table(section, ["Field", "Value"], [7.5, 19.2]);
            foreach (var (label, val) in model.Metrics)
            {
                TopCommitteeReportExport.AddRow(metricTable, [label, val]);
            }
        }

        if (!string.IsNullOrWhiteSpace(model.ExecutiveSummary))
        {
            TopCommitteeReportExport.Heading(section, "Executive Summary");
            foreach (var block in model.ExecutiveSummary.Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                TopCommitteeReportExport.AddPdfText(section, block).Format.SpaceAfter = 5;
            }
        }

        if (model.Highlights.Count > 0)
        {
            TopCommitteeReportExport.Heading(section, "Key Highlights");
            foreach (var item in model.Highlights.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                TopCommitteeReportExport.AddPdfText(section, $"• {item.Trim()}").Format.SpaceAfter = 3;
            }
        }

        foreach (var tbl in model.Tables)
        {
            TopCommitteeReportExport.Heading(section, tbl.Title);
            if (tbl.Rows.Count == 0 || tbl.Headers.Length == 0)
            {
                TopCommitteeReportExport.AddPdfText(section, "No records recorded in this section.");
                continue;
            }

            var colCount = Math.Min(tbl.Headers.Length, 10);
            var headers = tbl.Headers.Take(colCount).ToArray();
            var widths = CalculateColumnWidths(headers, tbl.Rows, colCount, 26.7);
            var pdfTable = TopCommitteeReportExport.Table(section, headers, widths);

            foreach (var r in tbl.Rows)
            {
                var normalized = new string[colCount];
                for (var c = 0; c < colCount; c++)
                    normalized[c] = c < r.Length ? (r[c] ?? "") : "";
                TopCommitteeReportExport.AddRow(pdfTable, normalized);
            }
        }

        return TopCommitteeReportExport.RenderPdf(doc);
    }

    internal static byte[] RenderExcelDocument(AiChatExportDocument model, string ownerWorkspace)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.AddWorksheet("Summary");
        summary.Cell(1, 1).Value = model.Title;
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 14;
        summary.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0c252d");

        summary.Cell(2, 1).Value = model.Subtitle;
        summary.Cell(2, 2).Value = $"Generated {FormatUtc(DateTime.UtcNow)} UTC ({ownerWorkspace})";

        var row = 4;
        if (model.Metrics.Count > 0)
        {
            StyleSheetHeader(summary, row, ["Field", "Value"]);
            row++;
            foreach (var (label, val) in model.Metrics)
            {
                summary.Cell(row, 1).Value = label;
                summary.Cell(row, 2).Value = val;
                row++;
            }
            row++;
        }

        if (!string.IsNullOrWhiteSpace(model.ExecutiveSummary))
        {
            summary.Cell(row, 1).Value = "Executive Summary";
            summary.Cell(row, 1).Style.Font.Bold = true;
            row++;
            summary.Cell(row, 1).Value = model.ExecutiveSummary;
            summary.Range(row, 1, row, 4).Merge().Style.Alignment.WrapText = true;
            summary.Row(row).Height = Math.Clamp(24 * (1 + model.ExecutiveSummary.Length / 110), 32, 180);
            row += 2;
        }

        if (model.Highlights.Count > 0)
        {
            summary.Cell(row, 1).Value = "Key Highlights";
            summary.Cell(row, 1).Style.Font.Bold = true;
            row++;
            foreach (var h in model.Highlights.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                summary.Cell(row, 1).Value = $"• {h.Trim()}";
                summary.Range(row, 1, row, 4).Merge().Style.Alignment.WrapText = true;
                row++;
            }
        }

        summary.Column(1).Width = 30;
        summary.Column(2).Width = 52;
        summary.Column(3).Width = 28;
        summary.Column(4).Width = 28;
        summary.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Summary" };
        var sheetIndex = 1;
        foreach (var tbl in model.Tables)
        {
            if (tbl.Headers.Length == 0) continue;
            var sheetName = MakeSafeSheetName(tbl.Title, usedNames, ref sheetIndex);
            var ws = workbook.AddWorksheet(sheetName);

            StyleSheetHeader(ws, 1, tbl.Headers);
            var rIdx = 2;
            foreach (var dataRow in tbl.Rows)
            {
                for (var c = 0; c < tbl.Headers.Length; c++)
                {
                    var val = c < dataRow.Length ? (dataRow[c] ?? "") : "";
                    ws.Cell(rIdx, c + 1).Value = val;
                    ws.Cell(rIdx, c + 1).Style.Alignment.WrapText = true;
                }
                rIdx++;
            }

            if (tbl.Rows.Count == 0)
            {
                ws.Cell(2, 1).Value = "No records recorded.";
                rIdx = 3;
            }

            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, Math.Max(2, rIdx - 1), tbl.Headers.Length).SetAutoFilter();
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            for (var c = 1; c <= tbl.Headers.Length; c++)
            {
                var maxLen = tbl.Rows.Select(r => c - 1 < r.Length ? (r[c - 1]?.Length ?? 0) : 0)
                    .Append(tbl.Headers[c - 1].Length)
                    .Max();
                ws.Column(c).Width = Math.Clamp(maxLen + 5, 14, 48);
            }
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void StyleSheetHeader(IXLWorksheet ws, int row, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(row, i + 1).Value = headers[i];

        var rng = ws.Range(row, 1, row, headers.Length);
        rng.Style.Font.Bold = true;
        rng.Style.Fill.BackgroundColor = XLColor.FromHtml("#00a3a6");
        rng.Style.Font.FontColor = XLColor.FromHtml("#002e30");
        rng.Style.Alignment.WrapText = true;
        ws.Row(row).Height = 28;
    }

    private static double[] CalculateColumnWidths(string[] headers, IReadOnlyList<string[]> rows, int colCount, double totalCm)
    {
        var weights = new double[colCount];
        for (var c = 0; c < colCount; c++)
        {
            var sampleMax = rows.Take(20)
                .Select(r => c < r.Length ? (r[c]?.Length ?? 0) : 0)
                .Append(headers[c].Length)
                .Max();
            weights[c] = Math.Clamp(Math.Sqrt(Math.Max(6, sampleMax)), 2.4, 9.5);
        }

        var sum = weights.Sum();
        var widths = weights.Select(w => Math.Round(totalCm * (w / sum), 2)).ToArray();
        var drift = Math.Round(totalCm - widths.Sum(), 2);
        widths[^1] = Math.Max(1.6, Math.Round(widths[^1] + drift, 2));
        return widths;
    }

    private static AiChatExportTable NormalizeCustomTable(string defaultTitle, IReadOnlyList<string> cols, IReadOnlyList<string[]> rows)
    {
        var headers = cols.Take(10)
            .Select((c, idx) => string.IsNullOrWhiteSpace(c) ? $"Column {idx + 1}" : c.Trim())
            .ToArray();
        var normalizedRows = rows.Take(150).Select(r =>
        {
            var arr = new string[headers.Length];
            for (var i = 0; i < headers.Length; i++)
                arr[i] = i < r.Length ? (r[i] ?? "") : "";
            return arr;
        }).ToList();
        return new AiChatExportTable(defaultTitle, headers, normalizedRows);
    }

    private static string MakeSafeSheetName(string rawTitle, HashSet<string> used, ref int index)
    {
        var cleaned = InvalidSheetCharsRegex().Replace(rawTitle ?? "", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = $"Data {index++}";
        if (cleaned.Length > 28) cleaned = cleaned[..28].Trim();
        var candidate = cleaned;
        while (!used.Add(candidate))
        {
            var suffix = $" {++index}";
            candidate = (cleaned.Length + suffix.Length > 31 ? cleaned[..(31 - suffix.Length)] : cleaned) + suffix;
        }
        return candidate;
    }

    private static string SanitizeSlug(string raw)
    {
        var slug = NonSlugRegex().Replace((raw ?? "Safi-Export").Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "Safi-Export";
        return slug.Length <= 55 ? slug : slug[..55].TrimEnd('-');
    }

    private static string FormatUtc(DateTime? dt) =>
        dt.HasValue ? dt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "N/A";

    private static string? ResolveTenderUrl(string workspace, int tenderId) => workspace switch
    {
        RoleNames.SourcingOfficer => $"/SourcingOfficer/Offers?tenderId={tenderId}",
        RoleNames.EvaluationCommittee => $"/EvaluationCommittee/Evaluations/{tenderId}",
        RoleNames.TopCommittee => $"/TopCommittee/TenderDetails/{tenderId}",
        _ => null
    };

    private static string? ResolveRequestUrl(string workspace, int requestId) => workspace switch
    {
        RoleNames.Department => $"/Department/Details/{requestId}",
        RoleNames.SourcingOfficer => $"/SourcingOfficer/Requests?id={requestId}",
        _ => null
    };

    // ---------------- JSON ARG HELPERS ----------------

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object
            || !el.TryGetProperty(prop, out var v)
            || v.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                list.Add(item.GetString()!.Trim());
        }
        return list;
    }

    private static IReadOnlyList<string[]> GetStringMatrix(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object
            || !el.TryGetProperty(prop, out var v)
            || v.ValueKind != JsonValueKind.Array)
            return [];
        var rows = new List<string[]>();
        foreach (var rowEl in v.EnumerateArray())
        {
            if (rowEl.ValueKind != JsonValueKind.Array) continue;
            var cells = new List<string>();
            foreach (var cell in rowEl.EnumerateArray())
            {
                cells.Add(cell.ValueKind switch
                {
                    JsonValueKind.String => cell.GetString() ?? "",
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => cell.GetRawText(),
                    _ => ""
                });
            }
            rows.Add(cells.ToArray());
        }
        return rows;
    }

    [GeneratedRegex(@"[:\\/?*\[\]]+")]
    private static partial Regex InvalidSheetCharsRegex();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex NonSlugRegex();
}
