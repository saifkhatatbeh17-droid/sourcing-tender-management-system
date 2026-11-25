using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

[Authorize(Policy = "SupplierHistory")]
public class SupplierController(ApplicationDbContext db, UserManager<ApplicationUser> users, IWebHostEnvironment env) : Controller
{
    private string UserId => users.GetUserId(User)!;
    private Task<SupplierProfile?> MyProfile() => db.SupplierProfiles.Include(p => p.User)
        .SingleOrDefaultAsync(p => p.UserId == UserId && (p.Status == "Approved" || p.Status == "Suspended"));

    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    public async Task<IActionResult> Dashboard()
    {
        var profile = await MyProfile();
        if (profile == null) return NotFound();
        var now = DateTime.UtcNow;
        var invitations = db.Invitations.Where(i => i.SupplierProfileId == profile.SupplierProfileId);
        var bids = db.Bids.Where(b => b.Invitation.SupplierProfileId == profile.SupplierProfileId);
        var vm = new SupplierDashboardViewModel
        {
            CompanyName = profile.CompanyName, VerificationStatus = profile.Status, ReviewNotes = profile.ReviewNotes,
            TotalInvitations = await invitations.CountAsync(),
            PendingInvitations = await invitations.CountAsync(i => i.Status == "Pending" && i.Tender.ClosesAt > now && (i.Tender.Status == "Open" || i.Tender.Status == "Published")),
            AcceptedInvitations = await invitations.CountAsync(i => i.Status == "Accepted"),
            SubmittedOffers = await bids.CountAsync(b => b.Status != "Draft"),
            AcceptedOffers = await bids.CountAsync(b => b.Status == "Accepted"),
            RecentInvitations = await invitations.AsNoTracking().Include(i => i.Tender).OrderByDescending(i => i.SentAt).Take(3).ToListAsync(),
            RecentOffers = await bids.AsNoTracking().Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).Include(b => b.BidItems).OrderByDescending(b => b.SubmittedAt ?? b.CreatedAt).Take(3).ToListAsync(),
            Notifications = await db.Notifications.AsNoTracking().Where(n => n.UserId == UserId).OrderByDescending(n => n.CreatedAt).Take(3).ToListAsync()
        };
        ViewBag.DashboardOfferStates = await bids.AsNoTracking().GroupBy(b =>
            b.Status == "Accepted" ? "Awarded" : b.Status == "Withdrawn" ? "Withdrawn" :
            b.Invitation.Tender.Status == "Cancelled" ? "Tender cancelled" :
            b.Status == "Rejected" || b.Status == "NonCompliant" || b.Invitation.Tender.Status == "Awarded" ? "Not selected" :
            b.Status == "Draft" ? "Draft" : b.ComplianceStatus == "Compliant" ? "Passed compliance" : "Awaiting review")
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count);
        return View(vm);
    }

    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> Invitations(string? status)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        IQueryable<Invitation> query = db.Invitations.AsNoTracking().Where(i => i.SupplierProfileId == profile.SupplierProfileId)
            .Include(i => i.Tender).Include(i => i.Bids);
        if (status == "Cancelled") query = query.Where(i => i.Tender.Status == "Cancelled");
        else if (status == "Expired") query = query.Where(i => i.Tender.Status != "Cancelled" &&
            i.Status == "Pending" && i.Tender.ClosesAt <= DateTime.UtcNow);
        else if (status == "Pending") query = query.Where(i => i.Tender.Status != "Cancelled" &&
            i.Status == "Pending" && i.Tender.ClosesAt > DateTime.UtcNow);
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Tender.Status != "Cancelled" && i.Status == status);
        ViewBag.Status = status;
        return View(await query.OrderByDescending(i => i.SentAt).ToListAsync());
    }

    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> Invitation(int id)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        var invitation = await db.Invitations.AsNoTracking().Include(i => i.Tender).ThenInclude(t => t.TenderDocuments)
            .Include(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(i => i.Tender).ThenInclude(t => t.EvaluationCriteria).Include(i => i.Bids)
            .Include(i => i.Tender).ThenInclude(t => t.OfferExtensions)
            .SingleOrDefaultAsync(i => i.InvitationId == id && i.SupplierProfileId == profile.SupplierProfileId);
        return invitation == null ? NotFound() : View(invitation);
    }

    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> DownloadTenderDocument(int id)
    {
        var profile=await MyProfile();if(profile==null)return NotFound();
        var document=await db.TenderDocuments.AsNoTracking().SingleOrDefaultAsync(d=>d.TenderDocumentId==id&&d.Tender.Invitations.Any(i=>i.SupplierProfileId==profile.SupplierProfileId));
        if(document==null)return NotFound();
        var path=Path.Combine(env.ContentRootPath,"App_Data","private-files","tenders",Path.GetFileName(document.StoredFileName));
        return System.IO.File.Exists(path)?PhysicalFile(path,document.ContentType,document.OriginalFileName):NotFound();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> Respond(int id, string response)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        var inv = await db.Invitations.Include(i => i.Tender).SingleOrDefaultAsync(i => i.InvitationId == id && i.SupplierProfileId == profile.SupplierProfileId);
        if (inv == null) return NotFound();
        if (inv.Status != "Pending" || inv.Tender.ClosesAt <= DateTime.UtcNow || inv.Tender.Status is not ("Open" or "Published"))
            TempData["Error"] = "This invitation can no longer be answered.";
        else if (response is not ("Accepted" or "Declined")) return BadRequest();
        else
        {
            inv.Status=response;inv.RespondedAt=DateTime.UtcNow;
            foreach(var officer in (await users.GetUsersInRoleAsync(RoleNames.SourcingOfficer)).Where(u=>u.IsActive))
                db.Notifications.Add(new Notification{UserId=officer.Id,Title=$"Supplier {response.ToLowerInvariant()} tender invitation",Message=$"{profile.CompanyName} {response.ToLowerInvariant()} the invitation for {inv.Tender.Title}.",RelativeUrl=$"/SourcingOfficer/Tenders?id={inv.TenderId}",CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync();TempData["Success"]=$"Invitation {response.ToLowerInvariant()}.";
        }
        return RedirectToAction(nameof(Invitation), new { id });
    }

    public async Task<IActionResult> Offers(string? q, string? status)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        IQueryable<Bid> query = db.Bids.AsNoTracking().AsSplitQuery().Where(b => b.Invitation.SupplierProfileId == profile.SupplierProfileId)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.OfferExtensions)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).Include(b => b.BidItems);
        if (status == "Cancelled") query = query.Where(b => b.Invitation.Tender.Status == "Cancelled");
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(b => b.Invitation.Tender.Status != "Cancelled" && b.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(b => b.Invitation.Tender.Title.Contains(q) || b.BidId.ToString() == q);
        ViewBag.Query = q; ViewBag.Status = status;
        return View(await query.OrderByDescending(b => b.SubmittedAt ?? b.CreatedAt).ToListAsync());
    }

    public async Task<IActionResult> History()
    {
        var profile = await MyProfile();
        if (profile == null) return NotFound();
        var offers = await db.Bids.AsNoTracking()
            .Where(b => b.Invitation.SupplierProfileId == profile.SupplierProfileId && b.SubmittedAt != null)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender)
            .Include(b => b.BidItems)
            .OrderByDescending(b => b.SubmittedAt).ToListAsync();
        return View(offers);
    }

    [HttpGet]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> CreateOffer(int invitationId, int? bidId)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        var inv = await db.Invitations.AsNoTracking().Include(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(i => i.InvitationId == invitationId && i.SupplierProfileId == profile.SupplierProfileId);
        if (inv == null) return NotFound();
        if (profile.Status != "Approved") return Forbid();
        if (inv.Status != "Accepted" || !inv.Tender.IsAcceptingOffers(DateTime.UtcNow)) return Forbid();
        var extended = await db.Set<TenderOfferExtension>().AnyAsync(e => e.TenderId == inv.TenderId);
        Bid? bid = null;
        if (bidId.HasValue)
        {
            bid = await db.Bids.Include(b => b.BidItems).SingleOrDefaultAsync(b => b.BidId == bidId && b.InvitationId == inv.InvitationId &&
                (b.Status == "Draft" || ((b.Status == "Submitted" || b.Status == "UnderReview") && b.ComplianceStatus != "Compliant") || (extended && (b.Status == "NonCompliant" || b.Status == "Withdrawn"))));
            if (bid == null) return NotFound();
        }
        ViewData["IsOfferRevision"] = bid is { Status: not "Draft" };
        var form = new OfferFormViewModel { InvitationId = inv.InvitationId, TenderId = inv.TenderId, BidId = bid?.BidId,
            TenderTitle = inv.Tender.Title, ClosesAt = inv.Tender.ClosesAt, Currency = bid?.Currency ?? "JOD",
            DeliveryDays = bid?.DeliveryDays ?? 0, ApplyTax = bid == null || bid.TaxAmount != 0, TaxPercentage = OfferTax.RatePercent, AdditionalCosts = bid?.AdditionalCosts ?? 0, PaymentTerms = bid?.PaymentTerms, TechnicalDetails = bid?.TechnicalDetails,
            Items = inv.Tender.PurchaseRequest.PurchaseRequestItems.Select(i => new OfferLineViewModel { PurchaseRequestItemId = i.PurchaseRequestItemId,
                ItemName = i.ItemName, Unit = i.Unit, RequestedQuantity = i.Quantity, RequestedSpecifications = i.Specifications, RequestedLegalWarrantyMonths = i.LegalWarrantyMonths,
                Quantity = i.Quantity,
                UnitPrice = bid?.BidItems.FirstOrDefault(x => x.PurchaseRequestItemId == i.PurchaseRequestItemId)?.UnitPrice ?? 0,
                Specifications = bid?.BidItems.FirstOrDefault(x => x.PurchaseRequestItemId == i.PurchaseRequestItemId)?.OfferedSpecifications ?? i.Specifications }).ToList() };
        await HttpContext.RequestServices.GetRequiredService<SourcingTenderManagement.Services.OfferGuarantees>().LoadAsync(form);
        await LoadProductPhotoReferences(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(20_000_000)]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> SaveOffer(OfferFormViewModel model, string submissionCommand, List<IFormFile>? documents)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        var inv = await db.Invitations.Include(i => i.Tender).ThenInclude(t => t.TenderDocuments)
            .Include(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(i => i.InvitationId == model.InvitationId && i.SupplierProfileId == profile.SupplierProfileId);
        if (inv == null) return NotFound();
        if (profile.Status != "Approved" || inv.Status != "Accepted" || !inv.Tender.IsAcceptingOffers(DateTime.UtcNow)) return Forbid();
        var extended = await db.Set<TenderOfferExtension>().AnyAsync(e => e.TenderId == inv.TenderId);
        var editableBid = db.Bids.Where(b => b.BidId == model.BidId && b.InvitationId == inv.InvitationId &&
            (b.Status == "Draft" || ((b.Status == "Submitted" || b.Status == "UnderReview") && b.ComplianceStatus != "Compliant") || (extended && (b.Status == "NonCompliant" || b.Status == "Withdrawn"))));
        var existingStatus = await editableBid.Select(b => b.Status).SingleOrDefaultAsync();
        if (model.BidId.HasValue && existingStatus == null) return NotFound();
        var isRevision = existingStatus is "NonCompliant" or "Withdrawn" or "Submitted" or "UnderReview";
        if (submissionCommand is not ("Draft" or "Submit") || (isRevision && submissionCommand != "Submit"))
            return BadRequest();
        ViewData["IsOfferRevision"] = isRevision;
        // Tender quantities are authoritative, including when a client tampers with posted values.
        for (var index = 0; index < model.Items.Count; index++)
        {
            var requested = inv.Tender.PurchaseRequest.PurchaseRequestItems.FirstOrDefault(x => x.PurchaseRequestItemId == model.Items[index].PurchaseRequestItemId);
            if (requested == null) continue;
            model.Items[index].Quantity = requested.Quantity;
            ModelState.Remove($"Items[{index}].Quantity");
        }
        var validItemIds = inv.Tender.PurchaseRequest.PurchaseRequestItems.Select(x => x.PurchaseRequestItemId).ToHashSet();
        if (model.Items.Count == 0 || model.Items.Any(i => !validItemIds.Contains(i.PurchaseRequestItemId))) ModelState.AddModelError(nameof(model.Items), "Offer items must match the tender request.");
        if (model.Items.Select(i => i.PurchaseRequestItemId).Distinct().Count() != model.Items.Count) ModelState.AddModelError(nameof(model.Items), "Each requested item can appear only once.");
        foreach (var item in model.Items)
        {
            if (item.Quantity <= 0) ModelState.AddModelError(nameof(item.Quantity), "Quantity must be greater than zero.");
            if (item.UnitPrice < 0) ModelState.AddModelError(nameof(item.UnitPrice), "Unit price cannot be negative.");
        }
        if (submissionCommand == "Submit" && model.Items.Any(i => i.UnitPrice <= 0)) ModelState.AddModelError(nameof(model.Items), "Enter a positive unit price for every item before submitting.");
        if (submissionCommand == "Submit" && inv.Tender.TenderDocuments.Count(d => d.IsMandatory) > (documents?.Count(f => f.Length > 0) ?? 0)) ModelState.AddModelError(nameof(documents), "Upload supporting documents for all mandatory tender documents.");
        try
        {
            var amount = model.Items.Aggregate(0m, (sum, line) => checked(sum + checked(line.Quantity * line.UnitPrice)));
            // Suppliers opt in to the fixed rate; amounts are always calculated server-side.
            model.TaxPercentage = model.ApplyTax ? OfferTax.RatePercent : 0;
            model.TaxAmount = model.ApplyTax ? OfferTax.Amount(amount) : 0;
            if (checked(amount + model.TaxAmount + model.AdditionalCosts) > 9999999999999999.99m) ModelState.AddModelError(nameof(model.Items), "The total offer amount is above the supported limit.");
        }
        catch (OverflowException) { ModelState.AddModelError(nameof(model.Items), "The total offer amount is above the supported limit."); }
        if (model.Currency?.Length != 3) ModelState.AddModelError(nameof(model.Currency), "Use a 3-letter currency code.");
        if (model.DeliveryDays < 0) ModelState.AddModelError(nameof(model.DeliveryDays), "Delivery days cannot be negative.");
        if (documents?.Any(f => f.Length > 10_000_000 || !AllowedFile(f.FileName)) == true) ModelState.AddModelError(nameof(documents), "Files must be PDF, DOCX, XLSX, JPG, or PNG and no larger than 10 MB each.");
        var preparedPhotos = new Dictionary<int, byte[]>();
        for (var index = 0; index < model.Items.Count; index++)
        {
            var line = model.Items[index];
            if (line.ProductPhoto == null || line.ProductPhoto.Length == 0) continue;
            try { preparedPhotos[line.PurchaseRequestItemId] = await SourcingTenderManagement.Services.OfferProductPhotos.PrepareAsync(line.ProductPhoto, HttpContext.RequestAborted); }
            catch (System.ComponentModel.DataAnnotations.ValidationException error) { ModelState.AddModelError($"Items[{index}].ProductPhoto", error.Message); }
        }
        await HttpContext.RequestServices.GetRequiredService<SourcingTenderManagement.Services.OfferGuarantees>().ValidateAsync(model, submissionCommand == "Submit", ModelState);
        if (!ModelState.IsValid)
        {
            model.TenderId = inv.TenderId; model.TenderTitle = inv.Tender.Title; model.ClosesAt = inv.Tender.ClosesAt;
            model.Items = inv.Tender.PurchaseRequest.PurchaseRequestItems.Select(item => model.Items.FirstOrDefault(x => x.PurchaseRequestItemId == item.PurchaseRequestItemId) ?? new OfferLineViewModel { PurchaseRequestItemId = item.PurchaseRequestItemId, ItemName = item.ItemName, Unit = item.Unit, RequestedQuantity = item.Quantity, RequestedSpecifications = item.Specifications, RequestedLegalWarrantyMonths = item.LegalWarrantyMonths }).ToList();
            await LoadProductPhotoReferences(model);
            return View("CreateOffer", model);
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var createdFiles = new List<string>();
        var replacedFiles = new List<string>();
        var committed = false;
        try
        {
            // Serialize submission with deadline closure and concurrent draft edits.
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {inv.TenderId};");
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = SupplierProfileId FROM dbo.SupplierProfiles WITH (UPDLOCK,HOLDLOCK) WHERE SupplierProfileId = {profile.SupplierProfileId};");
            if (!await db.SupplierProfiles.AnyAsync(s => s.SupplierProfileId == profile.SupplierProfileId && s.Status == "Approved"))
                return Forbid();
            var deadline = await db.Tenders.Where(t => t.TenderId == inv.TenderId &&
                (t.Status == "Published" || t.Status == "Open") && t.OpensAt <= DateTime.UtcNow && t.ClosesAt > DateTime.UtcNow)
                .Select(t => (DateTime?)t.ClosesAt).SingleOrDefaultAsync();
            if (deadline == null) return Forbid();
            if (model.BidId.HasValue && !await editableBid.AnyAsync(b => b.Status == existingStatus)) return NotFound();
            if (!model.BidId.HasValue && await db.Bids.AnyAsync(b => b.InvitationId == inv.InvitationId))
                return Conflict("An offer already exists for this invitation.");
        var bid = model.BidId.HasValue ? await editableBid.Include(b => b.BidItems).SingleAsync() : new Bid { InvitationId = inv.InvitationId, TenderId = inv.TenderId, CreatedAt = DateTime.UtcNow, Status = "Draft" };
        bid.Currency = (model.Currency ?? "JOD").ToUpperInvariant(); bid.DeliveryDays = model.DeliveryDays; bid.PaymentTerms = model.PaymentTerms?.Trim();
        bid.TechnicalDetails = model.TechnicalDetails?.Trim();
        bid.TaxAmount = model.TaxAmount; bid.AdditionalCosts = model.AdditionalCosts;
        if (model.BidId.HasValue) db.BidItems.RemoveRange(bid.BidItems);
        else db.Bids.Add(bid);
        foreach (var line in model.Items) db.BidItems.Add(new BidItem { Bid = bid, PurchaseRequestItemId = line.PurchaseRequestItemId, Quantity = line.Quantity, UnitPrice = line.UnitPrice, OfferedSpecifications = line.Specifications ?? "" });
        await db.SaveChangesAsync();
        if (documents != null)
        {
            var folder = Path.Combine(env.ContentRootPath, "App_Data", "private-files", "offers"); Directory.CreateDirectory(folder);
            foreach (var file in documents.Where(f => f.Length > 0))
            {
                var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
                var path = Path.Combine(folder, stored);
                createdFiles.Add(path);
                await using var stream = System.IO.File.Create(path); await file.CopyToAsync(stream);
                db.BidDocuments.Add(new BidDocument { BidId = bid.BidId, DocumentType = "Supporting document", OriginalFileName = Path.GetFileName(file.FileName), StoredFileName = stored, ContentType = file.ContentType, FileSizeBytes = file.Length, UploadedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        if (preparedPhotos.Count > 0)
        {
            var folder = Path.Combine(env.ContentRootPath, "App_Data", "private-files", "offers");
            Directory.CreateDirectory(folder);
            foreach (var photo in preparedPhotos)
            {
                var type = SourcingTenderManagement.Services.OfferProductPhotos.Type(photo.Key);
                var previous = await db.BidDocuments.Where(d => d.BidId == bid.BidId && d.DocumentType == type).ToListAsync();
                foreach (var document in previous) replacedFiles.Add(Path.Combine(folder, Path.GetFileName(document.StoredFileName)));
                db.BidDocuments.RemoveRange(previous);
                var stored = Guid.NewGuid().ToString("N") + ".jpg";
                var path = Path.Combine(folder, stored); createdFiles.Add(path);
                await System.IO.File.WriteAllBytesAsync(path, photo.Value, HttpContext.RequestAborted);
                db.BidDocuments.Add(new BidDocument { BidId = bid.BidId, DocumentType = type, OriginalFileName = $"product-{photo.Key}.jpg",
                    StoredFileName = stored, ContentType = "image/jpeg", FileSizeBytes = photo.Value.Length, UploadedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        await HttpContext.RequestServices.GetRequiredService<SourcingTenderManagement.Services.OfferGuarantees>().SaveAsync(model, bid, createdFiles, replacedFiles);
        if (submissionCommand == "Submit")
        {
            var submittedAt = DateTime.UtcNow;
            if (submittedAt >= deadline.Value) return Forbid();
            bid.OfficerOpenedAt = null;
            bid.ComplianceStatus = null;
            bid.ComplianceNotes = null;
            bid.ComplianceReviewedAt = null;
            bid.ComplianceReviewedByUserId = null;
            bid.Status = "Submitted";
            bid.SubmittedAt = submittedAt;
            var officers = (await users.GetUsersInRoleAsync(RoleNames.SourcingOfficer)).Where(u => u.IsActive).Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            officers.Add(inv.Tender.CreatedByUserId);
            foreach (var officerId in officers) db.Notifications.Add(new Notification { UserId = officerId, Title = "Supplier offer received", Message = $"{profile.CompanyName} submitted offer BID-{bid.BidId:D6} for {inv.Tender.Title}.", RelativeUrl = $"/SourcingOfficer/Offers?tenderId={inv.TenderId}", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        committed = true;
        TempData["Success"] = submissionCommand == "Submit" ? "Your offer was submitted successfully." : "Your draft was saved.";
        return RedirectToAction(nameof(Offer), new { id = bid.BidId });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            await transaction.RollbackAsync();
            HttpContext.RequestServices.GetRequiredService<ILogger<SupplierController>>().LogError(ex, "Offer save failed for invitation {InvitationId}", inv.InvitationId);
            ModelState.AddModelError("", "Your offer was not saved. Please attach the files again and retry.");
            model.TenderId = inv.TenderId; model.TenderTitle = inv.Tender.Title; model.ClosesAt = inv.Tender.ClosesAt;
            foreach (var line in model.Items)
            {
                var item = inv.Tender.PurchaseRequest.PurchaseRequestItems.Single(i => i.PurchaseRequestItemId == line.PurchaseRequestItemId);
                line.ItemName = item.ItemName; line.Unit = item.Unit; line.RequestedQuantity = item.Quantity; line.RequestedSpecifications = item.Specifications; line.RequestedLegalWarrantyMonths = item.LegalWarrantyMonths;
            }
            await LoadProductPhotoReferences(model);
            return View("CreateOffer", model);
        }
        finally
        {
            if (committed)
                foreach (var path in replacedFiles)
                    try { System.IO.File.Delete(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { HttpContext.RequestServices.GetRequiredService<ILogger<SupplierController>>().LogWarning(ex, "Could not remove replaced product photo {Path}", path); }
            if (!committed)
                foreach (var path in createdFiles)
                    try { System.IO.File.Delete(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { HttpContext.RequestServices.GetRequiredService<ILogger<SupplierController>>().LogError(ex, "Could not clean up offer attachment {Path}", path); }
        }
    }

    private async Task LoadProductPhotoReferences(OfferFormViewModel model)
    {
        if (!model.BidId.HasValue) return;
        var documents = await db.BidDocuments.AsNoTracking().Where(d => d.BidId == model.BidId && d.DocumentType.StartsWith(SourcingTenderManagement.Services.OfferProductPhotos.Prefix)).ToListAsync();
        foreach (var line in model.Items)
            line.ProductPhotoDocumentId = documents.LastOrDefault(d => d.DocumentType == SourcingTenderManagement.Services.OfferProductPhotos.Type(line.PurchaseRequestItemId))?.BidDocumentId;
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> CancelOffer(int id)
    {
        var tenderId = await db.Bids.AsNoTracking()
            .Where(b => b.BidId == id && b.Invitation.SupplierProfile.UserId == UserId)
            .Select(b => (int?)b.TenderId)
            .SingleOrDefaultAsync();
        if (tenderId == null) return NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId.Value};");
        var bid = await db.Bids.Include(b => b.Invitation).ThenInclude(i => i.Tender)
            .SingleOrDefaultAsync(b => b.BidId == id && b.Invitation.SupplierProfile.UserId == UserId);
        if (bid == null) return NotFound();

        if (!bid.Invitation.Tender.IsAcceptingOffers(DateTime.UtcNow) ||
            bid.SubmittedAt == null || bid.ComplianceStatus == "Compliant" || bid.Status is not ("Submitted" or "UnderReview" or "NonCompliant"))
        {
            TempData["Error"] = "An offer can only be canceled before officer acceptance and while the tender is accepting offers.";
            return RedirectToAction(nameof(Offer), new { id });
        }

        bid.Status = "Withdrawn";
        bid.ComplianceStatus = null;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"] = "Offer canceled.";
        return RedirectToAction(nameof(Offer), new { id });
    }

    public async Task<IActionResult> Offer(int id)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        var bid = await db.Bids.AsNoTracking().AsSplitQuery().Include(b => b.Invitation).ThenInclude(i => i.SupplierProfile)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.EvaluationReport)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.OfferExtensions)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequestItem).Include(b => b.BidDocuments)
            .SingleOrDefaultAsync(b => b.BidId == id && b.Invitation.SupplierProfileId == profile.SupplierProfileId);
        return bid == null ? NotFound() : View(bid);
    }

    public async Task<IActionResult> DownloadDocument(int id)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        var doc = await db.BidDocuments.AsNoTracking().Include(d => d.Bid).ThenInclude(b => b.Invitation)
            .SingleOrDefaultAsync(d => d.BidDocumentId == id && d.Bid.Invitation.SupplierProfileId == profile.SupplierProfileId);
        if (doc == null) return NotFound();
        var path = Path.Combine(env.ContentRootPath, "App_Data", "private-files", "offers", Path.GetFileName(doc.StoredFileName));
        return System.IO.File.Exists(path) ? PhysicalFile(path, doc.ContentType, doc.OriginalFileName) : NotFound();
    }

    public async Task<IActionResult> DownloadSummary(int id)
    {
        var result = await Offer(id);
        if (result is not ViewResult { Model: Bid bid }) return result;
        return SourcingTenderManagement.Services.Reports.ReportPdfDownload.Create(this,
            () => SourcingTenderManagement.Services.Reports.ReportPdfExport.SupplierSummary(bid), $"BID-{id:D6}-summary.pdf");
    }

    public async Task<IActionResult> Profile()
    {
        var profile = await db.SupplierProfiles.AsNoTracking().Include(p => p.User).Include(p => p.SupplierDocuments).SingleOrDefaultAsync(p => p.UserId == UserId && (p.Status == "Approved" || p.Status == "Suspended"));
        return profile == null ? NotFound() : View(profile);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> UpdateCategory(ProcurementCategoryInput input)
    {
        var profile = await db.SupplierProfiles.Include(p => p.User).Include(p => p.SupplierDocuments)
            .SingleOrDefaultAsync(p => p.UserId == UserId && (p.Status == "Approved" || p.Status == "Suspended"));
        if (profile == null) return NotFound();
        if (profile.Status != "Approved") return Forbid();
        if (!ModelState.IsValid)
        {
            ViewBag.CategoryInput = input;
            return View(nameof(Profile), profile);
        }

        profile.Category = input.SelectedCategory;
        await db.SaveChangesAsync();
        TempData["ProfileFeedback"] = "Supplier category updated.";
        return RedirectToAction(nameof(Profile));
    }

    public async Task<IActionResult> DownloadProfileDocument(int id)
    {
        var profileId = await db.SupplierProfiles.Where(p => p.UserId == UserId).Select(p => (int?)p.SupplierProfileId).SingleOrDefaultAsync();
        if (profileId == null) return NotFound();
        var document = await db.SupplierDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.SupplierDocumentId == id && d.SupplierProfileId == profileId.Value);
        if (document == null) return NotFound();
        var path = Path.Combine(env.ContentRootPath, "App_Data", "private-files", "suppliers", Path.GetFileName(document.StoredFileName));
        return System.IO.File.Exists(path) ? PhysicalFile(path, document.ContentType, document.OriginalFileName) : NotFound();
    }

    public IActionResult Clarifications(int? id) => RedirectToAction("Index", "Clarification", new { id });

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> Reply(int id, string message)
    {
        var profile = await MyProfile(); if (profile == null) return NotFound();
        if (string.IsNullOrWhiteSpace(message) || message.Length > 4000 || message.Trim() == SourcingTenderManagement.Services.ClarificationResolution.Confirmation) { TempData["Error"] = "Enter a message of up to 4,000 characters."; return RedirectToAction(nameof(Clarifications)); }
        var clarification = await db.Clarifications.SingleOrDefaultAsync(c => c.ClarificationId == id &&
            (c.SenderUserId == UserId || c.RecipientUserId == UserId) &&
            ((c.BidId != null && c.Bid!.Invitation.SupplierProfileId == profile.SupplierProfileId) ||
             (c.TenderId != null && c.Tender!.Invitations.Any(i => i.SupplierProfileId == profile.SupplierProfileId))));
        if (clarification == null) return NotFound();
        if (clarification.Status == "Resolved") { TempData["Error"] = "This clarification has been resolved."; return RedirectToAction(nameof(Clarifications)); }
        db.ClarificationResponses.Add(new ClarificationResponse { ClarificationId = id, RespondedByUserId = UserId, Message = message.Trim(), CreatedAt = DateTime.UtcNow });
        clarification.Status = "Answered";
        if(clarification.SenderUserId!=UserId)db.Notifications.Add(new Notification{UserId=clarification.SenderUserId,Title="Supplier replied to a clarification",Message=$"{profile.CompanyName} replied to the clarification request.",RelativeUrl="/SourcingOfficer/Clarifications",CreatedAt=DateTime.UtcNow});
        else if(clarification.RecipientUserId!=UserId)db.Notifications.Add(new Notification{UserId=clarification.RecipientUserId,Title="Clarification response received",Message=$"A response was added to your clarification request.",RelativeUrl="/Supplier/Clarifications",CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync(); TempData["Success"] = "Your reply was sent to the sourcing officer.";
        return RedirectToAction(nameof(Clarifications));
    }

    // Dismisses the award celebration popup by marking its winner notification read.
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = "ApprovedSupplier")]
    public async Task<IActionResult> AcknowledgeAward(int id)
    {
        await db.Notifications
            .Where(n => n.NotificationId == id && n.UserId == UserId && !n.IsRead && n.Title == ViewComponents.AwardCelebrationViewComponent.WinTitle)
            .ExecuteUpdateAsync(update => update.SetProperty(n => n.IsRead, true));
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest" ? NoContent() : RedirectToAction(nameof(Dashboard));
    }

    public async Task<IActionResult> Notifications()
    {
        return View(await db.Notifications.AsNoTracking().Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.CreatedAt).ToListAsync());
    }

    private static bool AllowedFile(string name) => new[] { ".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(name).ToLowerInvariant());

}
