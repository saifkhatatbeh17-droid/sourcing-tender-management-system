using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

// Read-only view of every AI call recorded by the AI gateway (AiInteractions).
public partial class AdminController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AiLog(string? search, string? feature, string? workspace, string? outcome,
        DateOnly? from, DateOnly? to, int page = 1, CancellationToken ct = default)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        feature = string.IsNullOrWhiteSpace(feature) ? null : feature;
        workspace = string.IsNullOrWhiteSpace(workspace) ? null : workspace;
        outcome = outcome is AiInteraction.Succeeded or AiInteraction.Failed or AiInteraction.Cancelled or AiInteraction.LimitReached ? outcome : null;

        var query =
            from a in db.AiInteractions.AsNoTracking()
            join u in db.Users.AsNoTracking() on a.UserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new { a, UserName = u != null ? u.FullName : null, UserEmail = u != null ? u.Email : null };

        if (search != null)
            query = query.Where(x => (x.UserName != null && x.UserName.Contains(search))
                || (x.UserEmail != null && x.UserEmail.Contains(search)));
        if (feature != null) query = query.Where(x => x.a.Feature == feature);
        if (workspace != null) query = query.Where(x => x.a.Workspace == workspace);
        if (outcome != null) query = query.Where(x => x.a.Outcome == outcome);
        if (from is { } start) { var since = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query
            .Where(x => x.a.CreatedAtUtc >= since); }
        if (to is { } end) { var until = end.AddDays(1)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query
            .Where(x => x.a.CreatedAtUtc < until); }

        var total = await query.CountAsync(ct);
        var succeeded = await query.CountAsync(x => x.a.Outcome == AiInteraction.Succeeded, ct);
        var failed = await query.CountAsync(x => x.a.Outcome == AiInteraction.Failed, ct);
        var cancelled = await query.CountAsync(x => x.a.Outcome == AiInteraction.Cancelled, ct);
        var limited = await query.CountAsync(x => x.a.Outcome == AiInteraction.LimitReached, ct);
        var averageMs = succeeded > 0 ? await query.Where(x => x.a.Outcome == AiInteraction.Succeeded)
            .AverageAsync(x => (double)x.a.DurationMs, ct) : (double?)null;
        var checkedCount = await query.CountAsync(x => x.a.SchemaConforms != null, ct);
        var conforming = await query.CountAsync(x => x.a.SchemaConforms == true, ct);

        var features = await query.GroupBy(x => x.a.Feature).Select(g => new
        {
            Feature = g.Key,
            Calls = g.Count(),
            Succeeded = g.Count(x => x.a.Outcome == AiInteraction.Succeeded),
            AverageMs = g.Where(x => x.a.Outcome == AiInteraction.Succeeded).Average(x => (double?)x.a.DurationMs),
            Checked = g.Count(x => x.a.SchemaConforms != null),
            Conforming = g.Count(x => x.a.SchemaConforms == true),
            PromptVersions = g.Select(x => x.a.PromptVersion).Distinct().Count(),
            LastUsed = g.Max(x => x.a.CreatedAtUtc)
        }).OrderByDescending(f => f.Calls).ToListAsync(ct);

        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)AiLogPageViewModel.PageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await query.OrderByDescending(x => x.a.AiInteractionId)
            .Skip((page - 1) * AiLogPageViewModel.PageSize).Take(AiLogPageViewModel.PageSize)
            .Select(x => new AiLogRow(x.a.AiInteractionId, x.a.CreatedAtUtc, x.UserName, x.UserEmail, x.a.Workspace, x.a.Feature,
                x.a.RecordType, x.a.RecordId, x.a.Outcome, x.a.Model, x.a.DurationMs, x.a.SchemaConforms, x.a.SchemaIssues,
                x.a.ErrorType, x.a.PromptVersion, x.a.InputChars, x.a.OutputChars, x.a.OutputPreview, x.a.IsInteractive))
            .ToListAsync(ct);

        ViewData["Title"] = "AI log";
        return View(new AiLogPageViewModel
        {
            Search = search, Feature = feature, Workspace = workspace, Outcome = outcome, From = from, To = to,
            Page = page, TotalPages = pages, TotalMatching = total,
            Summary = new AiLogSummary(total, succeeded, failed, cancelled, limited, averageMs,
                checkedCount > 0 ? conforming / (double)checkedCount : null),
            Features = features.Select(f => new AiLogFeatureRow(f.Feature, f.Calls, f.Calls > 0 ? f.Succeeded / (double)f.Calls : 0,
                f.AverageMs, f.Checked > 0 ? f.Conforming / (double)f.Checked : null, f.PromptVersions, f.LastUsed)).ToList(),
            Rows = rows,
            FeatureOptions = await db.AiInteractions.AsNoTracking()
                .Select(a => a.Feature)
                .Distinct()
                .OrderBy(f => f)
                .ToListAsync(ct),
            WorkspaceOptions = await db.AiInteractions.AsNoTracking()
                .Select(a => a.Workspace)
                .Distinct()
                .OrderBy(w => w)
                .ToListAsync(ct)
        });
    }
}
