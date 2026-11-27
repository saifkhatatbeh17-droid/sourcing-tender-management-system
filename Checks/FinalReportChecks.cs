using System.Net;
using System.Text;
using System.Text.Json;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;
namespace SourcingTenderManagement.Checks;
// Part of the --vllm-ai-checks self-test (called by VllmAiChecks).
public static class FinalReportChecks
{
    public static async Task RunAsync()
    {
        var tender = new Tender { TenderId = 1, Title = "Report check", PurchaseRequest = new(), EvaluationReport = new() { RecommendedBidId = 47 } };
        foreach (var id in new[] { 45, 47 }) tender.Invitations.Add(new() { SupplierProfile = new() { CompanyName = "Supplier " + id },
            Bids = [new() { BidId = id, Currency = "JOD", DeliveryDays = id == 47 ? 3 : 7, BidItems = [new() { Quantity = 2, UnitPrice = id == 47 ? 425 : 300 }] }] });
        foreach (var invitation in tender.Invitations) foreach (var bid in invitation.Bids) bid.Invitation = invitation;
        var page = new CommitteeEvaluationPage { Tender = tender };
        foreach (var mode in new[] { "valid", "retry", "incomplete", "wrong-selection", "missing-alternative", "invented-number" })
        {
            var handler = new Handler(mode);
            using var http = new HttpClient(handler);
            var ai = new BidRecommendationService(http, VllmAiChecks.LocalFixtureConfiguration());
            try
            {
                var result = await ai.FinalReport(page, 40, default);
                if (mode is not ("valid" or "retry")) throw new Exception("Invalid report accepted: " + mode);
                if (!new[] { "Chosen offer",
                    "Why it was chosen",
                    "Comparison",
                    "Observations",
                    "Conclusion",
                    "850.00 JOD",
                    "600.00 JOD" }.All(s => result.Report!.Contains(s))) throw new Exception("Section or trusted amount missing");
                if (result.Scenarios[0].RecommendedBidId != 47
                    || handler.Calls != (mode == "retry" ? 2 : 1)) throw new Exception("Selection or retry failed");
            }
            catch (InvalidOperationException) when (mode is "incomplete" or "wrong-selection" or "missing-alternative" or "invented-number")
            { if (handler.Calls != 2) throw new Exception("Must retry exactly once"); }
        }
        Console.WriteLine("PASS: five report sections, trusted totals, fixed manual selection, one retry, incomplete/changed/missing-comparison reports rejected.");
    }
    private sealed class Handler(string mode) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var content = new {
                selectedBidId = mode == "wrong-selection" ? 45 : 47,
                whyChosen = mode == "invented-number" ? "The total cost is within budget and falls into the acceptable range for this committee." : mode == "incomplete"
                    || mode == "retry"
                    && Calls == 1 ? "Short" : "The selected printer offers the requested network connection and warranty, with delivery within the requested timeframe.",
                comparison = mode == "missing-alternative" ? Array.Empty<object>() : new object[] { new { bidId = 45,
                    strengths = "This alternative has a lower total cost than the selected offer and passed screening.",
                    tradeoffs = "This alternative delivers more slowly than the selected offer and has a shorter warranty." } },
                observations = "No non-compliance is recorded for either eligible offer. Supplier warranty and support statements remain claims to verify.",
                conclusion = "Recommend the committee-selected offer for its documented delivery and warranty benefits, acknowledging its higher total cost."
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(content) } } }
            }), Encoding.UTF8, "application/json") });
        }
    }
}
