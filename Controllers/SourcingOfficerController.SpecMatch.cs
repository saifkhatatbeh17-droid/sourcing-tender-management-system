using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Controllers;

public sealed record OfficerSpecMatchPage(SpecMatchInput Input, SpecMatchResult? Result, bool Outdated, DateTime? CreatedAt);

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
            var bids = await db.Bids.AsNoTracking().Where(b => b.TenderId == id && b.SubmittedAt != null &&
                b.ComplianceStatus == "Compliant" && b.Status != "Withdrawn" && b.Status != "Rejected" &&
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

        return RedirectToAction(nameof(Offers), new { tenderId = id });
    }

    private async Task<OfficerSpecMatchPage> LoadSpecMatchAsync(Tender tender, List<Bid> bids, CancellationToken ct)
    {
        var input = SpecMatchInputFor(tender, bids);
        var latest = await LatestSpecMatchAsync(tender.TenderId, ct);
        if (latest is null) return new(input, null, false, null);
        if (latest.Fingerprint != SpecMatchService.Fingerprint(input))
            return new(input, null, true, latest.CreatedAt);

        var cells = JsonSerializer.Deserialize<SpecMatchCell[]>(latest.ResultJson) ?? [];
        return new(input, new(cells, latest.AiAvailable, latest.ModelName), false, latest.CreatedAt);
    }

    private Task<SpecMatchAnalysis?> LatestSpecMatchAsync(int tenderId, CancellationToken ct) =>
        db.SpecMatchAnalyses.AsNoTracking().Where(a => a.TenderId == tenderId)
            .OrderByDescending(a => a.SpecMatchAnalysisId).FirstOrDefaultAsync(ct);

    private static SpecMatchInput SpecMatchInputFor(Tender tender, IEnumerable<Bid> bids)
    {
        var offers = bids.Where(b => b.SubmittedAt != null && b.ComplianceStatus == "Compliant" &&
                b.Status is not ("Withdrawn" or "Rejected" or "Draft" or "NonCompliant"))
            .GroupBy(b => b.Invitation.SupplierProfileId)
            .Select(group => group.OrderByDescending(b => b.SubmittedAt).ThenByDescending(b => b.BidId).First())
            .OrderBy(b => b.BidId).ToArray();
        var items = tender.PurchaseRequest.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId).ToArray();
        return new(tender.TenderId, items, offers);
    }
}
