using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Checks;

public static class CommitteeApprovalOrderChecks
{
    public static async Task RunAsync()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var page = CommitteeScoringChecks.Fixture();
        var offers = page.Offers;
        offers[0].BidId = 45; offers[1].BidId = 47;
        var now = DateTime.UtcNow;
        for (int i = 1; i <= 4; i++) { page.UserId = "member" + i; CommitteeScoring.ConfirmWeight(page, 60, now); }
        foreach (var offer in page.Offers) for (int i = 1; i <= 4; i++)
        {
            page.UserId = "member" + i;
            CommitteeScoring.AcceptOffer(page, offer.BidId, offer.BidId == 47 ? 55 : 40, offer.BidId == 47 ? 35 : 20, null, now);
        }
        Check(CommitteeScoring.FinalizeIfReady(page, now) && page.ScoredWinnerId == 47, "Completed forms must select the highest score.");
        using var client = new HttpClient(new ReportHandler());
        var ai = new BidRecommendationService(client, new ConfigurationBuilder().Build());
        var report = await ai.Recommend(page, 60, CancellationToken.None, 47);
        Check(report.Scenarios.Single().RecommendedBidId == 47, "Report generation substituted another offer.");
        Console.WriteLine("PASS: all offer scores completed, highest score selected, AI report constrained to the scored winner.");
    }

    private sealed class ReportHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var schema = input.RootElement.GetProperty("format").GetProperty("properties");
            var choices = schema.GetProperty("scenarios").GetProperty("items").GetProperty("properties").GetProperty("recommendedBidId").GetProperty("enum");
            if (choices.GetArrayLength() != 1 || choices[0].GetInt32() != 47 || !input.RootElement.GetProperty("prompt").GetString()!.Contains("already selected bid 47"))
                throw new Exception("Final-report request did not constrain the AI to the committee's selection.");
            var result = new BidRecommendation([new(60, 47, "Selected by the committee")], [],
                [new(45, "Lower cost", "Slower delivery"), new(47, "Faster delivery", "Higher cost")], "Report about bid 47");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                done = true, done_reason = "stop", response = JsonSerializer.Serialize(result, BidRecommendationService.Json)
            }), Encoding.UTF8, "application/json") };
        }
    }
}
