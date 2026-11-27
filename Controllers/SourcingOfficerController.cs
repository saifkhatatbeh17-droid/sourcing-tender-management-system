using SourcingTenderManagement.Services.Reports.Data;
using Microsoft.AspNetCore.Authorization;
using SourcingTenderManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Controllers;
// The Sourcing Officer workspace: review purchase requests, create tenders, invite suppliers,
// check offers for compliance and send them to the Evaluation Committee.
// This class is split into several files (SourcingOfficerController.*.cs).
[Authorize(Roles = RoleNames.SourcingOfficer)]
public partial class SourcingOfficerController(ApplicationDbContext db) : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public async Task<IActionResult> Dashboard()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Forbid();
        var ownTenderIds = db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .Select(t => t.TenderId);

        ViewBag.PendingRequests = await ReviewableRequests(userId).AsNoTracking()
            .CountAsync(r => r.Status == "Submitted" || r.Status == "UnderReview");
        ViewBag.PublishedTenders = await db.Tenders.AsNoTracking()
            .CountAsync(t => t.CreatedByUserId == userId && (t.Status == "Published" || t.Status == "Open"));
        ViewBag.InvitationsSent = await db.Invitations.AsNoTracking()
            .CountAsync(i => i.InvitedByUserId == userId);
        ViewBag.SubmittedOffers = await db.Bids.AsNoTracking()
            .CountAsync(b => ownTenderIds.Contains(b.TenderId) && b.SubmittedAt != null);
        var tenderStates = await db.Tenders.AsNoTracking().Where(t => t.CreatedByUserId == userId)
            .GroupBy(t => t.Status).Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count);
        ViewBag.TenderStates = tenderStates;
        ViewBag.EvaluationsInProgress = tenderStates.GetValueOrDefault("UnderEvaluation");
        ViewBag.PendingCompliance = await db.Bids.AsNoTracking()
            .CountAsync(b => ownTenderIds.Contains(b.TenderId) && b.SubmittedAt != null &&
                (b.Status == "Submitted" || b.Status == "UnderReview"));
        ViewBag.ReportsAwaitingOfficer = await db.EvaluationReports.AsNoTracking()
            .CountAsync(r => ownTenderIds.Contains(r.TenderId) && r.Status == "CommitteeApproved");
        ViewBag.ReportsAwaitingManager = await db.EvaluationReports.AsNoTracking()
            .CountAsync(r => ownTenderIds.Contains(r.TenderId) && r.Status == "OfficerSubmitted");

        return View();
    }
    public async Task<IActionResult> Requests(int? id, string status = "All", string? q = null, int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Forbid();

        var availableStatuses = new[] { "All", "Submitted", "UnderReview", "Approved", "ConvertedToTender", "Returned", "Rejected" };
        if (!availableStatuses.Contains(status)) status = "All";

        IQueryable<PurchaseRequest> requestQuery = ReviewableRequests(userId)
            .AsNoTracking()
            .Include(r => r.Department)
            .Include(r => r.ReviewedByUser);
        if (status != "All")
            requestQuery = requestQuery.Where(r => r.Status == status);

        q = q?.Trim();
        if (!string.IsNullOrWhiteSpace(q))
            requestQuery = requestQuery.Where(r => r.Title.Contains(q) || r.Department.Name.Contains(q) || r.PurchaseRequestId.ToString() == q);
        const int pageSize = 10;
        var total = await requestQuery.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        var requests = await requestQuery
            .OrderBy(r => r.Status == "Submitted" || r.Status == "UnderReview" ? 0 : r.Status == "Approved" && r.ReviewedByUserId == userId ? 1 : 2)
            .ThenByDescending(r => r.SubmittedAt ?? r.CreatedAt).ThenByDescending(r => r.PurchaseRequestId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.RequestTotal = total; ViewBag.RequestPage = page; ViewBag.RequestPages = pages; ViewBag.RequestQuery = q;

        PurchaseRequest? selectedRequest = null;
        if (id.HasValue)
        {
            selectedRequest = await ReviewableRequests(userId)
                .AsNoTracking()
                .Include(r => r.Department)
                .Include(r => r.RequestedByUser)
                .Include(r => r.ReviewedByUser)
                .Include(r => r.PurchaseRequestItems)
                .SingleOrDefaultAsync(r => r.PurchaseRequestId == id.Value);

            if (selectedRequest == null) return NotFound();

            ViewBag.HasOpenRequestClarifications = await db.Clarifications.AnyAsync(c =>
                c.PurchaseRequestId == id && c.Status != "Resolved");
        }

        ViewBag.SelectedRequest = selectedRequest;
        ViewBag.StatusFilter = status;
        ViewBag.CurrentUserId = userId;
        return View(requests);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Forbid();
        var request = await ReviewableRequests(userId).AsNoTracking()
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == id);
        if (request == null) return NotFound();
        return RedirectToAction(nameof(Requests), new { id, status = request.Status });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> StartReview(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Forbid();

        await using var auditTransaction = await db.Database.BeginTransactionAsync();
        var updatedRows = await db.PurchaseRequests
            .Where(r => r.PurchaseRequestId == id && r.Status == "Submitted" &&
                (r.ReviewedByUserId == null || r.ReviewedByUserId == userId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.Status, "UnderReview")
                .SetProperty(r => r.ReviewedByUserId, userId));
        // ExecuteUpdateAsync returns how many rows it changed. Anything but 1 means the record was changed by someone else or is no longer allowed.
        if (updatedRows != 1)
        {
            var assignedToAnotherOfficer = await db.PurchaseRequests.AsNoTracking()
                .AnyAsync(r => r.PurchaseRequestId == id && r.Status != "Draft" &&
                    r.ReviewedByUserId != null && r.ReviewedByUserId != userId);
            if (assignedToAnotherOfficer) return NotFound();
            TempData["RequestError"] = "This request is no longer available to start review.";
            return RedirectToAction(nameof(Requests), new { id, status = "Submitted" });
        }

        await HttpContext.RequestServices.GetRequiredService<IAuditLog>().WriteAsync("Modified", "PurchaseRequest", id.ToString(),
            "Purchase request review started.", changesJson: "{\"Before\":\"Submitted\",\"After\":\"UnderReview\"}");
        await auditTransaction.CommitAsync();
        TempData["RequestSuccess"] = "The request is now assigned to you for review.";
        return RedirectToAction(nameof(Requests), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ApproveRequest(int id, string? notes = null) =>
        SaveRequestDecision(id, "Approved", notes, notesRequired: false);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ReturnRequest(int id, string? reason) =>
        SaveRequestDecision(id, "Returned", reason, notesRequired: true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> RejectRequest(int id, string? reason) =>
        SaveRequestDecision(id, "Rejected", reason, notesRequired: true);

    private async Task<IActionResult> SaveRequestDecision(int id, string decision, string? notes, bool notesRequired)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Forbid();

        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if ((notesRequired && cleanNotes == null) || notes?.Trim().Length > 2000)
        {
            TempData["RequestError"] = notesRequired
                ? "Enter a reason of 1 to 2000 characters for Return or Reject."
                : "Review notes cannot exceed 2000 characters.";
            return RedirectToAction(nameof(Requests), new { id });
        }

        var request = await db.PurchaseRequests.AsNoTracking()
            .Where(r => r.PurchaseRequestId == id)
            .Select(r => new { r.Status, r.ReviewedByUserId, r.RequestedByUserId })
            .SingleOrDefaultAsync();
        if (request == null || request.Status == "Draft") return NotFound();
        if (request.ReviewedByUserId != userId && !(request.Status == "Submitted" && request.ReviewedByUserId == null)) return Forbid();
        if (request.Status is not ("UnderReview" or "Submitted"))
        {
            TempData["RequestError"] = "Only the assigned officer can decide a request that is UnderReview.";
            return RedirectToAction(nameof(Requests), new { id, status = request.Status });
        }

        var reviewedAt = DateTime.UtcNow;
        await using var auditTransaction = await db.Database.BeginTransactionAsync();
        var updatedRows = await db.PurchaseRequests
            .Where(r => r.PurchaseRequestId == id && ((r.Status == "UnderReview" && r.ReviewedByUserId == userId) || (r.Status == "Submitted" && r.ReviewedByUserId == null)) &&
                !r.Clarifications.Any(c => c.Status != "Resolved"))
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.Status, decision)
                .SetProperty(r => r.ReviewedByUserId, userId)
                .SetProperty(r => r.ReviewNotes, r => cleanNotes ?? r.ReviewNotes)
                .SetProperty(r => r.ReviewedAt, reviewedAt));
        // ExecuteUpdateAsync returns how many rows it changed. Anything but 1 means the record was changed by someone else or is no longer allowed.
        if (updatedRows != 1)
        {
            TempData["RequestError"] = await db.Clarifications.AnyAsync(c => c.PurchaseRequestId == id && c.Status != "Resolved")
                ? "Resolve all open clarifications before recording a review decision."
                : "This request changed before your decision was saved. Review its current status.";
            return RedirectToAction(nameof(Requests), new { id });
        }
        await HttpContext.RequestServices.GetRequiredService<IAuditLog>().WriteAsync("Modified", "PurchaseRequest", id.ToString(),
            "Purchase request review decision recorded.", changesJson: System.Text.Json.JsonSerializer.Serialize(new { Before = request.Status, After = decision }));
        if (decision == "Rejected")
        {
            db.Notifications.Add(new Notification
            {
                UserId = request.RequestedByUserId,
                Title = $"Purchase request #{id} rejected",
                Message = cleanNotes!,
                RelativeUrl = $"/Department/Details/{id}",
                IsRead = false,
                CreatedAt = reviewedAt
            });
            await db.SaveChangesAsync();
        }
        await auditTransaction.CommitAsync();

        TempData["RequestSuccess"] = $"Purchase Request {decision.ToLowerInvariant()} successfully.";
        return RedirectToAction(nameof(Requests), new { id, status = decision });
    }

    private IQueryable<PurchaseRequest> ReviewableRequests(string userId) =>
        db.PurchaseRequests.ReviewableBy(userId);
    public IActionResult Tenders(int? id) => RedirectToAction(nameof(Offers), new { tenderId = id });

    private async Task<IActionResult> LoadTenderForms()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        ViewBag.EligiblePurchaseRequests = await db.PurchaseRequests
            .AsNoTracking()
            .Where(r => (r.Status == "Approved" ||
                (r.Status == "UnderReview" && r.ReviewedByUserId == userId) ||
                (r.Status == "Submitted" && (r.ReviewedByUserId == null || r.ReviewedByUserId == userId))) &&
                !db.Tenders.Any(t => t.PurchaseRequestId == r.PurchaseRequestId))
            .OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt)
            .ThenByDescending(r => r.PurchaseRequestId)
            .ToListAsync();
        ViewBag.LaunchSuppliers = await db.SupplierProfiles.AsNoTracking().Where(s => s.Status == "Approved").OrderBy(s => s.CompanyName).ToListAsync();
        ViewBag.CreateTender = new SourcingOfficerTenderForm();
        return await Offers(null, null);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? purchaseRequestId, int? replacesTenderId = null)
    {
        if (replacesTenderId.HasValue)
        {
            var source = await db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.TenderId == replacesTenderId &&
                t.CreatedByUserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && t.Status == "Closed" && t.RejectionForwardedAt != null &&
                t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"));
            if (source == null) return NotFound();
            if (await db.Tenders.AnyAsync(t => t.ReplacesTenderId == source.TenderId)) return Conflict("A replacement already exists.");
            var replacement = new SourcingOfficerTenderForm { ReplacesTenderId = source.TenderId,
                PurchaseRequestId = source.PurchaseRequestId, OpensAt = DateTime.UtcNow, ClosesAt = DateTime.UtcNow.AddDays(7) };
            replacement.LoadCategory(source.Category);
            return await ShowCreateTender(replacement);
        }
        var result = await LoadTenderForms();
        var eligibleRequests = (List<PurchaseRequest>)ViewBag.EligiblePurchaseRequests;
        if (purchaseRequestId.HasValue && !eligibleRequests.Any(r => r.PurchaseRequestId == purchaseRequestId.Value))
            return NotFound();

        var request = purchaseRequestId.HasValue
            ? await db.PurchaseRequests.AsNoTracking().Include(r => r.PurchaseRequestItems)
                .SingleOrDefaultAsync(r => r.PurchaseRequestId == purchaseRequestId.Value)
            : null;
        if (purchaseRequestId.HasValue && request == null) return NotFound();
        return await ShowCreateTender(new SourcingOfficerTenderForm { PurchaseRequestId = purchaseRequestId ?? 0,
            Title = request?.Title ?? "", Description = request == null ? "" : request.Reason + "\n" + string.Join("\n", request.PurchaseRequestItems.Select(i => $"{i.ItemName}: {i.Quantity:0.###} {i.Unit}. {i.Specifications}")),
            OpensAt = DateTime.UtcNow, ClosesAt = DateTime.UtcNow.AddDays(7) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PurchaseRequestId,Title,Description,Terms,OpensAt,ClosesAt,Category,OtherCategory,ReplacesTenderId")] SourcingOfficerTenderForm tender, bool launch = true, List<int>? supplierProfileIds = null, bool allSuppliers = false)
    {
        if (tender.PurchaseRequestId <= 0)
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "Select an eligible purchase request.");
        if (string.IsNullOrWhiteSpace(tender.Title))
            ModelState.AddModelError(nameof(Tender.Title), "Enter a tender title.");
        else if (tender.Title.Trim().Length > 200)
            ModelState.AddModelError(nameof(Tender.Title), "The title cannot exceed 200 characters.");
        if (string.IsNullOrWhiteSpace(tender.Description))
            ModelState.AddModelError(nameof(Tender.Description), "Enter a tender description.");
        else if (tender.Description.Trim().Length > 4000)
            ModelState.AddModelError(nameof(Tender.Description), "The description cannot exceed 4000 characters.");
        if (tender.Terms?.Length > 4000)
            ModelState.AddModelError(nameof(Tender.Terms), "Terms cannot exceed 4000 characters.");
        if (tender.OpensAt == default)
            ModelState.AddModelError(nameof(Tender.OpensAt), "Enter the opening date and time.");
        if (tender.ClosesAt == default)
            ModelState.AddModelError(nameof(Tender.ClosesAt), "Enter the tender deadline.");
        else if (tender.ClosesAt <= tender.OpensAt || tender.ClosesAt <= DateTime.UtcNow)
            ModelState.AddModelError(nameof(Tender.ClosesAt), "The tender deadline must be in the future and later than the opening time.");

        if (!ModelState.IsValid) return await ShowCreateTender(tender);

        await using var transaction = await db.Database.BeginTransactionAsync();
        Tender? rejectedSource = null;
        if (tender.ReplacesTenderId.HasValue)
        {
            // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @source int; SELECT @source = TenderId FROM dbo.Tenders WITH (UPDLOCK,HOLDLOCK) WHERE TenderId = {tender.ReplacesTenderId.Value};");
            rejectedSource = await db.Tenders.SingleOrDefaultAsync(t => t.TenderId == tender.ReplacesTenderId && t.CreatedByUserId == User.FindFirstValue(ClaimTypes.NameIdentifier) &&
                t.Status == "Closed" && t.RejectionForwardedAt != null && t.PurchaseRequestId == tender.PurchaseRequestId &&
                t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"));
            if (rejectedSource == null) return NotFound();
            if (await db.Tenders.AnyAsync(t => t.ReplacesTenderId == rejectedSource.TenderId))
                return Conflict("A replacement may be created only once.");
        }
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = PurchaseRequestId FROM dbo.PurchaseRequests WITH (UPDLOCK, HOLDLOCK) WHERE PurchaseRequestId = {tender.PurchaseRequestId};");
        var request = await db.PurchaseRequests
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == tender.PurchaseRequestId);
        if (request == null)
        {
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "The selected purchase request was not found.");
            return await ShowCreateTender(tender);
        }
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (rejectedSource == null && request.Status != "Approved" &&
            !(request.Status == "UnderReview" && request.ReviewedByUserId == officerId) &&
            !(request.Status == "Submitted" && (request.ReviewedByUserId == null || request.ReviewedByUserId == officerId)))
        {
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "The selected purchase request is no longer eligible for a tender.");
            return await ShowCreateTender(tender);
        }
        if (rejectedSource == null && await db.Tenders.AnyAsync(t => t.PurchaseRequestId == tender.PurchaseRequestId))
        {
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "A tender already exists for this purchase request.");
            return await ShowCreateTender(tender);
        }

        if (await db.Clarifications.AnyAsync(c => c.PurchaseRequestId == request.PurchaseRequestId && c.Status != "Resolved"))
        {
            ModelState.AddModelError("", "Resolve the request’s open questions before preparing its tender.");
            return await ShowCreateTender(tender);
        }
        var eligibleSuppliers = SuppliersForCategory(tender.SelectedCategory);
        var requestedIds = (supplierProfileIds ?? []).Distinct().ToList();
        var selectedSuppliers = await eligibleSuppliers.Where(s => allSuppliers || requestedIds.Contains(s.SupplierProfileId))
            .Select(s => s.SupplierProfileId).ToListAsync();
        if (tender.ClosesAt <= DateTime.UtcNow || selectedSuppliers.Count == 0 ||
            (!allSuppliers && selectedSuppliers.Count != requestedIds.Count))
        {
            ModelState.AddModelError("", "Choose approved suppliers in the tender category and a future deadline before publishing.");
            return await ShowCreateTender(tender);
        }
        if (request.Status is "UnderReview" or "Submitted") { request.ReviewedAt = DateTime.UtcNow; request.ReviewedByUserId = officerId; }
        request.Status = "ConvertedToTender";
        var createdTender = new Tender
        {
            ReplacesTenderId = rejectedSource?.TenderId,
            PurchaseRequestId = request.PurchaseRequestId,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            Title = tender.Title.Trim(),
            Category = tender.SelectedCategory,
            Description = tender.Description.Trim(),
            Terms = string.IsNullOrWhiteSpace(tender.Terms) ? null : tender.Terms.Trim(),
            OpensAt = tender.OpensAt,
            ClosesAt = tender.ClosesAt,
            Status = "Published"
        };
        foreach (var supplierId in selectedSuppliers)
            createdTender.Invitations.Add(new Invitation { SupplierProfileId = supplierId, InvitedByUserId = officerId!, Status = "Pending", SentAt = DateTime.UtcNow });
        db.Tenders.Add(createdTender);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["TenderSuccess"] = "Tender published and suppliers invited. Their offers will appear here.";
        return RedirectToAction(nameof(Tenders), new { id = createdTender.TenderId });
    }

    private async Task<IActionResult> ShowCreateTender(SourcingOfficerTenderForm tender)
    {
        var result = await LoadTenderForms();
        if (tender.ReplacesTenderId.HasValue)
        {
            var request = await db.Tenders.AsNoTracking()
                .Where(t => t.TenderId == tender.ReplacesTenderId && t.CreatedByUserId == User.FindFirstValue(ClaimTypes.NameIdentifier) &&
                    t.Status == "Closed" && t.RejectionForwardedAt != null && t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected"))
                .Select(t => t.PurchaseRequest).SingleOrDefaultAsync();
            if (request == null) return NotFound();
            var requests = (List<PurchaseRequest>)ViewBag.EligiblePurchaseRequests;
            if (!requests.Any(r => r.PurchaseRequestId == request.PurchaseRequestId)) requests.Add(request);
        }
        ViewBag.CreateTender = tender;
        ViewBag.OpenCreateModal = true;
        return result;
    }

    // Keep old edit links on the tender workspace without exposing an edit flow.
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await db.Tenders.AnyAsync(t => t.TenderId == id && t.CreatedByUserId == userId)) return NotFound();
        return RedirectToAction(nameof(Offers), new { tenderId = id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Edit(SourcingOfficerTenderForm input) => NotFound();

    // Legacy clients can still reach the workspace; publication happens during creation.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishTender(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await db.Tenders.AnyAsync(t => t.TenderId == id && t.CreatedByUserId == userId)) return NotFound();
        return RedirectToAction(nameof(Offers), new { tenderId = id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishAndInvite(int id, List<int>? supplierProfileIds, bool allSuppliers = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};");
        var tender = await db.Tenders.Include(t => t.Invitations).SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        var requestedIds = (supplierProfileIds ?? []).Distinct().ToList();
        var suppliers = await SuppliersForCategory(tender.Category)
            .Where(s => allSuppliers || requestedIds.Contains(s.SupplierProfileId))
            .Select(s => s.SupplierProfileId).ToListAsync();
        if (tender.Status is not ("Published" or "Open") || tender.ClosesAt <= DateTime.UtcNow || tender.ClosesAt <= tender.OpensAt ||
            suppliers.Count == 0 || (!allSuppliers && suppliers.Count != requestedIds.Count))
        {
            TempData["TenderError"] = "Choose approved suppliers in the tender category and ensure this tender has a future deadline.";
            return RedirectToAction(nameof(Offers), new { tenderId = id });
        }
        tender.Status = "Published";
        foreach (var supplier in suppliers.Where(s => !tender.Invitations.Any(i => i.SupplierProfileId == s)))
            tender.Invitations.Add(new Invitation { SupplierProfileId = supplier, InvitedByUserId = userId!, Status = "Pending", SentAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["TenderSuccess"] = "Suppliers invited. Submitted offers will appear below.";
        return RedirectToAction(nameof(Offers), new { tenderId = id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelTender(int id, bool fromList = false, bool declineReplacement = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {id};");
        var tender = await db.Tenders.SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();

        var canCancel = declineReplacement
            ? tender.Status == "Closed" && tender.RejectionForwardedAt != null &&
              await db.TenderApprovals.AnyAsync(a => a.TenderId == id && a.Stage == "Top" && a.Decision == "Rejected") &&
              !await db.Tenders.AnyAsync(t => t.ReplacesTenderId == id)
            : tender.CanCancel(DateTime.UtcNow);
        if (!canCancel)
        {
            TempData["TenderError"] = declineReplacement ? "This replacement can no longer be declined." : "Only an active tender can be canceled before its deadline.";
        }
        else
        {
            tender.Status = "Cancelled";
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["TenderSuccess"] = declineReplacement ? "Replacement declined." : "Tender canceled.";
        }

        return fromList ? RedirectToAction(nameof(Tenders)) : RedirectToAction(nameof(Offers), new { tenderId = id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SendToEvaluation(int id) => ReviewAndSend(id, new Dictionary<int, string>());

    private async Task<bool> HasCompletedComplianceForEvaluation(int tenderId)
    {
        if (await db.TenderApprovals.AnyAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected")) return false;
        var submittedBids = db.Bids.AsNoTracking()
            .Where(b => b.TenderId == tenderId && b.SubmittedAt != null && b.Status != "Withdrawn");
        var hasCompliantBid = await submittedBids.AnyAsync(b => b.ComplianceStatus == "Compliant");
        var hasUnresolvedDecision = await submittedBids.AnyAsync(b =>
            b.ComplianceStatus != "Compliant" && b.ComplianceStatus != "NonCompliant");
        return hasCompliantBid && !hasUnresolvedDecision;
    }

    private IQueryable<SupplierProfile> SuppliersForCategory(string? category)
    {
        var approved = db.SupplierProfiles.Where(s => s.Status == "Approved");
        return category == null ? approved : approved.Where(s =>
            EF.Functions.Collate(s.Category!, "Latin1_General_100_CI_AS") == category);
    }

    public async Task<IActionResult> Invitations(int? tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (tenderId.HasValue && !await db.Tenders.AnyAsync(t => t.TenderId == tenderId && t.CreatedByUserId == userId)) return NotFound();
        ViewBag.SelectedTenderId = tenderId;
        var invitations = await db.Invitations
            .AsNoTracking()
            .Where(i => i.InvitedByUserId == userId && (!tenderId.HasValue || i.TenderId == tenderId))
            .Include(i => i.Tender)
            .Include(i => i.SupplierProfile)
            .OrderByDescending(i => i.SentAt)
            .ToListAsync();

        ViewBag.EligibleTenders = await db.Tenders
            .AsNoTracking()
            .Where(t => t.CreatedByUserId == userId && (t.Status == "Published" || t.Status == "Open") && t.ClosesAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        ViewBag.LaunchSuppliers = await db.SupplierProfiles
            .AsNoTracking()
            .Where(s => s.Status == "Approved")
            .OrderBy(s => s.CompanyName)
            .ToListAsync();

        return View(invitations);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInvitation(int tenderId, List<int>? supplierProfileIds, bool allSuppliers = false)
    {
        if (!ModelState.IsValid || tenderId <= 0 || (!allSuppliers && (supplierProfileIds?.Count ?? 0) == 0))
        {
            TempData["InvitationError"] = "Select a tender and at least one approved supplier.";
            return RedirectToAction(nameof(Invitations), new { tenderId });
        }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (tender.Status is not ("Published" or "Open") || tender.ClosesAt <= DateTime.UtcNow)
        {
            TempData["InvitationError"] = "Choose one of your published or open tenders whose submission deadline has not passed.";
            return RedirectToAction(nameof(Invitations), new { tenderId });
        }
        var selectedIds = (supplierProfileIds ?? []).Distinct().ToList();
        await using var invitationTransaction = db.Database.CurrentTransaction is null
            // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        var approvedIds = await SuppliersForCategory(tender.Category)
            .Where(s => allSuppliers || selectedIds.Contains(s.SupplierProfileId))
            .Select(s => s.SupplierProfileId).ToListAsync();
        if (approvedIds.Count == 0 || (!allSuppliers && approvedIds.Count != selectedIds.Count))
        {
            TempData["InvitationError"] = "Select approved suppliers in the tender category.";
            return RedirectToAction(nameof(Invitations), new { tenderId });
        }
        var existingIds = await db.Invitations.Where(i => i.TenderId == tenderId)
            .Select(i => i.SupplierProfileId).ToListAsync();
        var invitations = approvedIds.Except(existingIds).Select(supplierId => new Invitation
        {
            TenderId = tenderId, SupplierProfileId = supplierId, InvitedByUserId = userId!,
            Status = "Pending", SentAt = DateTime.UtcNow
        }).ToList();
        if (invitations.Count == 0)
        {
            TempData["InvitationSuccess"] = "All selected suppliers have already been invited. No duplicate invitations were created.";
            return RedirectToAction(nameof(Invitations), new { tenderId });
        }
        db.Invitations.AddRange(invitations);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            foreach (var invitation in invitations) db.Entry(invitation).State = EntityState.Detached;
            TempData["InvitationError"] = "The invitations could not be saved. Refresh and try again; a supplier may have just been invited.";
            return RedirectToAction(nameof(Invitations), new { tenderId });
        }
        if (invitationTransaction is not null) await invitationTransaction.CommitAsync();
        TempData["InvitationSuccess"] = $"Sent {invitations.Count} invitation(s). Suppliers already invited were skipped.";
        return RedirectToAction(nameof(Invitations), new { tenderId });
    }

    public async Task<IActionResult> Offers(int? id, int? tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenders = await db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .OrderByDescending(t => t.Status == "Published" || t.Status == "Open" || t.Status == "Closed")
            .ThenByDescending(t => t.CreatedAt).ToListAsync();
        if (id.HasValue)
        {
            var bid = await db.Bids.AsNoTracking().SingleOrDefaultAsync(b => b.BidId == id && b.SubmittedAt != null
                && db.Tenders.Any(t => t.TenderId == b.TenderId && t.CreatedByUserId == userId));
            if (bid == null) return NotFound();
            tenderId = bid.TenderId;
        }
        tenderId ??= tenders.FirstOrDefault()?.TenderId;
        if (tenderId.HasValue && !tenders.Any(t => t.TenderId == tenderId)) return NotFound();
        var bids = await db.Bids.AsNoTracking().Where(b => b.TenderId == tenderId && b.SubmittedAt != null)
            .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
            .Include(b => b.BidDocuments).OrderBy(b => b.BidId).ToListAsync();
        var selectedTender = tenders.SingleOrDefault(t => t.TenderId == tenderId);
        ViewData["AiChatRecordId"] = selectedTender?.TenderId;
        // Used by the offer pre-check to compare uploaded documents with the tender's mandatory documents.
        ViewBag.MandatoryDocuments = tenderId.HasValue ? await db.TenderDocuments.CountAsync(d => d.TenderId == tenderId && d.IsMandatory) : 0;
        if (selectedTender != null)
            selectedTender.PurchaseRequest = await db.PurchaseRequests.AsNoTracking()
                .Include(r => r.PurchaseRequestItems).SingleAsync(r => r.PurchaseRequestId == selectedTender.PurchaseRequestId);
        if (selectedTender != null)
            ViewBag.SpecMatch = await LoadSpecMatchAsync(selectedTender, bids, HttpContext.RequestAborted);
        ViewBag.LaunchSuppliers = await SuppliersForCategory(selectedTender?.Category)
            .AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync();
        ViewBag.WorkspaceInvitations = tenderId.HasValue ? await db.Invitations.AsNoTracking()
            .Where(i => i.TenderId == tenderId).Include(i => i.SupplierProfile).OrderBy(i => i.SupplierProfile.CompanyName).ToListAsync() : new List<Invitation>();
        ViewBag.WorkspaceReport = tenderId.HasValue ? await db.EvaluationReports.AsNoTracking()
            .Include(r => r.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
            .SingleOrDefaultAsync(r => r.TenderId == tenderId) : null;
        ViewBag.WorkspaceMembers = tenderId.HasValue ? await db.CommitteeAssignments.AsNoTracking().Where(a => a.TenderId == tenderId && a.CommitteeType == "Evaluation").Include(a => a.User).OrderBy(a => a.Position).ToListAsync() : new List<CommitteeAssignment>();
        ViewBag.WorkspaceApprovals = tenderId.HasValue ? await db.TenderApprovals.AsNoTracking().Where(a => a.TenderId == tenderId && a.Stage == "Evaluation").ToListAsync() : new List<TenderApproval>();
        ViewBag.WorkspaceReviewCounts = tenderId.HasValue ? await db.Evaluations.AsNoTracking()
            .Where(e => e.TenderId == tenderId && e.Status == "Approved" && e.ApprovedAt != null &&
                e.FinancialMark != null && e.TechnicalMark != null && e.Bid.ComplianceStatus == "Compliant" && e.Bid.Status != "Withdrawn")
            .GroupBy(e => e.CommitteeAssignmentId).Select(group => new { AssignmentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.AssignmentId, row => row.Count) : new Dictionary<int, int>();
        ViewBag.OfferTenders = tenders;
        if (selectedTender != null)
            selectedTender.OfferExtensions = await db.Set<TenderOfferExtension>().AsNoTracking().Where(e => e.TenderId == selectedTender.TenderId).OrderBy(e => e.Round).ToListAsync();
        ViewBag.RoundRejected = tenderId.HasValue && await db.TenderApprovals.AnyAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected");
        ViewBag.SelectedTender = selectedTender;
        ViewBag.SelectedBidId = id;
        ViewBag.RoundRejected = tenderId.HasValue && await db.TenderApprovals.AnyAsync(a =>
            a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected");
        ViewBag.OpenQuestions = await db.Clarifications.AsNoTracking()
            .Where(c => c.Status != "Resolved" && ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn")))
            .Select(c => c.ClarificationId).ToListAsync();
        ViewBag.ForwardedRejection = selectedTender?.RejectionForwardedAt != null
            ? await db.TenderApprovals.AsNoTracking().Include(a => a.User)
                .FirstOrDefaultAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected") : null;
        ViewBag.ReplacementTender = selectedTender?.RejectionForwardedAt != null
            ? await db.Tenders.AsNoTracking().FirstOrDefaultAsync(t => t.ReplacesTenderId == tenderId && t.CreatedByUserId == userId) : null;
        if (selectedTender?.TopEditState != null)
        {
            ViewBag.TopEditRequesterName = !string.IsNullOrWhiteSpace(selectedTender.TopEditRequestedByUserId)
                ? await db.Users.AsNoTracking().Where(u => u.Id == selectedTender.TopEditRequestedByUserId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "CEO & Chiefs Member"
                : "CEO & Chiefs Member";
            ViewBag.CompliantOffersForSwitch = bids
                .Where(b => b.ComplianceStatus == "Compliant" && b.Status is not ("Draft" or "Withdrawn" or "Rejected" or "NonCompliant"))
                .OrderBy(b => b.BidItems.Sum(x => x.Quantity * x.UnitPrice) + b.TaxAmount + b.AdditionalCosts)
                .ToList();
        }
        return View("Offers", bids);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewAndSend(int tenderId, Dictionary<int, string> decisions, bool saveOnly = false, string? shortfallReason = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (await db.TenderApprovals.AnyAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected"))
            return Conflict("This voting round was rejected. Create a replacement tender after the manager forwards it.");
        // A stale tender status must never restart an existing committee cycle.
        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId) ||
            await db.Evaluations.AnyAsync(e => e.TenderId == tenderId))
        {
            TempData["TenderError"] = "These offers have already been sent to the committee. View the committee report and decisions below.";
            return RedirectToAction(nameof(Offers), new { tenderId });
        }
        var bids = await db.Bids.Include(b => b.Invitation)
            .Where(b => b.TenderId == tenderId && b.SubmittedAt != null && b.Status != "Withdrawn").ToListAsync();
        string? error = null;
        if (tender.Status is not ("Published" or "Open" or "Closed")) error = "This tender has already moved beyond offer review.";
        else if (!saveOnly && tender.ClosesAt > DateTime.UtcNow) error = "Wait until the tender deadline passes before sending offers to the committee.";
        else if (bids.Count == 0) error = "At least one submitted offer is required.";
        else if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
            ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn"))))
            error = "Resolve the open questions shown below before sending to the committee.";
        else error = ValidateOfferDecisions(bids, decisions, requireCompliant: false);
        if (error != null)
        {
            TempData["TenderError"] = error;
            return RedirectToAction(nameof(Offers), new { tenderId });
        }
        foreach (var bid in bids.Where(b => b.ComplianceStatus is not ("Compliant" or "NonCompliant")))
        {
            bid.ComplianceStatus = decisions[bid.BidId];
            bid.Status = bid.ComplianceStatus;
            bid.ComplianceReviewedByUserId = userId;
            bid.ComplianceReviewedAt = DateTime.UtcNow;
        }
        var compliant = bids.Where(b => b.ComplianceStatus == "Compliant")
            .Select(b => b.Invitation.SupplierProfileId).Distinct().Count();
        if (saveOnly)
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["ComplianceSuccess"] = "Compliance decisions saved.";
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        var round = 1 + await db.Set<TenderOfferExtension>().CountAsync(e => e.TenderId == tenderId);
        shortfallReason = shortfallReason?.Trim();
        if (compliant == 0 || (compliant < TenderOfferExtension.RequiredCompliantOffers &&
            (round < TenderOfferExtension.MaximumRound || string.IsNullOrWhiteSpace(shortfallReason) || shortfallReason.Length > 2000)))
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["TenderError"] = compliant < TenderOfferExtension.RequiredCompliantOffers && round < TenderOfferExtension.MaximumRound
                ? $"There are {compliant} compliant offers. Extend the deadline and contact suppliers before sending to the committee."
                : compliant == 0 ? "At least one compliant offer is required."
                : "Enter a reason of up to 2,000 characters for sending fewer than three compliant offers.";
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        tender.OfferShortfallReason = compliant < TenderOfferExtension.RequiredCompliantOffers ? shortfallReason : null;
        var setupError = await SourcingTenderManagement.Services.Evaluation.CommitteeSetup.Prepare(db, tender);
        if (setupError != null)
        {
            TempData["TenderError"] = setupError;
            return RedirectToAction(nameof(Offers), new { tenderId });
        }
        tender.Status = "UnderEvaluation";
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["TenderSuccess"] = "Sent to the committee. Its members can now review the offers and choose a supplier.";
        return RedirectToAction(nameof(Offers), new { tenderId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOfferOpened(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var offer = db.Bids.Where(b => b.BidId == id && b.SubmittedAt != null &&
            db.Tenders.Any(t => t.TenderId == b.TenderId && t.CreatedByUserId == userId));
        if (!await offer.AnyAsync()) return NotFound();
        await offer.Where(b => b.OfficerOpenedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.OfficerOpenedAt, DateTime.UtcNow));
        return NoContent();
    }

    internal static string? ValidateOfferDecisions(IReadOnlyList<Bid> bids, IReadOnlyDictionary<int, string> decisions, bool requireCompliant = true)
    {
        if (bids.Count == 0) return "At least one submitted offer is required.";
        foreach (var bid in bids)
        {
            if (bid.ComplianceStatus is "Compliant" or "NonCompliant") continue;
            if (bid.Status is not ("Submitted" or "UnderReview") ||
                !decisions.TryGetValue(bid.BidId, out var decision) || decision is not ("Compliant" or "NonCompliant"))
                return "Choose Compliant or Non-compliant for every offer. Nothing has been sent.";
        }
        return !requireCompliant || bids.Any(b => b.ComplianceStatus == "Compliant" ||
            (b.ComplianceStatus != "NonCompliant" && decisions.GetValueOrDefault(b.BidId) == "Compliant"))
            ? null : "At least one offer must be compliant to continue.";
    }

    public IActionResult Compliance(int? id) => RedirectToAction(nameof(Offers), new { id });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewCompliance(int id, string decision, string? notes)
    {
        if (decision != "Compliant" && decision != "NonCompliant")
        {
            TempData["ComplianceError"] = "Select a supported compliance decision.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (notes?.Trim().Length > 2000)
        {
            TempData["ComplianceError"] = "Compliance notes cannot exceed 2000 characters.";
            return RedirectToAction(nameof(Compliance), new { id });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenderId = await db.Bids.AsNoTracking()
            .Where(b => b.BidId == id && db.Tenders.Any(t => t.TenderId == b.TenderId && t.CreatedByUserId == userId))
            .Select(b => (int?)b.TenderId).SingleOrDefaultAsync();
        if (tenderId == null) return NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync();
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId.Value};");
        var bid = await db.Bids
            .Include(b => b.Clarifications).ThenInclude(c => c.ClarificationResponses)
            .SingleOrDefaultAsync(b => b.BidId == id && b.SubmittedAt != null &&
                db.Tenders.Any(t => t.TenderId == b.TenderId && t.CreatedByUserId == userId));
        if (bid == null) return NotFound();

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId) ||
            await db.Evaluations.AnyAsync(e => e.TenderId == tenderId))
        {
            TempData["ComplianceError"] = "Compliance decisions are locked after offers are sent to the committee.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (bid.ComplianceStatus is "Compliant" or "NonCompliant")
        {
            TempData["ComplianceError"] = "A final compliance decision has already been recorded and cannot be changed.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (bid.Status is not ("Submitted" or "UnderReview") ||
            !await db.Tenders.AnyAsync(t => t.TenderId == tenderId &&
                (t.Status == "Open" || t.Status == "Published" || t.Status == "Closed") &&
                !t.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected")))
        {
            TempData["ComplianceError"] = "This offer is not waiting for compliance review.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (bid.ComplianceStatus == "Compliant" || bid.ComplianceStatus == "NonCompliant")
        {
            TempData["ComplianceError"] = "A final compliance decision has already been recorded.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (bid.Clarifications.Any(c => c.Status != "Resolved"))
        {
            TempData["ComplianceError"] = "Both sides must resolve open offer clarifications before completing compliance review.";
            return RedirectToAction(nameof(Compliance), new { id });
        }

        bid.ComplianceStatus = decision;
        bid.Status = decision;
        bid.ComplianceNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        bid.ComplianceReviewedByUserId = userId;
        bid.ComplianceReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["ComplianceSuccess"] = "Compliance decision saved.";
        return RedirectToAction(nameof(Compliance), new { id });
    }
    public async Task<IActionResult> Comparison(int? tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenders = await db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId && t.Invitations.Any(i => i.Bids.Any(b => b.SubmittedAt != null)))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        int? selectedTenderId = tenderId ?? tenders.FirstOrDefault()?.TenderId;
        if (selectedTenderId.HasValue && tenders.All(t => t.TenderId != selectedTenderId.Value)) return NotFound();

        var bids = selectedTenderId.HasValue
            ? await db.Bids.AsNoTracking()
                .Where(b => b.TenderId == selectedTenderId.Value && b.SubmittedAt != null &&
                    db.Tenders.Any(t => t.TenderId == b.TenderId && t.CreatedByUserId == userId))
                .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
                .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
                .OrderBy(b => b.BidId)
                .ToListAsync()
            : [];

        ViewBag.ComparisonTenders = tenders;
        ViewBag.SelectedTenderId = selectedTenderId;
        ViewBag.ComparisonCriteria = selectedTenderId.HasValue
            ? await db.EvaluationCriteria.AsNoTracking().Where(c => c.TenderId == selectedTenderId.Value).OrderBy(c => c.Category).ThenBy(c => c.Name).ToListAsync()
            : new List<EvaluationCriterion>();
        return View(bids);
    }
    public async Task<IActionResult> Evaluations(int? tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenders = await db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        int? selectedTenderId = tenderId ?? tenders.FirstOrDefault()?.TenderId;
        if (selectedTenderId.HasValue && tenders.All(t => t.TenderId != selectedTenderId.Value)) return NotFound();

        ViewBag.EvaluationTenders = tenders;
        ViewBag.SelectedTenderId = selectedTenderId;
        ViewBag.SelectedTenderStatus = selectedTenderId.HasValue
            ? tenders.Single(t => t.TenderId == selectedTenderId.Value).Status
            : null;
        ViewBag.CommitteeAssignments = selectedTenderId.HasValue
            ? await db.CommitteeAssignments.AsNoTracking()
                .Where(a => a.TenderId == selectedTenderId.Value && a.CommitteeType == "Evaluation")
                .Include(a => a.User)
                .OrderBy(a => a.Position)
                .ToListAsync()
            : new List<CommitteeAssignment>();
        ViewBag.EvaluationApprovals = selectedTenderId.HasValue
            ? await db.TenderApprovals.AsNoTracking()
                .Where(a => a.TenderId == selectedTenderId.Value && a.Stage == "Evaluation")
                .Include(a => a.CommitteeAssignment).ThenInclude(a => a!.User)
                .Include(a => a.User)
                .OrderBy(a => a.CommitteeAssignment!.Position)
                .ToListAsync()
            : new List<TenderApproval>();
        ViewBag.EvaluationCriteria = selectedTenderId.HasValue
            ? await db.EvaluationCriteria.AsNoTracking()
                .Where(c => c.TenderId == selectedTenderId.Value)
                .OrderBy(c => c.Category).ThenBy(c => c.Name)
                .ToListAsync()
            : new List<EvaluationCriterion>();
        ViewBag.Evaluations = selectedTenderId.HasValue
            ? await db.Evaluations.AsNoTracking()
                .Where(e => e.TenderId == selectedTenderId.Value)
                .Include(e => e.Bid).ThenInclude(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
                .Include(e => e.CommitteeAssignment).ThenInclude(a => a.User)
                .Include(e => e.EvaluationScores).ThenInclude(s => s.EvaluationCriterion)
                .OrderBy(e => e.BidId).ThenBy(e => e.CommitteeAssignment.Position)
                .ToListAsync()
            : new List<Evaluation>();
        ViewBag.EvaluationReport = selectedTenderId.HasValue
            ? await db.EvaluationReports.AsNoTracking().SingleOrDefaultAsync(r => r.TenderId == selectedTenderId.Value)
            : null;
        ViewBag.RecommendationBids = selectedTenderId.HasValue
            ? await db.Bids.AsNoTracking()
                .Where(b => b.TenderId == selectedTenderId.Value && b.Status == "Compliant" && b.ComplianceStatus == "Compliant")
                .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
                .OrderBy(b => b.BidId)
                .ToListAsync()
            : new List<Bid>();
        return View();
    }

    [NonAction]
    public static string ReportReviewVersion(EvaluationReport report) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new { report.EvaluationReportId, report.CommitteeSummary, report.RecommendedBidId, report.Status, report.SubmittedAt }))));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewCommitteeReport(int tenderId, string decision, string? reason, string version, bool fromReports = false)
    {
        IActionResult ReviewDestination() => fromReports ? RedirectToAction(nameof(Reports)) : RedirectToAction(nameof(Offers), new { tenderId });
        IActionResult Error(string message) { TempData["EvaluationError"] = message; return ReviewDestination(); }
        if (!ModelState.IsValid || decision is not ("Approve" or "RequestEdits" or "Reject")) return Error("Choose a supported report decision.");
        if (reason?.Length > 4000 || (decision != "Approve" && string.IsNullOrWhiteSpace(reason)))
            return Error("Enter a reason of up to 4,000 characters when requesting edits or rejecting.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.Include(t => t.EvaluationReport).Include(t => t.CommitteeAssignments).Include(t => t.TenderApprovals)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        var report = tender.EvaluationReport;
        if (tender.Status != "AwaitingOfficerSummary" || report?.Status != "CommitteeApproved")
            return Error("This report is no longer awaiting your review. Refresh the page.");
        if (version != ReportReviewVersion(report)) return Error("The report changed. Refresh and review the current report before deciding.");
        var members = tender.CommitteeAssignments.Where(a => a.CommitteeType == "Evaluation").ToList();
        if (members.Count != 4 || members.Any(a => !tender.TenderApprovals.Any(p => p.Stage == "Evaluation" && p.Decision == "Approved" && p.CommitteeAssignmentId == a.CommitteeAssignmentId)))
            return Error("All four committee members must approve the report first.");
        if (string.IsNullOrWhiteSpace(report.CommitteeSummary) || !report.RecommendedBidId.HasValue ||
            !await db.Bids.AnyAsync(b => b.BidId == report.RecommendedBidId && b.TenderId == tenderId && b.ComplianceStatus == "Compliant" && b.Status == "Compliant"))
            return Error("The approved report must include an eligible recommended offer.");
        report.OfficerSummary = decision == "Approve" ? "Approved by sourcing officer. Report and recommended offer forwarded unchanged." :
            (decision == "RequestEdits" ? "Edits requested: " : "Rejected by sourcing officer: ") + reason!.Trim();
        if (await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
            ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn"))))
            return Error("Both sides must resolve open tender and offer clarifications before completing report review.");

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
            db.CommitteeTieVotes.RemoveRange(await db.CommitteeTieVotes.Where(v => v.TenderId == tenderId).ToListAsync());
            db.TenderApprovals.RemoveRange(tender.TenderApprovals.Where(a => a.Stage == "Evaluation"));
            foreach (var evaluation in await db.Evaluations.Where(e => e.TenderId == tenderId).ToListAsync()) { evaluation.Status = "Submitted"; evaluation.ApprovedAt = null; }
        }
        else tender.Status = "Cancelled";
        foreach (var recipient in decision == "Approve" ? await db.Users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingManager))).Select(u => u.Id).ToListAsync() : members.Select(a => a.UserId).ToList())
            db.Notifications.Add(new Notification { UserId = recipient, Title = "Committee report review", Message = $"{tender.Title}: officer decision — {decision}. Open the report to review the details.", RelativeUrl = decision == "Approve" ? "/SourcingManager/Submissions" : $"/EvaluationCommittee/Evaluations/{tenderId}", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["EvaluationSuccess"] = decision switch { "Approve" => "Approved. The unchanged report was sent to the Sourcing Manager.", "RequestEdits" => "Returned to the committee for edits and four fresh approvals.", _ => "Report rejected and tender cancelled." };
        return ReviewDestination();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondToTopEdit(int tenderId, int selectedBidId, string? officerNotes)
    {
        IActionResult Fail(string msg)
        {
            TempData["EvaluationError"] = msg;
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        var trimmed = officerNotes?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            return Fail("Enter your response or offer-selection justification (1–4,000 characters).");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");

        var tender = await db.Tenders
            .Include(t => t.EvaluationReport)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();

        var report = tender.EvaluationReport;
        if (tender.Status != "AwaitingOfficerSummary" || tender.TopEditState != "WithOfficer" || report == null)
            return Fail("This tender is not currently awaiting your edit response.");

        var openClarifications = await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
            ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn")));
        if (openClarifications)
            return Fail("Resolve open tender and offer clarifications before sending your response.");

        var chosenBid = await db.Bids
            .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .SingleOrDefaultAsync(b => b.BidId == selectedBidId && b.TenderId == tenderId &&
                b.SubmittedAt != null && b.ComplianceStatus == "Compliant" &&
                b.Status != "Draft" && b.Status != "Withdrawn" && b.Status != "Rejected" && b.Status != "NonCompliant");
        if (chosenBid == null)
            return Fail("Choose an eligible compliant offer from this tender.");

        var previousBidId = report.RecommendedBidId;
        var switchedOffer = previousBidId != chosenBid.BidId;
        report.RecommendedBidId = chosenBid.BidId;

        var summaryLine = switchedOffer
            ? $"Selected alternative compliant offer #{chosenBid.BidId} ({chosenBid.Invitation.SupplierProfile.CompanyName}) instead of offer #{previousBidId}. Officer response: {trimmed}"
            : $"Confirmed offer #{chosenBid.BidId} ({chosenBid.Invitation.SupplierProfile.CompanyName}). Officer response: {trimmed}";

        tender.TopEditOfficerNotes = summaryLine;
        tender.TopEditOfficerRespondedAt = DateTime.UtcNow;
        tender.TopEditState = "OfficerAnswered";
        tender.Status = "AwaitingManagerReview";

        report.OfficerSummary = summaryLine;
        report.Status = "OfficerSubmitted";
        report.SubmittedAt = DateTime.UtcNow;

        var managerIds = await db.Users
            .Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.SourcingManager)))
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var mgrId in managerIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = mgrId,
                Title = switchedOffer ? "Sourcing Officer selected an alternative offer" : "Sourcing Officer answered edit request",
                Message = switchedOffer
                    ? $"Tender #{tenderId}: Officer chose compliant offer #{chosenBid.BidId} ({chosenBid.Invitation.SupplierProfile.CompanyName}) and answered the edit request."
                    : $"Tender #{tenderId}: Officer answered the CEO & Chiefs edit request and returned it to you.",
                RelativeUrl = $"/SourcingManager/Submissions?rejectedTenderId={tenderId}&showRejections=true",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["EvaluationSuccess"] = switchedOffer
            ? $"Selected alternative offer #{chosenBid.BidId} ({chosenBid.Invitation.SupplierProfile.CompanyName}) and sent your response back to the Sourcing Manager."
            : "Sent your answer back to the Sourcing Manager to continue the CEO & Chiefs cycle.";
        return RedirectToAction(nameof(Offers), new { tenderId });
    }
    public IActionResult Clarifications(int? id) => RedirectToAction("Index", "Clarification", new { id });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplyClarification(int id, string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 4000 || message.Trim() == SourcingTenderManagement.Services.ClarificationResolution.Confirmation)
        {
            TempData["ClarificationError"] = "Enter a response of 1 to 4000 characters.";
            return RedirectToAction(nameof(Clarifications), new { id });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownTenderIds = db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .Select(t => t.TenderId);
        var clarification = await db.Clarifications.SingleOrDefaultAsync(c =>
            c.ClarificationId == id && c.RecipientUserId == userId && c.Status == "Pending" &&
            ((c.TenderId.HasValue && ownTenderIds.Contains(c.TenderId.Value)) ||
             (c.BidId.HasValue && db.Bids.Any(b => b.BidId == c.BidId.Value && ownTenderIds.Contains(b.TenderId))) ||
             c.PurchaseRequestId.HasValue));
        if (clarification == null) return NotFound();

        db.ClarificationResponses.Add(new ClarificationResponse
        {
            ClarificationId = clarification.ClarificationId,
            RespondedByUserId = userId!,
            Message = message.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        clarification.Status = "Answered";
        await db.SaveChangesAsync();

        TempData["ClarificationSuccess"] = "Your response has been sent.";
        return RedirectToAction(nameof(Clarifications), new { id });
    }
    public async Task<IActionResult> Reports(int? id, bool review = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownTenderIds = db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .Select(t => t.TenderId);
        var reports = await db.EvaluationReports.AsNoTracking()
            .Where(r => ownTenderIds.Contains(r.TenderId))
            .Include(r => r.Tender)
            .Include(r => r.PreparedByUser)
            .Include(r => r.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
            .OrderByDescending(r => r.SubmittedAt ?? r.Tender.CreatedAt)
            .ToListAsync();

        EvaluationReport? selectedReport = null;
        if (id.HasValue)
        {
            selectedReport = reports.SingleOrDefault(r => r.EvaluationReportId == id.Value);
            if (selectedReport == null) return NotFound();
        }
        if (review)
        {
            if (selectedReport == null) return NotFound();
            if (selectedReport.Bid != null)
                selectedReport.Bid.BidItems = await db.BidItems.AsNoTracking().Where(i => i.BidId == selectedReport.RecommendedBidId).ToListAsync();
            selectedReport.Tender.OfferExtensions = await db.Set<TenderOfferExtension>().AsNoTracking().Where(e => e.TenderId == selectedReport.TenderId).ToListAsync();
            var members = await db.CommitteeAssignments.AsNoTracking().Where(a => a.TenderId == selectedReport.TenderId && a.CommitteeType == "Evaluation").OrderBy(a => a.Position).ToListAsync();
            var approvals = await db.TenderApprovals.AsNoTracking().Where(a => a.TenderId == selectedReport.TenderId && a.Stage == "Evaluation" && a.Decision == "Approved").ToListAsync();
            ViewBag.ReviewMembers = members;
            ViewBag.ReviewApprovals = approvals;
            return PartialView("_ReportReviewDialog", selectedReport);
        }
        ViewBag.SelectedReport = selectedReport;
        return View(reports);
    }
}
