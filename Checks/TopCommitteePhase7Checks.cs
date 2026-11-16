using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
namespace SourcingTenderManagement.Checks;

public static class TopCommitteePhase7Checks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
        async Task Rejects<T>(Func<Task> action, string name) where T : Exception
        { try { await action(); } catch (T) { passed++; return; } throw new Exception(name); }
        static ClaimsPrincipal Actor(string id, string role = RoleNames.TopCommittee) => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)], "Checks"));
        var db = services.GetRequiredService<ApplicationDbContext>();
        var id = await db.Users.AsNoTracking().Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.TopCommittee))).Select(u => u.Id).FirstAsync();
        var history = services.GetRequiredService<TopCommitteeHistory>();
        await Rejects<CommitteeAccessDeniedException>(() => history.GetAsync(new ClaimsPrincipal(), new()), "Anonymous history");
        await Rejects<CommitteeAccessDeniedException>(() => history.GetAsync(Actor(id, RoleNames.Supplier), new()), "Wrong role");
        await Rejects<CommitteeAccessDeniedException>(() => history.GetAsync(Actor("missing-history-account"), new()), "Stale identity");
        foreach (var invalid in new CommitteeHistoryFilter[] { new() { Status = "Invented" }, new() { Decision = "ReturnedForReview" },
            new() { Page = 0 }, new() { PageSize = 0 }, new() { PageSize = 101 }, new() { Search = new string('x', 201) } })
            await Rejects<ValidationException>(() => history.GetAsync(Actor(id), invalid), "Invalid history filter");
        var live = await history.GetAsync(Actor(id), new());
        var expected = await db.TenderApprovals.AsNoTracking().CountAsync(v => v.Stage == "Top" && v.UserId == id &&
            (v.Decision == "Approved" || v.Decision == "Rejected") && v.Tender.CommitteeAssignments.Any(a => a.CommitteeType == "Top" && a.UserId == id));
        Check(live.Total == expected, "Live SQL own assignment scope");
        Check(!db.ChangeTracker.HasChanges(), "History read-only");
        var now = DateTime.UtcNow;
        var pending = new CommitteeHistoryRow(1, 1, "Pending test tender", "AwaitingCommitteeDecision", "Approved", now, 1, null, 1, false, null);
        Check(pending.Outcome == "Pending collective decision", "One approval is not final");
        var award = pending with { VoteId = 2, TenderId = 2, Title = "Award test tender", Status = "Awarded", AwardedBidId = 2, NominatedBidId = 2, Approvals = 4, AwardedAt = now };
        Check(award.Outcome == "Awarded", "Four approval award display");
        Check((pending with { HasRejection = true }).Outcome.Contains("permanently ended"), "Terminal rejection despite status reset");
        Check((award with { HasRejection = true }).Outcome.Contains("Rejected"), "Historical rejection precedence");
        Check((pending with { Status = "Cancelled" }).Outcome == "No final committee outcome recorded", "No invented outcome");
        Check((award with { AwardedAt = null }).Outcome != "Awarded", "Incomplete award not final");
        var page = new CommitteeHistoryPage(new(), [pending, award, pending with { VoteId = 3, TenderId = 3, Title = "Rejected test tender", Decision = "Rejected", HasRejection = true }], 3);
        var html = await TopCommitteePhase2Checks.RenderAsync(services, Actor(id), "History", page);
        Check(html.Contains("Pending collective decision") && html.Contains("permanently ended") && html.Contains("Award test tender"), "History Razor outcomes");
        Check(html.Contains("/TopCommittee/TenderDetails/1") && html.Contains("/TopCommittee/TenderDetails/2"), "Authorized details link construction");
        Check(!html.Contains("SubmitVote") && !html.Contains("DeleteVote") && !html.Contains("Edit vote"), "No historical mutation controls");
        var malicious = await TopCommitteePhase2Checks.RenderAsync(services, Actor(id), "History", page with { Rows = [pending with { Title = "<script>alert(1)</script>" }] });
        Check(!malicious.Contains("<script>alert(1)</script>") && malicious.Contains("&lt;script&gt;"), "Encoded titles");
        var empty = await TopCommitteePhase2Checks.RenderAsync(services, Actor(id), "History", page with { Rows = [], Total = 0 });
        Check(empty.Contains("No votes recorded yet") && !empty.Contains("Pending test tender"), "Empty history");
        Check(!html.Contains("Private delivery clarification"), "No private conversations");
        var output = Environment.GetEnvironmentVariable("TOP_COMMITTEE_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        { await File.WriteAllTextAsync(Path.Combine(output, "history.html"), html); await File.WriteAllTextAsync(Path.Combine(output, "history-empty.html"), empty); }
        var test = Environment.GetEnvironmentVariable("TOP_COMMITTEE_HISTORY_TEST_CONNECTION");
        if (!string.IsNullOrEmpty(test))
        {
            await using var isolated = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(test).Options);
            if (!isolated.Database.GetDbConnection().Database.StartsWith("SourcingTender_Phase46_")) throw new Exception("Refuse non-isolated SQL target");
            var query = new TopCommitteeHistory(isolated, new(isolated));
            var all = await query.GetAsync(Actor("p46-member1"), new());
            Check(all.Total == 3, "SQL own votes only");
            Check(all.Rows.Single(r => r.TenderId == 1).Outcome == "Awarded" && all.Rows.Single(r => r.TenderId == 1).Approvals == 4, "SQL award");
            Check(all.Rows.Single(r => r.TenderId == 2).Decision == "Rejected" && all.Rows.Single(r => r.TenderId == 2).HasRejection, "SQL rejection");
            Check(all.Rows.Single(r => r.TenderId == 3).Outcome == "Pending collective decision", "SQL own vote collective pending");
            Check((await query.GetAsync(Actor("p46-member2"), new())).Rows.All(r => r.Decision == "Approved"), "Different member not shown rejection vote");
            Check((await query.GetAsync(Actor("p46-outsider"), new())).Total == 0, "Unassigned empty");
            Check((await query.GetAsync(Actor("p46-member1"), new() { Search = "Approval" })).Total == 1, "SQL title search");
            Check((await query.GetAsync(Actor("p46-member1"), new() { Decision = "Rejected" })).Rows.Single().TenderId == 2, "SQL decision filter");
            Check((await query.GetAsync(Actor("p46-member1"), new() { Status = "Awarded" })).Rows.Single().TenderId == 1, "SQL status filter");
            var first = await query.GetAsync(Actor("p46-member1"), new() { PageSize = 1 });
            var second = await query.GetAsync(Actor("p46-member1"), new() { PageSize = 1, Page = 2 });
            Check(first.Total == 3 && first.Rows.Count == 1 && second.Rows.Count == 1 && first.Rows[0].VoteId != second.Rows[0].VoteId, "SQL stable pagination");
            Check((await query.GetAsync(Actor("p46-member1"), new() { PageSize = 1 })).Rows[0].VoteId == first.Rows[0].VoteId, "SQL repeat stable order");
            Check(all.Rows.Select(r => r.DecidedAt).SequenceEqual(all.Rows.OrderByDescending(r => r.DecidedAt).ThenByDescending(r => r.VoteId).Select(r => r.DecidedAt)), "SQL chronological order");
            Check(!isolated.ChangeTracker.Entries().Any(), "No tracked SQL entities");
        }
        Console.WriteLine($"PASS: {passed} Phase 7 decision history checks; optional isolated SQL checks are read-only.");
    }
}
