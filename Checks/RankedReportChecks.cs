using System.Net;
using System.Text.Json;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Checks;

public static class RankedReportChecks
{
    public static async Task RunAsync()
    {
        var page = CommitteeScoringChecks.Fixture();
        page.Tender!.Invitations.Clear();
        for (var id = 1; id <= 4; id++)
        {
            var invitation = new Invitation { SupplierProfile = new() { CompanyName = "Supplier " + id } };
            invitation.Bids.Add(new Bid { BidId = id, TenderId = 1, Invitation = invitation, Currency = "JOD",
                Status = "Compliant", ComplianceStatus = "Compliant", DeliveryDays = id + 2,
                BidItems = [new() { Quantity = 1, UnitPrice = 110 - id * 10 }] });
            page.Tender.Invitations.Add(invitation);
        }
        var now = DateTime.UtcNow;
        foreach (var member in page.Members)
        {
            page.UserId = member.UserId;
            if (CommitteeConsensus.ConfirmWeight(page, 50, now) != null) throw new Exception("Fixture percentages failed.");
        }
        foreach (var member in page.Members)
        foreach (var bid in page.Offers)
        {
            page.UserId = member.UserId;
            if (AllOfferScoring.Save(page, bid.BidId, 50 - bid.BidId * 5, 50 - bid.BidId * 5, null, now.AddSeconds(1)) != null)
                throw new Exception("Fixture review failed.");
        }
        AllOfferScoring.SelectWinner(page, now.AddSeconds(2));
        foreach (var mode in new[] { "valid", "contradiction", "wrong-winner", "offline" })
        {
            var handler = new Handler(mode);
            using var http = new HttpClient(handler);
            var service = new BidRecommendationService(http, VllmAiChecks.LocalFixtureConfiguration());
            var result = await service.FinalReport(page, 50, default);
            var report = result.Report!;
            Check(result.Scenarios.Single().RecommendedBidId == 1, "AI changed the winner.");
            Check(report.Contains("Supplier 1") && report.Contains("Supplier 2") && report.Contains("Supplier 3") && !report.Contains("Supplier 4"), "Report must cover only the top three.");
            Check(report.Contains("10 JOD lower") && report.Contains("1 days slower"), "Trusted comparisons lost direction.");
            Check(!report.Contains("cheaper despite"), "Invalid AI narrative reached the report.");
            Check(result.MissingInformation.Length == (mode == "valid" ? 0 : 1), "Unverified narrative must be disclosed.");
            Check(handler.Calls == (mode is "valid" or "offline" ? 1 : 2), "Wrong retry count.");
        }
        page.Offers[1].Currency = "USD";
        Check(!AllOfferScoring.Complete(page), "Mixed currencies must block score finalization.");
        page.Offers[1].Currency = "JOD";
        using (var http = new HttpClient(new Handler("referral")))
        {
            await new BidRecommendationService(http, VllmAiChecks.LocalFixtureConfiguration()).DraftReferral(new { offer = new { supplier = "Supplier 1" } }, default);
        }
        Console.WriteLine("PASS: top-three report, fixed winner, accurate comparisons, invalid/offline AI fallback and winner-only manager instructions.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Handler(string mode) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (mode == "offline") throw new HttpRequestException("Fixture AI unavailable.");
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var messages = input.RootElement.GetProperty("messages");
            var system = messages[0].GetProperty("content").GetString()!;
            var userPrompt = messages[1].GetProperty("content").GetString()!;
            if (mode == "referral")
            {
                Check(system.Contains("ONLY the first-ranked winning offer") && system.Contains("without listing or comparing alternatives"), "Manager scope is ambiguous.");
                return Reply(new { summary = "Supplier 1 is the recorded winner; final committee review is requested." });
            }
            Check(system.Contains("TOP THREE") && system.Contains("ONLY the first-place winner"), "Committee prompt lost its scope.");
            using var evidence = JsonDocument.Parse(userPrompt.Split("\nCorrect the previous response:")[0]);
            var ids = evidence.RootElement.GetProperty("topOffers").EnumerateArray().Select(x => x.GetProperty("bidId").GetInt32()).ToArray();
            Check(ids.SequenceEqual(new[] { 1, 2, 3 }) && !evidence.RootElement.GetRawText().Contains("Supplier 4"), "AI received offers outside shortlist.");
            return Reply(new { winnerBidId = mode == "wrong-winner" ? 2 : 1, offers = ids.Select(id => new {
                bidId = id, assessment = mode == "contradiction" ? "This supplier is cheaper despite the actual quotation facts, which must be ignored."
                    : "The requested specification provides limited evidence of product suitability. Supplier documentation requires verification before purchase.",
                caveats = "Confirm the requested quantities and packaging against the supplier documentation before making a purchase decision."
            }).ToArray() });
        }
        private static HttpResponseMessage Reply(object result) => new(HttpStatusCode.OK) {
            Content = JsonContent.Create(new {
                choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(result, BidRecommendationService.Json) } } }
            }) };
    }
}
