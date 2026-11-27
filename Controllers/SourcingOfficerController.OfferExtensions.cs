using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// Sourcing Officer actions on a tender's offer deadline: close early, extend the round, or (in demos) simulate the deadline.
public partial class SourcingOfficerController
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseSubmissionsEarly(int tenderId, bool sendToCommittee = false)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.SingleOrDefaultAsync(t => t.TenderId == tenderId
            && t.CreatedByUserId == officerId);
        if (tender == null) return NotFound();

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId)
            || await db.Evaluations
            .AnyAsync(e => e.TenderId == tenderId))
        { TempData["TenderError"] = "This tender has already been sent to the committee."; return RedirectToAction(nameof(Offers), new { tenderId }); }
        var now = DateTime.UtcNow;
        if (!tender.IsAcceptingOffers(now) ||
            await db.Set<TenderOfferExtension>().AnyAsync(e => e.TenderId == tenderId) ||
            await db.TenderApprovals.AnyAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected"))
        {
            TempData["TenderError"] = "Only an active first round can close early. Extensions run through their stated deadline.";
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        var supplierCount = await db.Bids
            .Where(b => b.TenderId == tenderId && b.SubmittedAt != null && b.Status != "Withdrawn")
            .Select(b => b.Invitation.SupplierProfileId).Distinct().CountAsync();
        if (supplierCount < TenderOfferExtension.RequiredCompliantOffers)
        {
            TempData["TenderError"] = "At least three suppliers must have active submitted offers to close early.";
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        if (sendToCommittee)
        {
            var offers = await db.Bids.Include(b => b.Invitation)
                .Where(b => b.TenderId == tenderId && b.SubmittedAt != null && b.Status != "Withdrawn").ToListAsync();
            string? error = offers.Any(b => b.ComplianceStatus is not ("Compliant" or "NonCompliant"))
                ? "Review every offer for compliance before sending to the committee."
                : offers.Where(b => b.ComplianceStatus == "Compliant")
                    .Select(b => b.Invitation.SupplierProfileId)
                    .Distinct()
                    .Count() < TenderOfferExtension.RequiredCompliantOffers
                    ? "At least three compliant suppliers are required to close submissions and send to the committee." : null;
            if (error == null && await db.Clarifications.AnyAsync(c => c.Status != "Resolved" &&
                ((c.BidId == null && c.TenderId == tenderId) || (c.Bid != null && c.Bid.TenderId == tenderId && c.Bid.Status != "Withdrawn"))))
                error = "Resolve open questions before sending to the committee.";
            if (error == null)
                error = await SourcingTenderManagement.Services.Evaluation.CommitteeSetup.Prepare(db, tender);
            if (error != null)
            {
                TempData["TenderError"] = error;
                return RedirectToAction(nameof(Offers), new { tenderId });
            }
        }

        var previousDeadline = tender.ClosesAt;
        tender.ClosesAt = now;
        tender.Status = sendToCommittee ? "UnderEvaluation" : "Closed";
        var recipients = await db.Invitations.Where(i => i.TenderId == tenderId)
            .Select(i => i.SupplierProfile.UserId).Distinct().ToListAsync();
        db.Notifications.AddRange(recipients.Select(userId => new Notification
        {
            UserId = userId,
            Title = "Tender submissions closed early",
            Message = $"The officer closed submissions for {tender.Title} at {now:dd MMM yyyy HH:mm} UTC. The original deadline was {previousDeadline:dd MMM yyyy HH:mm} UTC. Offers can no longer be submitted or withdrawn.",
            RelativeUrl = $"/Supplier/Invitations#tender-{tenderId}",
            CreatedAt = now
        }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["TenderSuccess"] = sendToCommittee
            ? "Submissions closed and compliant offers sent to the committee."
            : "Submissions closed and invited suppliers were notified. You can now review offers.";
        return RedirectToAction(nameof(Offers), new { tenderId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateDeadline(int tenderId, int currentRound, [FromServices] IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return NotFound();

        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.Include(t => t.OfferExtensions)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == officerId);
        if (tender == null) return NotFound();

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId)
            || await db.Evaluations
            .AnyAsync(e => e.TenderId == tenderId))
        { TempData["TenderError"] = "This tender has already been sent to the committee."; return RedirectToAction(nameof(Offers), new { tenderId }); }
        var now = DateTime.UtcNow;
        if (!tender.IsAcceptingOffers(now) || tender.OfferExtensions.Count + 1 != currentRound ||
            await db.TenderApprovals.AnyAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected"))
        {
            TempData["TenderError"] = "The selected offer round is no longer accepting submissions.";
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        tender.ClosesAt = now;
        tender.Status = "Closed";
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["TenderSuccess"] = $"Round {currentRound} ended for testing. You can now review offers.";
        return RedirectToAction(nameof(Offers), new { tenderId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendOfferRound(int tenderId, int currentRound, DateTime? newDeadline)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Lock this row until the transaction ends, so two people cannot change the same record at the same time.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.Include(t => t.OfferExtensions)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == officerId);
        if (tender == null) return NotFound();

        IActionResult ReturnError(string message)
        {
            TempData["TenderError"] = message;
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId)
            || await db.Evaluations
            .AnyAsync(e => e.TenderId == tenderId))
        { TempData["TenderError"] = "This tender has already been sent to the committee."; return RedirectToAction(nameof(Offers), new { tenderId }); }
        var now = DateTime.UtcNow;
        var round = tender.OfferExtensions.Count + 1;
        if (tender.Status is not ("Published" or "Open" or "Closed") || tender.ClosesAt > now ||
            await db.TenderApprovals.AnyAsync(a => a.TenderId == tenderId && a.Stage == "Top" && a.Decision == "Rejected"))
            return ReturnError("Only a tender that has reached its deadline and is awaiting offer review can be extended.");
        if (round != currentRound || round >= TenderOfferExtension.MaximumRound)
            return ReturnError("The tender round has changed or the final round has been reached.");
        if (!newDeadline.HasValue || newDeadline.Value < now.AddHours(TenderOfferExtension.MinimumResponseHours))
            return ReturnError($"Allow suppliers at least {TenderOfferExtension.MinimumResponseHours} hours to respond.");

        var bids = db.Bids.Where(b => b.TenderId == tenderId && b.SubmittedAt != null && b.Status != "Withdrawn");
        if (await bids.AnyAsync(b => b.ComplianceStatus == null ||
            (b.ComplianceStatus != "Compliant" && b.ComplianceStatus != "NonCompliant")))
            return ReturnError("Save a compliance decision for every submitted offer before extending.");
        var compliantSupplierIds = await bids.Where(b => b.ComplianceStatus == "Compliant")
            .Select(b => b.Invitation.SupplierProfileId).Distinct().ToListAsync();
        if (compliantSupplierIds.Count >= TenderOfferExtension.RequiredCompliantOffers)
            return ReturnError("This tender already has enough compliant offers for committee review.");

        var suppliers = await SuppliersForCategory(tender.Category)
            .Where(s => s.User.IsActive && !compliantSupplierIds.Contains(s.SupplierProfileId))
            .Select(s => new { s.SupplierProfileId, s.UserId, s.CompanyName }).ToListAsync();

        var invitations = await db.Invitations.Where(i => i.TenderId == tenderId)
            .ToDictionaryAsync(i => i.SupplierProfileId);
        var outreach = new List<string>();
        foreach (var supplier in suppliers.OrderBy(s => s.CompanyName))
        {
            var isNew = !invitations.TryGetValue(supplier.SupplierProfileId, out var invitation);
            if (invitation == null)
            {
                invitation = new Invitation
                {
                    TenderId = tenderId,
                    SupplierProfileId = supplier.SupplierProfileId,
                    InvitedByUserId = officerId!,
                    Status = "Pending",
                    SentAt = now
                };
                db.Invitations.Add(invitation);
            }
            else if (invitation.Status == "Declined")
            {
                invitation.Status = "Pending";
                invitation.RespondedAt = null;
            }

            outreach.Add($"{supplier.CompanyName} (supplier #{supplier.SupplierProfileId}) — {(isNew ? "Invited" : "Reminded")}");
            db.Notifications.Add(new Notification
            {
                UserId = supplier.UserId,
                Title = isNew ? "Tender invitation" : "Tender reopened for offers",
                Message = $"{tender.Title} is accepting offers through {newDeadline.Value:dd MMM yyyy HH:mm} UTC.",
                RelativeUrl = $"/Supplier/Invitations#tender-{tenderId}",
                CreatedAt = now
            });
        }

        tender.OfferExtensions.Add(new TenderOfferExtension
        {
            Round = round + 1,
            PreviousDeadline = tender.ClosesAt,
            OpenedAt = now,
            Deadline = newDeadline.Value,
            PreviousCompliantOffers = compliantSupplierIds.Count,
            Outreach = outreach.Count == 0 ? "No approved suppliers in this category were available to invite or remind." : string.Join("\n", outreach)
        });
        tender.ClosesAt = newDeadline.Value;
        tender.Status = "Open";
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["TenderSuccess"] = outreach.Count == 0
            ? $"Round {round + 1} opened. No approved suppliers were available to contact."
            : $"Round {round + 1} opened and {outreach.Count} suppliers were contacted.";
        return RedirectToAction(nameof(Offers), new { tenderId });
    }
}
