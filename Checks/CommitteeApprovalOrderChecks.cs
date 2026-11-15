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
        var tender = new Tender { Status = "UnderEvaluation", PurchaseRequest = new() };
        tender.EvaluationCriteria = [new() { Category = "Financial", Weight = 60 }, new() { Category = "Requirements", Weight = 40 }];
        for (var i = 1; i <= 4; i++)
            tender.CommitteeAssignments.Add(new() { CommitteeAssignmentId = i, CommitteeType = "Evaluation", Position = i, UserId = "member" + i });
        var invitation = new Invitation { SupplierProfile = new() { CompanyName = "Test supplier" } };
        invitation.Bids = [new() { BidId = 45, Currency = "JOD", Invitation = invitation }, new() { BidId = 47, Currency = "JOD", Invitation = invitation }];
        tender.Invitations.Add(invitation);
        var page = new CommitteeEvaluationPage { Tender = tender, UserId = "member4" };
        for (var i = 1; i <= 4; i++) { page.UserId = "member" + i; Check(CommitteeConsensus.ConfirmWeight(page, 60, DateTime.UtcNow) == null, "Percentage confirmation failed."); }
        tender.EvaluationReport = new() { Status = "Draft", RecommendedBidId = 47, CommitteeSummary = "Old AI draft" };
        Check(!page.SelectionReady, "An old AI draft must not count as an explicit committee selection.");
        tender.EvaluationReport.CommitteeSummary = null;
        Check(page.SelectionReady && !page.ReportReady, "A manual selection must be approvable before a report exists.");
        for (var i = 1; i <= 3; i++) tender.TenderApprovals.Add(new() { Stage = "Evaluation", Decision = "Approved", UserId = "member" + i });
        tender.EvaluationReport.CommitteeSummary = "Test report";
        EvaluationCommitteeController.CompleteWeightApproval(page);
        Check(tender.Status == "UnderEvaluation", "Three approvals must not be treated as four by counting the requester.");
        tender.TenderApprovals.Add(new() { Stage = "Evaluation", Decision = "Approved", UserId = "member4" });
        tender.EvaluationReport.CommitteeSummary = null;
        EvaluationCommitteeController.CompleteWeightApproval(page);
        Check(tender.Status == "UnderEvaluation", "Four approvals must remain pending final report generation.");
        tender.EvaluationReport.CommitteeSummary = "Report about the committee-selected offer";
        EvaluationCommitteeController.CompleteWeightApproval(page);
        Check(tender.Status == "AwaitingOfficerSummary" && tender.EvaluationReport.Status == "CommitteeApproved" && page.Approvals.Count == 4,
            "The completed report must be sent with all four approvals intact.");

        using var client = new HttpClient(new ReportHandler());
        var ai = new BidRecommendationService(client, new ConfigurationBuilder().Build());
        var report = await ai.Recommend(page, 60, CancellationToken.None, 47);
        Check(report.Scenarios.Single().RecommendedBidId == 47, "Report generation substituted another offer.");
        Console.WriteLine("PASS: explicit selection, approvals before report, no premature handoff, preserved approvals, report constrained to selected offer.");
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
