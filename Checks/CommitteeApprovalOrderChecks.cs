using System.Net;
using System.Text;
using System.Text.Json;
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
        var tender = new Tender { Status = "UnderEvaluation", PurchaseRequest = new() };
        tender.EvaluationCriteria = [new() { Category = "Financial", Weight = 60 }, new() { Category = "Requirements", Weight = 40 }];
        for (var i = 1; i <= 4; i++)
            tender.CommitteeAssignments.Add(new() { CommitteeAssignmentId = i, CommitteeType = "Evaluation", Position = i, UserId = "member" + i });
        var invitation = new Invitation { SupplierProfile = new() { CompanyName = "Test supplier" } };
        invitation.Bids = [new() { BidId = 45, Currency = "JOD", Invitation = invitation }, new() { BidId = 47, Currency = "JOD", Invitation = invitation }];
        invitation.Bids.ElementAt(0).DeliveryDays = 7;
        invitation.Bids.ElementAt(1).DeliveryDays = 3;
        invitation.Bids.ElementAt(0).BidItems.Add(new() { Quantity = 1, UnitPrice = 100 });
        invitation.Bids.ElementAt(1).BidItems.Add(new() { Quantity = 1, UnitPrice = 200 });
        tender.Invitations.Add(invitation);
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = "member4" };
        for (var i = 1; i <= 4; i++) { page.UserId = "member" + i; Check(CommitteeConsensus.ConfirmWeight(page, 60, DateTime.UtcNow) == null, "Percentage confirmation failed."); }
        tender.EvaluationReport = new() { Status = "Draft", RecommendedBidId = 47, CommitteeSummary = "Old AI draft" };
        Check(!page.SelectionReady, "An old AI draft must not count as an explicit committee selection.");
        tender.EvaluationReport.CommitteeSummary = null;
        Check(!page.SelectionReady, "Manual selection cannot bypass all-offer scoring.");
        foreach (var offer in page.Offers) for (var i = 1; i <= 4; i++)
        {
            page.UserId = "member" + i;
            Check(AllOfferScoring.Save(page, offer.BidId, offer.BidId == 47 ? 50 : 20, offer.BidId == 47 ? 30 : 10, null, DateTime.UtcNow) == null, "Offer review failed.");
        }
        AllOfferScoring.SelectWinner(page, DateTime.UtcNow);
        Check(page.SelectionReady && page.Tender!.EvaluationReport!.RecommendedBidId == 47 && !page.ReportReady, "Highest score must reach report step before handoff.");
        var fourth = tender.TenderApprovals.Last(); tender.TenderApprovals.Remove(fourth);
        tender.EvaluationReport.CommitteeSummary = "Test report";
        EvaluationCommitteeController.CompleteWeightApproval(page);
        Check(tender.Status == "UnderEvaluation", "Three approvals cannot send the report.");
        tender.TenderApprovals.Add(fourth); tender.EvaluationReport.CommitteeSummary = null;
        EvaluationCommitteeController.CompleteWeightApproval(page);
        Check(tender.Status == "UnderEvaluation", "All reviews wait for final report generation.");
        tender.EvaluationReport.CommitteeSummary = "Report about the highest-scoring offer";
        EvaluationCommitteeController.CompleteWeightApproval(page);
        Check(tender.Status == "AwaitingOfficerSummary" && tender.EvaluationReport.Status == "CommitteeApproved", "Completed scored report must send with all four approvals.");

        using var client = new HttpClient(new ReportHandler());
        var ai = new BidRecommendationService(client, VllmAiChecks.LocalFixtureConfiguration());
        var report = await ai.Recommend(page, 60, CancellationToken.None, 47);
        Check(report.Scenarios.Single().RecommendedBidId == 47, "Report generation substituted another offer.");
        Console.WriteLine("PASS: explicit selection, approvals before report, no premature handoff, preserved approvals, report constrained to selected offer.");
    }

    private sealed class ReportHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var schema = input.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("properties");
            var choices = schema.GetProperty("scenarios").GetProperty("items").GetProperty("properties").GetProperty("recommendedBidId").GetProperty("enum");
            var userPrompt = input.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            var thinkingEnabled = input.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean();
            if (thinkingEnabled || choices.GetArrayLength() != 1 || choices[0].GetInt32() != 47 || !userPrompt.Contains("already selected bid 47"))
                throw new Exception("Final-report request did not constrain vLLM to the committee's selection with thinking disabled.");
            var result = new BidRecommendation([new(60, 47, "Selected by the committee")], [],
                [new(45, "Lower cost", "Slower delivery"), new(47, "Faster delivery", "Higher cost")], "Report about bid 47");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                choices = new[] { new { finish_reason = "stop", message = new { content = ReportContentChecks.StructuredResponse(result) } } }
            }), Encoding.UTF8, "application/json") };
        }
    }
}
