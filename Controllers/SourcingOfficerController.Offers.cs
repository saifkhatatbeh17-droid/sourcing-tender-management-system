using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Services.Reports;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Compliance;

namespace SourcingTenderManagement.Controllers;

public sealed record OfficerSpecMatchPage(SpecMatchInput Input, SpecMatchResult? Result, bool Outdated, DateTime? CreatedAt,
    IReadOnlyDictionary<int, BidGuarantee>? LegalWarranties = null);

public partial class SourcingOfficerController
{
    // Serialize concurrent runs for the same tender in this application instance.
    private static readonly SemaphoreSlim[] SpecMatchLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunSpecMatch(int id, [FromServices] SpecMatchService service, CancellationToken ct)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId) return Forbid();

        var tender = await db.Tenders.AsNoTracking()
            .Include(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(t => t.TenderId == id && t.CreatedByUserId == userId, ct);
        if (tender is null) return NotFound();

        var gate = SpecMatchLocks[id % SpecMatchLocks.Length];
        await gate.WaitAsync(ct);
        try
        {
            // Spec match is a reading aid for the compliance decision, so offers still waiting for that decision are included.
            var bids = await db.Bids.AsNoTracking().Where(b => b.TenderId == id && b.SubmittedAt != null &&
                b.ComplianceStatus != "NonCompliant" && b.Status != "Withdrawn" && b.Status != "Rejected" &&
                b.Status != "Draft" && b.Status != "NonCompliant")
                .Include(b => b.Invitation).Include(b => b.BidItems).ToListAsync(ct);
            var input = SpecMatchInputFor(tender, bids);
            if (input.Items.Count == 0 || input.Offers.Count == 0)
                return RedirectToAction(nameof(Offers), new { tenderId = id });

            var fingerprint = SpecMatchService.Fingerprint(input);
            var latest = await LatestSpecMatchAsync(id, ct);
            if (latest?.Fingerprint != fingerprint)
            {
                var result = await service.RunAsync(input, ct);
                db.SpecMatchAnalyses.Add(new SpecMatchAnalysis
                {
                    TenderId = id,
                    Fingerprint = fingerprint,
                    ResultJson = JsonSerializer.Serialize(result.Cells),
                    ModelName = result.ModelName,
                    AiAvailable = result.AiAvailable,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
                TempData["TenderSuccess"] = "Spec match updated";
            }
        }
        finally
        {
            gate.Release();
        }

        return RedirectToAction(nameof(Offers), null, new { tenderId = id }, "spec-match");
    }

    private async Task<OfficerSpecMatchPage> LoadSpecMatchAsync(Tender tender, List<Bid> bids, CancellationToken ct)
    {
        var input = SpecMatchInputFor(tender, bids);
        var latest = await LatestSpecMatchAsync(tender.TenderId, ct);
        // The legal warranty is compared in code on every page load, so it needs no AI run and never goes stale.
        var offerIds = input.Offers.Select(b => b.BidId).ToArray();
        var legal = (await db.BidGuarantees.AsNoTracking().Where(g => g.Kind == "Legal" && offerIds.Contains(g.BidId)).ToListAsync(ct))
            .GroupBy(g => g.BidId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());
        if (latest is null) return new(input, null, false, null, legal);
        if (latest.Fingerprint != SpecMatchService.Fingerprint(input))
            return new(input, null, true, latest.CreatedAt, legal);

        var cells = JsonSerializer.Deserialize<SpecMatchCell[]>(latest.ResultJson) ?? [];
        return new(input, new(cells, latest.AiAvailable, latest.ModelName), false, latest.CreatedAt, legal);
    }

    private Task<SpecMatchAnalysis?> LatestSpecMatchAsync(int tenderId, CancellationToken ct) =>
        db.SpecMatchAnalyses.AsNoTracking().Where(a => a.TenderId == tenderId)
            .OrderByDescending(a => a.SpecMatchAnalysisId).FirstOrDefaultAsync(ct);

    private static SpecMatchInput SpecMatchInputFor(Tender tender, IEnumerable<Bid> bids)
    {
        var offers = bids.Where(b => b.SubmittedAt != null && b.ComplianceStatus != "NonCompliant" &&
                b.Status is not ("Withdrawn" or "Rejected" or "Draft" or "NonCompliant"))
            .GroupBy(b => b.Invitation.SupplierProfileId)
            .Select(group => group.OrderByDescending(b => b.SubmittedAt).ThenByDescending(b => b.BidId).First())
            .OrderBy(b => b.BidId).ToArray();
        var items = tender.PurchaseRequest.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).ToArray();
        return new(tender.TenderId, items, offers);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseSubmissionsEarly(int tenderId, bool sendToCommittee = false)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == officerId);
        if (tender == null) return NotFound();

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId) || await db.Evaluations.AnyAsync(e => e.TenderId == tenderId))
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
                : offers.Where(b => b.ComplianceStatus == "Compliant").Select(b => b.Invitation.SupplierProfileId).Distinct().Count() < TenderOfferExtension.RequiredCompliantOffers
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
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.Include(t => t.OfferExtensions)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == officerId);
        if (tender == null) return NotFound();

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId) || await db.Evaluations.AnyAsync(e => e.TenderId == tenderId))
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
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @locked int; SELECT @locked = TenderId FROM dbo.Tenders WITH (UPDLOCK, HOLDLOCK) WHERE TenderId = {tenderId};");
        var tender = await db.Tenders.Include(t => t.OfferExtensions)
            .SingleOrDefaultAsync(t => t.TenderId == tenderId && t.CreatedByUserId == officerId);
        if (tender == null) return NotFound();

        IActionResult ReturnError(string message)
        {
            TempData["TenderError"] = message;
            return RedirectToAction(nameof(Offers), new { tenderId });
        }

        if (await db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId) || await db.Evaluations.AnyAsync(e => e.TenderId == tenderId))
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

    [HttpGet]
    public async Task<IActionResult> ReportPdf(int? id)
    {
        var result=await Reports(id);
        if(result is not ViewResult {Model:List<EvaluationReport> reports} view)return result;
        if(id.HasValue)
        {
            var report=(EvaluationReport)view.ViewData["SelectedReport"]!;
            return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerReport(report),$"officer-report-{id}.pdf");
        }
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerReports(reports),"officer-reports.pdf");
    }
    [HttpGet]
    public async Task<IActionResult> EvaluationReportPdf(int tenderId)
    {
        var result=await Evaluations(tenderId);
        if(result is not ViewResult view)return result;
        if(view.ViewData["EvaluationReport"] is not EvaluationReport report||report.TenderId!=tenderId)return NotFound();
        return ReportPdfDownload.Create(this,()=>ReportPdfExport.OfficerEvaluation(view.ViewData),$"officer-evaluation-{tenderId}.pdf");
    }

    // AI document review for one submitted offer. Read-only: it never changes the compliance decision.
    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("AiChat")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AiOfferReview(int id, [FromServices] OfferPreCheck precheck, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Same scope as the Offers page: submitted offers on tenders this officer created.
        var bid = await db.Bids.AsNoTracking()
            .Include(b => b.BidItems)
            .Include(b => b.BidDocuments)
            .Include(b => b.Invitation).ThenInclude(i => i.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(b => b.BidId == id && b.SubmittedAt != null && b.Invitation.Tender.CreatedByUserId == userId, ct);
        if (bid == null) return NotFound(new { error = "This offer is not available in your workspace." });

        var tender = bid.Invitation.Tender;
        try
        {
            var review = await precheck.ReviewAsync(bid, tender, tender.PurchaseRequest.PurchaseRequestItems.ToList(), DateTime.UtcNow, ct);
            return Ok(new
            {
                summary = review.Summary,
                findings = review.Findings,
                hiddenUnverifiable = review.HiddenUnverifiable,
                documentNotes = review.DocumentNotes
            });
        }
        catch (AiLimitReachedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The AI review is unavailable right now. The automatic checks above still apply; try again in a moment." });
        }
    }
}
