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

public sealed record AiChatExportTable(string Title, string[] Headers, IReadOnlyList<string[]> Rows);

public sealed record AiChatExportDocument(
    string Title,
    string Subtitle,
    string FileSlug,
    IReadOnlyList<(string Label, string Value)> Metrics,
    string? ExecutiveSummary,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<AiChatExportTable> Tables);

/// <summary>One authorized record (tender, request or supplier) loaded for the signed-in user's role.</summary>
public sealed record AiChatRecordData(string Kind, int Id, string Title, object Data, string? Url, string Label);

/// <summary>
/// Loads records exactly as the user's role may see them, and turns any of them (or the workspace summary)
/// into a PDF/Excel file. Every export is built from the same role-scoped data the chat reads, so a file can
/// never contain more than the user could already open.
/// </summary>
public sealed partial class AiChatExportService(
    ApplicationDbContext db,
    ITopCommitteeQueries topCommittee,
    AiChatDownloadStore downloads)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AiChatToolExecutionResult> InspectRecordAsync(
        string workspace, ClaimsPrincipal principal, ApplicationUser user, JsonElement args, CancellationToken ct)
    {
        var record = await LoadRecordAsync(workspace, principal, user,
            Str(args, "recordType") ?? "tender",
            Int(args, "id") ?? Int(args, "tenderId") ?? Int(args, "requestId") ?? Int(args, "supplierId"),
            Str(args, "search"), ct);
        return record == null
            ? new(false, "No matching record was found in your authorized workspace.")
            : new(true, $"Loaded {record.Kind} #{record.Id} ('{record.Title}'): {JsonSerializer.Serialize(record.Data, Json)}", record.Url, record.Label);
    }

    /// <summary>Loads one tender, request or supplier by id (or title search) if the role may see it.</summary>
    public async Task<AiChatRecordData?> LoadRecordAsync(
        string workspace, ClaimsPrincipal principal, ApplicationUser user,
        string recordType, int? id, string? search, CancellationToken ct)
    {
        recordType = recordType.Trim().ToLowerInvariant();
        search = search?.Trim();

        if (recordType.Contains("request"))
        {
            var req = await LoadAuthorizedRequestAsync(workspace, user, id, search, ct);
            if (req == null) return null;
            var data = new
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
                Items = req.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId)
                    .Select(i => new { i.PurchaseRequestItemId, i.ItemName, i.Quantity, i.Unit, i.Specifications }).ToArray(),
                LinkedTenders = req.Tenders.OrderByDescending(t => t.TenderId)
                    .Select(t => new { t.TenderId, t.Title, t.Status, ClosesUtc = FormatUtc(t.ClosesAt) }).ToArray()
            };
            return new("Purchase Request", req.PurchaseRequestId, req.Title, data,
                workspace switch
                {
                    RoleNames.Department => $"/Department/Details/{req.PurchaseRequestId}",
                    RoleNames.SourcingOfficer => $"/SourcingOfficer/Requests?id={req.PurchaseRequestId}",
                    _ => null
                }, $"Open Request #{req.PurchaseRequestId}");
        }

        if (recordType.Contains("supplier") && workspace is not (RoleNames.Supplier or RoleNames.Department))
        {
            var query = db.SupplierProfiles.AsNoTracking()
                .Include(s => s.Invitations).ThenInclude(i => i.Tender)
                .Include(s => s.Invitations).ThenInclude(i => i.Bids).ThenInclude(b => b.BidItems);
            var supplier = id is > 0
                ? await query.FirstOrDefaultAsync(s => s.SupplierProfileId == id.Value, ct)
                : !string.IsNullOrWhiteSpace(search) ? await query.FirstOrDefaultAsync(s => s.CompanyName.Contains(search), ct) : null;
            if (supplier == null) return null;

            var bids = supplier.Invitations.SelectMany(i => i.Bids).Where(b => b.SubmittedAt != null).ToList();
            var data = new
            {
                supplier.SupplierProfileId,
                supplier.CompanyName,
                supplier.RegistrationNumber,
                supplier.Category,
                supplier.Status,
                InvitationsCount = supplier.Invitations.Count,
                SubmittedOffersCount = bids.Count,
                AwardedCount = supplier.Invitations.Count(i => i.Tender.Status == "Awarded" && i.Bids.Any(b => b.BidId == i.Tender.AwardedBidId)),
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
            var isAdmin = workspace == RoleNames.Admin;
            return new("Supplier", supplier.SupplierProfileId, supplier.CompanyName, data,
                isAdmin ? $"/Admin/SupplierDetails/{supplier.SupplierProfileId}" : null,
                $"Open Supplier #{supplier.SupplierProfileId}");
        }

        var tender = await LoadAuthorizedTenderAsync(workspace, principal, user, id, search, ct);
        if (tender == null) return null;
        return new("Tender", tender.TenderId, tender.Title,
            await BuildAuthorizedTenderSnapshotAsync(workspace, user, tender, ct),
            workspace switch
            {
                RoleNames.SourcingOfficer => $"/SourcingOfficer/Offers?tenderId={tender.TenderId}",
                RoleNames.EvaluationCommittee => $"/EvaluationCommittee/Evaluations/{tender.TenderId}",
                RoleNames.TopCommittee => $"/TopCommittee/TenderDetails/{tender.TenderId}",
                _ => null
            }, $"Open Tender #{tender.TenderId}");
    }

    public async Task<AiChatToolExecutionResult> GenerateDownloadAsync(
        string workspace, ClaimsPrincipal principal, ApplicationUser user, JsonElement args,
        object? workspaceData, CancellationToken ct)
    {
        var scope = (Str(args, "scope") ?? "").Trim().ToLowerInvariant();
        var format = (Str(args, "format") ?? "both").Trim().ToLowerInvariant();
        var tenderId = Int(args, "tenderId");
        var requestId = Int(args, "requestId");
        var search = Str(args, "search")?.Trim();
        var title = Str(args, "title")?.Trim();
        var roleLabel = RoleNames.DisplayName(workspace);

        object? data = null;
        string? subject = null, targetUrl = null, targetLabel = null;
        var slug = "";

        if (scope == "tender" || tenderId is > 0 || scope == "request" || requestId is > 0)
        {
            var isTender = scope == "tender" || tenderId is > 0;
            var record = await LoadRecordAsync(workspace, principal, user, isTender ? "tender" : "request",
                isTender ? tenderId : requestId, search ?? title, ct);
            if (record == null)
                return new(false, $"Could not find an authorized {(isTender ? "Tender" : "Purchase Request")} matching that ID or title to export.");
            data = record.Data;
            subject = $"{record.Kind} #{record.Id} — {record.Title}";
            slug = $"{record.Kind.Replace(" ", "")}-{record.Id}";
            (targetUrl, targetLabel) = (record.Url, record.Label);
        }
        else if (scope == "workspace")
        {
            data = workspaceData;
            subject = $"{roleLabel} Workspace Summary";
            slug = $"{roleLabel}-Workspace-Summary";
        }

        var doc = BuildDocument(workspace, user, title ?? subject, slug, data,
            Str(args, "executiveSummary")?.Trim(), StrList(args, "highlights"), StrList(args, "columns"), StrMatrix(args, "rows"));

        var links = new List<AiChatDownloadLink>();
        if (format is not ("excel" or "xlsx"))
            links.Add(downloads.Save(user.Id, workspace, $"{doc.FileSlug}.pdf", "application/pdf", "Download PDF", "pdf", RenderPdfDocument(doc, roleLabel)));
        if (format is "excel" or "xlsx" or "both" or "all")
            links.Add(downloads.Save(user.Id, workspace, $"{doc.FileSlug}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Download Excel (.xlsx)", "excel", RenderExcelDocument(doc, roleLabel)));

        return new(true,
            $"Generated downloadable file(s) ({string.Join(" & ", links.Select(l => l.FileName))}) for '{doc.Title}'. Clickable download buttons are now attached directly below your chat reply.",
            targetUrl, targetLabel, null, links);
    }

    // ---------------- ONE GENERIC DOCUMENT BUILDER ----------------

    /// <summary>
    /// Turns role-scoped data (a tender, a request, the workspace summary) into a document: single values become the
    /// metadata table, and every list of records becomes its own table/sheet. Optional custom columns/rows are added last.
    /// </summary>
    private static AiChatExportDocument BuildDocument(
        string workspace, ApplicationUser user, string? title, string slug, object? data,
        string? summary, IReadOnlyList<string> highlights, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows)
    {
        var roleLabel = RoleNames.DisplayName(workspace);
        title = string.IsNullOrWhiteSpace(title) ? $"Safi Summary — {roleLabel}" : title;

        var metrics = new List<(string, string)>
        {
            ("Prepared For", $"{user.FullName} ({user.Email})"),
            ("Workspace", roleLabel),
            ("Generated (UTC)", FormatUtc(DateTime.UtcNow))
        };
        var tables = new List<AiChatExportTable>();

        if (data != null)
        {
            var root = JsonSerializer.SerializeToElement(data, Json);
            if (root.ValueKind == JsonValueKind.Object)
                foreach (var prop in root.EnumerateObject())
                    AddValue(Humanize(prop.Name), prop.Value, metrics, tables);
        }

        if (columns.Count > 0 && rows.Count > 0)
        {
            var headers = columns.Take(10).Select((c, i) => string.IsNullOrWhiteSpace(c) ? $"Column {i + 1}" : c.Trim()).ToArray();
            tables.Add(new("Structured Data", headers, rows.Take(150)
                .Select(r => headers.Select((_, i) => i < r.Length ? r[i] ?? "" : "").ToArray()).ToList()));
        }

        return new(title, $"Prepared by Safi · Tiryaq {roleLabel}", SanitizeSlug(string.IsNullOrWhiteSpace(slug) ? title : slug),
            metrics, summary ?? $"Generated by Safi for {user.FullName} in the {roleLabel} workspace.", highlights, tables);
    }

    private static void AddValue(string name, JsonElement value, List<(string, string)> metrics, List<AiChatExportTable> tables)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var inner in value.EnumerateObject())
                    AddValue($"{name} · {Humanize(inner.Name)}", inner.Value, metrics, tables);
                break;
            case JsonValueKind.Array when value.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object):
                var items = value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Take(150).ToList();
                var keys = items.SelectMany(i => i.EnumerateObject().Select(p => p.Name)).Distinct().Take(10).ToArray();
                tables.Add(new(name, keys.Select(Humanize).ToArray(),
                    items.Select(i => keys.Select(k => i.TryGetProperty(k, out var v) ? Cell(v) : "").ToArray()).ToList()));
                break;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                break;
            default:
                metrics.Add((name, Cell(value)));
                break;
        }
    }

    private static string Cell(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null => "",
        JsonValueKind.Array => string.Join("; ", v.EnumerateArray().Select(Cell)),
        JsonValueKind.Object => string.Join(", ", v.EnumerateObject().Select(p => $"{Humanize(p.Name)}: {Cell(p.Value)}")),
        _ => v.GetRawText()
    };

    private static string Humanize(string name) => HumpRegex().Replace(name, " $1").Trim();

    // ---------------- ROLE-SCOPED LOADERS ----------------

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
                baseQuery.Where(t => t.Invitations.Any(i => i.SupplierProfile.UserId == user.Id && i.SupplierProfile.Status == "Approved")),
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
            return await scoped.OrderByDescending(r => r.PurchaseRequestId).FirstOrDefaultAsync(r => r.Title.Contains(search), ct);

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
            foreach (var block in model.ExecutiveSummary.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

    // ---------------- JSON ARG HELPERS ----------------

    public static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static int? Int(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static IReadOnlyList<string> StrList(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(i.GetString()))
                .Select(i => i.GetString()!.Trim()).ToList()
            : [];

    private static IReadOnlyList<string[]> StrMatrix(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Array)
                .Select(r => r.EnumerateArray().Select(c => c.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                    ? (c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.GetRawText()) : "").ToArray()).ToList()
            : [];

    [GeneratedRegex(@"[:\\/?*\[\]]+")]
    private static partial Regex InvalidSheetCharsRegex();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex NonSlugRegex();

    [GeneratedRegex(@"(?<=[a-z0-9])([A-Z])")]
    private static partial Regex HumpRegex();
}
