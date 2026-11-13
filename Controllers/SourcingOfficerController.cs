using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.SourcingOfficer)]
public class SourcingOfficerController(ApplicationDbContext db) : Controller
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
        ViewBag.DraftTenders = await db.Tenders.AsNoTracking()
            .CountAsync(t => t.CreatedByUserId == userId && t.Status == "Draft");
        ViewBag.PublishedTenders = await db.Tenders.AsNoTracking()
            .CountAsync(t => t.CreatedByUserId == userId && (t.Status == "Published" || t.Status == "Open"));
        ViewBag.InvitationsSent = await db.Invitations.AsNoTracking()
            .CountAsync(i => i.InvitedByUserId == userId);
        ViewBag.SubmittedOffers = await db.Bids.AsNoTracking()
            .CountAsync(b => ownTenderIds.Contains(b.TenderId) && b.SubmittedAt != null);
        ViewBag.EvaluationsInProgress = await db.Evaluations.AsNoTracking()
            .CountAsync(e => ownTenderIds.Contains(e.TenderId) &&
                (e.Status == "Draft" || e.Status == "Submitted"));
        ViewBag.PendingCompliance = await db.Bids.AsNoTracking()
            .CountAsync(b => ownTenderIds.Contains(b.TenderId) && b.SubmittedAt != null &&
                (b.Status == "Submitted" || b.Status == "UnderReview"));
        ViewBag.ReportsAwaitingOfficer = await db.EvaluationReports.AsNoTracking()
            .CountAsync(r => ownTenderIds.Contains(r.TenderId) && r.Status == "CommitteeApproved");
        ViewBag.ReportsAwaitingManager = await db.EvaluationReports.AsNoTracking()
            .CountAsync(r => ownTenderIds.Contains(r.TenderId) && r.Status == "OfficerSubmitted");

        return View();
    }
    public async Task<IActionResult> Requests(int? id, string status = "Submitted")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Forbid();

        var availableStatuses = new[] { "All", "Submitted", "UnderReview", "Approved", "Returned", "Rejected" };
        if (!availableStatuses.Contains(status)) status = "Submitted";

        IQueryable<PurchaseRequest> requestQuery = ReviewableRequests(userId)
            .AsNoTracking()
            .Include(r => r.Department)
            .Include(r => r.ReviewedByUser);
        if (status != "All")
            requestQuery = requestQuery.Where(r => r.Status == status);

        var requests = await requestQuery
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync();

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

        var updatedRows = await db.PurchaseRequests
            .Where(r => r.PurchaseRequestId == id && r.Status == "Submitted" &&
                (r.ReviewedByUserId == null || r.ReviewedByUserId == userId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.Status, "UnderReview")
                .SetProperty(r => r.ReviewedByUserId, userId));
        if (updatedRows != 1)
        {
            var assignedToAnotherOfficer = await db.PurchaseRequests.AsNoTracking()
                .AnyAsync(r => r.PurchaseRequestId == id && r.Status != "Draft" &&
                    r.ReviewedByUserId != null && r.ReviewedByUserId != userId);
            if (assignedToAnotherOfficer) return NotFound();
            TempData["RequestError"] = "This request is no longer available to start review.";
            return RedirectToAction(nameof(Requests), new { id, status = "Submitted" });
        }

        TempData["RequestSuccess"] = "The request is now assigned to you for review.";
        return RedirectToAction(nameof(Requests), new { id, status = "UnderReview" });
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
            return RedirectToAction(nameof(Requests), new { id, status = "UnderReview" });
        }

        var request = await db.PurchaseRequests.AsNoTracking()
            .Where(r => r.PurchaseRequestId == id)
            .Select(r => new { r.Status, r.ReviewedByUserId })
            .SingleOrDefaultAsync();
        if (request == null || request.Status == "Draft") return NotFound();
        if (request.ReviewedByUserId != userId) return Forbid();
        if (request.Status != "UnderReview")
        {
            TempData["RequestError"] = "Only the assigned officer can decide a request that is UnderReview.";
            return RedirectToAction(nameof(Requests), new { id, status = request.Status });
        }

        var reviewedAt = DateTime.UtcNow;
        var updatedRows = await db.PurchaseRequests
            .Where(r => r.PurchaseRequestId == id && r.Status == "UnderReview" && r.ReviewedByUserId == userId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.Status, decision)
                .SetProperty(r => r.ReviewNotes, r => cleanNotes ?? r.ReviewNotes)
                .SetProperty(r => r.ReviewedAt, reviewedAt));
        if (updatedRows != 1)
        {
            TempData["RequestError"] = "This request changed before your decision was saved. Review its current status.";
            return RedirectToAction(nameof(Requests), new { id, status = "UnderReview" });
        }

        TempData["RequestSuccess"] = $"Purchase Request {decision.ToLowerInvariant()} successfully.";
        return RedirectToAction(nameof(Requests), new { id, status = decision });
    }

    private IQueryable<PurchaseRequest> ReviewableRequests(string userId) =>
        db.PurchaseRequests.Where(r => r.Status != "Draft" &&
            (r.ReviewedByUserId == userId || (r.ReviewedByUserId == null && r.Status == "Submitted")));
    public async Task<IActionResult> Tenders(int? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownedTenders = db.Tenders.AsNoTracking().Where(t => t.CreatedByUserId == userId);
        var model = new TendersPageViewModel
        {
            Tenders = await ownedTenders.OrderByDescending(t => t.CreatedAt).ToListAsync()
        };
        if (id.HasValue)
        {
            model.SelectedTender = await ownedTenders
                .Include(t => t.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
                .Include(t => t.Bid).ThenInclude(b => b!.BidItems)
                .SingleOrDefaultAsync(t => t.TenderId == id);
            if (model.SelectedTender == null) return NotFound();
        }

        ViewBag.CanSendToEvaluation = model.SelectedTender is { Status: "Closed" } selectedTender &&
            await HasCompletedComplianceForEvaluation(selectedTender.TenderId);

        ViewBag.ApprovedPurchaseRequests = await db.PurchaseRequests
            .AsNoTracking()
            .Where(r => r.Status == "Approved" && !db.Tenders.Any(t => t.PurchaseRequestId == r.PurchaseRequestId))
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync();
        ViewBag.CreateTender = new SourcingOfficerTenderForm();
        return View("Tenders", model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? purchaseRequestId)
    {
        var result = await Tenders(null);
        ViewBag.CreateTender = new SourcingOfficerTenderForm { PurchaseRequestId = purchaseRequestId ?? 0 };
        ViewBag.OpenCreateModal = true;
        return result;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("PurchaseRequestId,Title,Description,Terms,OpensAt,ClosesAt")] SourcingOfficerTenderForm tender)
    {
        if (tender.PurchaseRequestId <= 0)
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "Select an approved purchase request.");
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
            ModelState.AddModelError(nameof(Tender.ClosesAt), "Enter the closing date and time.");
        else if (tender.ClosesAt <= tender.OpensAt)
            ModelState.AddModelError(nameof(Tender.ClosesAt), "The closing date and time must be later than the opening date and time.");

        if (!ModelState.IsValid) return await ShowCreateTender(tender);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = await db.PurchaseRequests
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == tender.PurchaseRequestId);
        if (request == null)
        {
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "The selected purchase request was not found.");
            return await ShowCreateTender(tender);
        }
        if (request.Status != "Approved")
        {
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "Only approved purchase requests can be converted to a tender.");
            return await ShowCreateTender(tender);
        }
        if (await db.Tenders.AnyAsync(t => t.PurchaseRequestId == tender.PurchaseRequestId))
        {
            ModelState.AddModelError(nameof(Tender.PurchaseRequestId), "A tender already exists for this purchase request.");
            return await ShowCreateTender(tender);
        }

        request.Status = "ConvertedToTender";
        db.Tenders.Add(new Tender
        {
            PurchaseRequestId = request.PurchaseRequestId,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            Title = tender.Title.Trim(),
            Description = tender.Description.Trim(),
            Terms = string.IsNullOrWhiteSpace(tender.Terms) ? null : tender.Terms.Trim(),
            OpensAt = tender.OpensAt,
            ClosesAt = tender.ClosesAt,
            Status = "Draft"
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return RedirectToAction(nameof(Tenders));
    }

    private async Task<IActionResult> ShowCreateTender(SourcingOfficerTenderForm tender)
    {
        var result = await Tenders(null);
        ViewBag.CreateTender = tender;
        ViewBag.OpenCreateModal = true;
        return result;
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (tender.Status != "Draft") return Forbid();

        var result = await Tenders(id);
        ViewBag.EditTender = new SourcingOfficerTenderForm
        {
            TenderId = tender.TenderId,
            Title = tender.Title,
            Description = tender.Description,
            Terms = tender.Terms,
            OpensAt = tender.OpensAt,
            ClosesAt = tender.ClosesAt
        };
        ViewBag.OpenEditModal = true;
        return result;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind("TenderId,Title,Description,Terms,OpensAt,ClosesAt")] SourcingOfficerTenderForm input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            ModelState.AddModelError(nameof(Tender.Title), "Enter a tender title.");
        else if (input.Title.Trim().Length > 200)
            ModelState.AddModelError(nameof(Tender.Title), "The title cannot exceed 200 characters.");
        if (string.IsNullOrWhiteSpace(input.Description))
            ModelState.AddModelError(nameof(Tender.Description), "Enter a tender description.");
        else if (input.Description.Trim().Length > 4000)
            ModelState.AddModelError(nameof(Tender.Description), "The description cannot exceed 4000 characters.");
        if (input.Terms?.Length > 4000)
            ModelState.AddModelError(nameof(Tender.Terms), "Terms cannot exceed 4000 characters.");
        if (input.OpensAt == default)
            ModelState.AddModelError(nameof(Tender.OpensAt), "Enter the opening date and time.");
        if (input.ClosesAt == default)
            ModelState.AddModelError(nameof(Tender.ClosesAt), "Enter the closing date and time.");
        else if (input.ClosesAt <= input.OpensAt)
            ModelState.AddModelError(nameof(Tender.ClosesAt), "The closing date and time must be later than the opening date and time.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders
            .SingleOrDefaultAsync(t => t.TenderId == input.TenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (tender.Status != "Draft") return Forbid();

        if (!ModelState.IsValid) return await ShowEditTender(input);

        tender.Title = input.Title.Trim();
        tender.Description = input.Description.Trim();
        tender.Terms = string.IsNullOrWhiteSpace(input.Terms) ? null : input.Terms.Trim();
        tender.OpensAt = input.OpensAt;
        tender.ClosesAt = input.ClosesAt;
        await db.SaveChangesAsync();

        TempData["TenderSuccess"] = "Tender updated successfully.";
        return RedirectToAction(nameof(Tenders), new { id = tender.TenderId });
    }

    private async Task<IActionResult> ShowEditTender(SourcingOfficerTenderForm input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenderId == input.TenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (tender.Status != "Draft") return Forbid();

        var result = await Tenders(input.TenderId);
        ViewBag.EditTender = input;
        ViewBag.OpenEditModal = true;
        return result;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishTender(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders
            .SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (tender.Status != "Draft")
        {
            TempData["TenderError"] = "Only your Draft tenders can be published.";
            return RedirectToAction(nameof(Tenders), new { id });
        }

        if (tender.OpensAt == default || tender.ClosesAt == default || tender.ClosesAt <= tender.OpensAt)
        {
            TempData["TenderError"] = "The tender must have valid opening and closing dates, and the closing date must be later.";
            return RedirectToAction(nameof(Tenders), new { id });
        }

        tender.Status = "Published";
        await db.SaveChangesAsync();
        TempData["TenderSuccess"] = "Tender published successfully.";
        return RedirectToAction(nameof(Tenders), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToEvaluation(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();

        if (tender.Status != "Closed")
        {
            TempData["TenderError"] = "Only your Closed tenders can be sent to Evaluation.";
            return RedirectToAction(nameof(Tenders), new { id });
        }

        var submittedBids = db.Bids.AsNoTracking()
            .Where(b => b.TenderId == id && b.SubmittedAt != null);
        if (!await submittedBids.AnyAsync())
        {
            TempData["TenderError"] = "At least one submitted bid is required before sending this Tender to Evaluation.";
            return RedirectToAction(nameof(Tenders), new { id });
        }
        if (await submittedBids.AnyAsync(b => b.ComplianceStatus != "Compliant" && b.ComplianceStatus != "NonCompliant"))
        {
            TempData["TenderError"] = "Complete the compliance review for every submitted bid before sending this Tender to Evaluation.";
            return RedirectToAction(nameof(Tenders), new { id });
        }
        if (!await submittedBids.AnyAsync(b => b.ComplianceStatus == "Compliant"))
        {
            TempData["TenderError"] = "At least one submitted bid must be Compliant before this Tender can enter Evaluation.";
            return RedirectToAction(nameof(Tenders), new { id });
        }

        var updatedRows = await db.Tenders
            .Where(t => t.TenderId == id && t.CreatedByUserId == userId && t.Status == "Closed")
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.Status, "UnderEvaluation"));
        if (updatedRows != 1)
        {
            TempData["TenderError"] = "This Tender changed before the handoff. Refresh the page and review its current status.";
            return RedirectToAction(nameof(Tenders), new { id });
        }

        TempData["TenderSuccess"] = "Tender sent to the Evaluation Committee successfully.";
        return RedirectToAction(nameof(Tenders), new { id });
    }

    private async Task<bool> HasCompletedComplianceForEvaluation(int tenderId)
    {
        var submittedBids = db.Bids.AsNoTracking()
            .Where(b => b.TenderId == tenderId && b.SubmittedAt != null);
        var hasCompliantBid = await submittedBids.AnyAsync(b => b.ComplianceStatus == "Compliant");
        var hasUnresolvedDecision = await submittedBids.AnyAsync(b =>
            b.ComplianceStatus != "Compliant" && b.ComplianceStatus != "NonCompliant");
        return hasCompliantBid && !hasUnresolvedDecision;
    }

    public async Task<IActionResult> Invitations()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var invitations = await db.Invitations
            .AsNoTracking()
            .Where(i => i.InvitedByUserId == userId)
            .Include(i => i.Tender)
            .Include(i => i.SupplierProfile)
            .OrderByDescending(i => i.SentAt)
            .ToListAsync();

        ViewBag.EligibleTenders = await db.Tenders
            .AsNoTracking()
            .Where(t => t.CreatedByUserId == userId && t.Status == "Published")
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        ViewBag.ApprovedSuppliers = await db.SupplierProfiles
            .AsNoTracking()
            .Where(s => s.Status == "Approved")
            .OrderBy(s => s.CompanyName)
            .ToListAsync();

        return View(invitations);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInvitation(int tenderId, int supplierProfileId)
    {
        if (tenderId <= 0 || supplierProfileId <= 0)
        {
            TempData["InvitationError"] = "Select a tender and an approved supplier.";
            return RedirectToAction(nameof(Invitations));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.SingleOrDefaultAsync(t =>
            t.TenderId == tenderId && t.CreatedByUserId == userId);

        if (tender == null) return NotFound();
        if (tender.Status != "Published")
        {
            TempData["InvitationError"] = "Invitations can only be created for your Published tenders.";
            return RedirectToAction(nameof(Invitations));
        }

        var supplier = await db.SupplierProfiles.SingleOrDefaultAsync(s =>
            s.SupplierProfileId == supplierProfileId && s.Status == "Approved");
        if (supplier == null)
        {
            TempData["InvitationError"] = "The selected supplier was not found or is not approved.";
            return RedirectToAction(nameof(Invitations));
        }

        if (await db.Invitations.AnyAsync(i =>
                i.TenderId == tenderId && i.SupplierProfileId == supplierProfileId))
        {
            TempData["InvitationError"] = "This supplier has already been invited to this tender.";
            return RedirectToAction(nameof(Invitations));
        }

        var invitation = new Invitation
        {
            TenderId = tenderId,
            SupplierProfileId = supplierProfileId,
            InvitedByUserId = userId!,
            Status = "Pending",
            SentAt = DateTime.UtcNow
        };

        db.Invitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(invitation).State = EntityState.Detached;
            var duplicateExists = await db.Invitations.AnyAsync(i =>
                i.TenderId == tenderId && i.SupplierProfileId == supplierProfileId);
            TempData["InvitationError"] = duplicateExists
                ? "This supplier has already been invited to this tender."
                : "The invitation could not be saved. Please try again.";
            return RedirectToAction(nameof(Invitations));
        }

        TempData["InvitationSuccess"] = "Supplier invitation created successfully.";
        return RedirectToAction(nameof(Invitations));
    }
    public async Task<IActionResult> Offers(int? id, int? tenderId, int? supplierProfileId, string status = "All")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownedTenderIds = db.Tenders
            .AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .Select(t => t.TenderId);
        var allowedStatuses = new[] { "All", "Submitted", "UnderReview", "Compliant", "NonCompliant", "Accepted", "Rejected", "Withdrawn" };
        if (!allowedStatuses.Contains(status)) status = "All";

        IQueryable<Bid> bidQuery = db.Bids
            .AsNoTracking()
            .Where(b => b.SubmittedAt != null && ownedTenderIds.Contains(b.TenderId))
            .Include(b => b.Invitation).ThenInclude(i => i.Tender)
            .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
            .Include(b => b.BidDocuments);
        if (tenderId.HasValue)
            bidQuery = bidQuery.Where(b => b.TenderId == tenderId.Value);
        if (supplierProfileId.HasValue)
            bidQuery = bidQuery.Where(b => b.Invitation.SupplierProfileId == supplierProfileId.Value);
        if (status != "All")
            bidQuery = bidQuery.Where(b => b.Status == status);

        var bids = await bidQuery
            .OrderByDescending(b => b.SubmittedAt)
            .ToListAsync();

        Bid? selectedBid = null;
        if (id.HasValue)
        {
            selectedBid = await db.Bids
                .AsNoTracking()
                .Where(b => b.BidId == id.Value && b.SubmittedAt != null && ownedTenderIds.Contains(b.TenderId))
                .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
                .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
                .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
                .Include(b => b.BidDocuments)
                .SingleOrDefaultAsync();
            if (selectedBid == null) return NotFound();
        }

        ViewBag.SelectedBid = selectedBid;
        ViewBag.TenderFilter = tenderId;
        ViewBag.SupplierFilter = supplierProfileId;
        ViewBag.StatusFilter = status;
        ViewBag.OfferTenders = await db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId && t.Invitations.Any(i => i.Bids.Any(b => b.SubmittedAt != null)))
            .OrderBy(t => t.Title)
            .ToListAsync();
        ViewBag.OfferSuppliers = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Invitations.Any(i => ownedTenderIds.Contains(i.TenderId) && i.Bids.Any(b => b.SubmittedAt != null)))
            .OrderBy(s => s.CompanyName)
            .ToListAsync();
        return View(bids);
    }
    public async Task<IActionResult> Compliance(int? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownedTenderIds = db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .Select(t => t.TenderId);
        var bids = await db.Bids.AsNoTracking()
            .Where(b => b.SubmittedAt != null && ownedTenderIds.Contains(b.TenderId))
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem)
            .Include(b => b.BidDocuments)
            .Include(b => b.Clarifications).ThenInclude(c => c.ClarificationResponses)
            .OrderBy(b => b.ComplianceStatus == null ? 0 : b.ComplianceStatus == "PendingClarification" ? 1 : 2)
            .ThenByDescending(b => b.SubmittedAt)
            .ToListAsync();

        Bid? selectedBid = null;
        if (id.HasValue)
        {
            selectedBid = bids.SingleOrDefault(b => b.BidId == id.Value);
            if (selectedBid == null) return NotFound();
        }

        ViewBag.SelectedBid = selectedBid;
        return View(bids);
    }

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
        var bid = await db.Bids
            .Include(b => b.Clarifications).ThenInclude(c => c.ClarificationResponses)
            .SingleOrDefaultAsync(b => b.BidId == id && b.SubmittedAt != null &&
                db.Tenders.Any(t => t.TenderId == b.TenderId && t.CreatedByUserId == userId));
        if (bid == null) return NotFound();

        if (bid.Status != "Submitted" && bid.Status != "UnderReview")
        {
            TempData["ComplianceError"] = "This offer is not waiting for compliance review.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (bid.ComplianceStatus == "Compliant" || bid.ComplianceStatus == "NonCompliant")
        {
            TempData["ComplianceError"] = "A final compliance decision has already been recorded.";
            return RedirectToAction(nameof(Compliance), new { id });
        }
        if (bid.ComplianceStatus == "PendingClarification" &&
            !bid.Clarifications.Any(c => (c.Status == "Answered" || c.Status == "Resolved") && c.ClarificationResponses.Count > 0))
        {
            TempData["ComplianceError"] = "Wait for a supplier response before completing this compliance review.";
            return RedirectToAction(nameof(Compliance), new { id });
        }

        bid.ComplianceStatus = decision;
        bid.Status = decision;
        bid.ComplianceNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        bid.ComplianceReviewedByUserId = userId;
        bid.ComplianceReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitOfficerReport(int tenderId, int? recommendedBidId, string? officerSummary)
    {
        if (string.IsNullOrWhiteSpace(officerSummary) || officerSummary.Trim().Length > 4000)
        {
            TempData["EvaluationError"] = "Enter a Sourcing Officer summary of 1 to 4000 characters.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.AsNoTracking().SingleOrDefaultAsync(t =>
            t.TenderId == tenderId && t.CreatedByUserId == userId);
        if (tender == null) return NotFound();
        if (tender.Status != "AwaitingOfficerSummary")
        {
            TempData["EvaluationError"] = "The Tender must be AwaitingOfficerSummary before you can submit it to the Sourcing Manager.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        var evaluationAssignmentIds = await db.CommitteeAssignments.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.CommitteeType == "Evaluation")
            .Select(a => a.CommitteeAssignmentId)
            .ToListAsync();
        if (evaluationAssignmentIds.Count != 4)
        {
            TempData["EvaluationError"] = "Four Evaluation Committee assignments are required before Officer submission.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        var approvedAssignmentCount = await db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Stage == "Evaluation" && a.Decision == "Approved" &&
                a.CommitteeAssignmentId.HasValue && evaluationAssignmentIds.Contains(a.CommitteeAssignmentId.Value))
            .Select(a => a.CommitteeAssignmentId)
            .Distinct()
            .CountAsync();
        if (approvedAssignmentCount != 4)
        {
            TempData["EvaluationError"] = "All four Evaluation Committee members must approve before Officer submission.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        var report = await db.EvaluationReports.SingleOrDefaultAsync(r => r.TenderId == tenderId);
        if (report == null || report.Status != "CommitteeApproved" || string.IsNullOrWhiteSpace(report.CommitteeSummary))
        {
            TempData["EvaluationError"] = "A completed CommitteeApproved Evaluation Report is required before Officer submission.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        if (!recommendedBidId.HasValue)
        {
            TempData["EvaluationError"] = "Select a Compliant bid as the recommendation before submitting.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        var recommendedBidExists = await db.Bids.AnyAsync(b =>
            b.BidId == recommendedBidId.Value && b.TenderId == tenderId &&
            b.Status == "Compliant" && b.ComplianceStatus == "Compliant");
        if (!recommendedBidExists)
        {
            TempData["EvaluationError"] = "The recommended offer must be a Compliant bid belonging to this Tender.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var updatedRows = await db.Tenders
            .Where(t => t.TenderId == tenderId && t.CreatedByUserId == userId && t.Status == "AwaitingOfficerSummary")
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.Status, "AwaitingManagerReview"));
        if (updatedRows != 1)
        {
            await transaction.RollbackAsync();
            TempData["EvaluationError"] = "This Tender changed before submission. Refresh the page and review its current status.";
            return RedirectToAction(nameof(Evaluations), new { tenderId });
        }

        report.OfficerSummary = officerSummary.Trim();
        report.RecommendedBidId = recommendedBidId.Value;
        report.PreparedByUserId = userId;
        report.Status = "OfficerSubmitted";
        report.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["EvaluationSuccess"] = "The Evaluation Report, Officer Summary, and recommendation were submitted to the Sourcing Manager.";
        return RedirectToAction(nameof(Evaluations), new { tenderId });
    }
    public async Task<IActionResult> Clarifications(int? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownTenderIds = db.Tenders.AsNoTracking()
            .Where(t => t.CreatedByUserId == userId)
            .Select(t => t.TenderId);
        var ownRequestIds = db.PurchaseRequests.AsNoTracking().Select(r => r.PurchaseRequestId);
        var clarifications = await db.Clarifications.AsNoTracking()
            .Where(c => (c.SenderUserId == userId || c.RecipientUserId == userId) &&
                ((c.TenderId.HasValue && ownTenderIds.Contains(c.TenderId.Value)) ||
                 (c.BidId.HasValue && db.Bids.Any(b => b.BidId == c.BidId.Value && ownTenderIds.Contains(b.TenderId))) ||
                 (c.PurchaseRequestId.HasValue && ownRequestIds.Contains(c.PurchaseRequestId.Value))))
            .Include(c => c.SenderUser).ThenInclude(u => u.SupplierProfileUser)
            .Include(c => c.RecipientUser).ThenInclude(u => u.SupplierProfileUser)
            .Include(c => c.Tender)
            .Include(c => c.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(c => c.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.Tender)
            .Include(c => c.PurchaseRequest).ThenInclude(r => r!.Department)
            .Include(c => c.ClarificationResponses).ThenInclude(r => r.RespondedByUser)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        Clarification? selectedClarification = null;
        if (id.HasValue)
        {
            selectedClarification = clarifications.SingleOrDefault(c => c.ClarificationId == id.Value);
            if (selectedClarification == null) return NotFound();
        }
        ViewBag.SelectedClarification = selectedClarification;
        ViewBag.CurrentUserId = userId;
        return View(clarifications);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplyClarification(int id, string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 4000)
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
    public async Task<IActionResult> Reports(int? id)
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
        ViewBag.SelectedReport = selectedReport;
        return View(reports);
    }
}
