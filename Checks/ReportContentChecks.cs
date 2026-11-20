using System.Net;
using System.Text.Json;
using SourcingTenderManagement.Services.Evaluation;
using SourcingTenderManagement.Services.Reports;

namespace SourcingTenderManagement.Checks;

public static class ReportContentChecks
{
    public static async Task RunAsync()
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); }
        var blocks = ReportContent.Parse("Chosen offer\r\nSupplier: 1,123.00 JOD\r\n\r\n## Financial review\n- Higher cost\n- Faster delivery\n\nConclusion\n<script>alert(1)</script>");
        Check(blocks.Count(b => b.Kind == "heading") == 3 && blocks.Single(b => b.Kind == "list").Lines.Length == 2, "Report sections or list parsing failed.");
        Check(blocks.Last().Lines[0] == "<script>alert(1)</script>", "Source text must remain text for Razor encoding.");
        var spans = ReportContent.Inline("Offer #235 costs 1,123.00 JOD at 50% financial. **Supplier-stated** terms.").ToArray();
        Check(spans.Count(s => s.Emphasis) == 1 && string.Concat(spans.Select(s => s.Text)).Contains("Supplier-stated"), "Financial facts or emphasis formatting failed.");
        Check(ReportContent.Parse("Old saved report without headings.").Single().Kind == "paragraph", "Historical report compatibility failed.");
        var page = CommitteeScoringChecks.Fixture();
        page.Offers[0].BidItems.Single().UnitPrice = 112.3m;
        page.Offers[1].BidItems.Single().UnitPrice = 109.2m;
        page.Offers[0].DeliveryDays = 3;
        page.Offers[1].DeliveryDays = 7;
        page.Offers[0].Invitation.SupplierProfile.CompanyName = "Rimal Pharmaceutical Distribution";
        // Each offer must have its own supplier for named-comparison checks.
        var cedar = new SourcingTenderManagement.Models.Entities.Invitation { SupplierProfile = new() { CompanyName = "Cedar Pharmacy Wholesale" } };
        var cedarBid = page.Offers[1];
        page.Tender!.Invitations.First().Bids.Remove(cedarBid);
        cedar.Bids.Add(cedarBid); cedarBid.Invitation = cedar; page.Tender.Invitations.Add(cedar);
        foreach (var contradiction in new[] {
            "Cedar Pharmacy Wholesale offers lower total cost and faster delivery than Rimal Pharmaceutical Distribution.",
            "Cedar Pharmacy Wholesale provides faster delivery within 7 days, while Rimal Pharmaceutical Distribution delivers in 3 days.",
            "Rimal Pharmaceutical Distribution is cheaper than Cedar Pharmacy Wholesale." })
        {
            try { BidNarrativeValidation.Check(page, 2, contradiction); throw new Exception("Contradictory comparison was accepted."); }
            catch (InvalidOperationException) { }
        }
        BidNarrativeValidation.Check(page, 2, "Cedar Pharmacy Wholesale costs less. Rimal Pharmaceutical Distribution has faster delivery than Cedar Pharmacy Wholesale.");
        using var facts = JsonDocument.Parse(BidRecommendationService.BidData(page, 50));
        var prices = facts.RootElement.GetProperty("objectiveFacts").GetProperty("priceAssessment").EnumerateArray().ToArray();
        Check(!prices[0].GetProperty("isLowestTotal").GetBoolean() && prices[0].GetProperty("costAboveLowestInSameCurrency").GetDecimal() == 31
            && prices[1].GetProperty("isLowestTotal").GetBoolean(), "1123 must be recorded as more expensive than 1092.");
        var handler = new PromptHandler();
        using var http = new HttpClient(handler);
        var ai = new BidRecommendationService(http, VllmAiChecks.LocalFixtureConfiguration());
        var result = await ai.Recommend(page, 50, default);
        Check(result.ModelName == "fixture/model", "Comparison must identify the model that returned it.");
        Check(ReportContent.Parse(result.Scenarios[0].Reason).Count(b => b.Kind == "heading") == 4, "Structured recommendation lost one of its four sections.");
        Check(handler.Calls == 2, "Contradictory delivery explanation must be rejected and retried once.");

        // 3-offer comparison checks: middle offer comparatives and post-claim target mentions must not false-fail.
        var levant = new SourcingTenderManagement.Models.Entities.Invitation { SupplierProfile = new() { CompanyName = "Levant Medicine Supply" } };
        var middleBid = new SourcingTenderManagement.Models.Entities.Bid
        {
            BidId = 3, TenderId = 1, Currency = "JOD", DeliveryDays = 5, Status = "Compliant", ComplianceStatus = "Compliant",
            Invitation = levant, BidItems = [new() { Quantity = 10, UnitPrice = 110.5m }]
        };
        levant.Bids.Add(middleBid);
        page.Tender.Invitations.Add(levant);
        BidNarrativeValidation.Check(page, 2, "The other bids are more expensive, making Cedar Pharmacy Wholesale the lowest cost choice.");
        BidNarrativeValidation.Check(page, 2, "No material tradeoff is evident; the alternative bids are more expensive.");
        BidNarrativeValidation.Check(page, 1, "No material tradeoff is evident; the alternative bids are slower.");
        BidNarrativeValidation.Check(page, 2, "Apart from the higher cost of the other offers, there are no material trade-offs for Cedar Pharmacy Wholesale.");
        BidNarrativeValidation.Check(page, 2, "Cedar Pharmacy Wholesale is cheapest; Levant Medicine Supply is cheaper than Rimal Pharmaceutical Distribution.");
        BidNarrativeValidation.Check(page, 3, "Higher total cost (1,105.00 vs 1,092.00 JOD) than Bid #2. Slower delivery.", true);
        BidNarrativeValidation.Check(page, 3, "Slower than the 3-day offer, yet faster than the 7-day offer.", true);
        foreach (var threeOfferContradiction in new[] {
            "Higher total cost than Bid #1.",
            "Slower delivery than Cedar Pharmacy Wholesale." })
        {
            try { BidNarrativeValidation.Check(page, 3, threeOfferContradiction, true); throw new Exception("3-offer contradiction accepted."); }
            catch (InvalidOperationException) { }
        }
        try { BidNarrativeValidation.Check(page, 2, "The alternative bids are slower."); throw new Exception("Invalid alternative-bids claim accepted."); }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS: report headings/lists, legacy text, emphasized facts, accurate price order and detailed structured AI instructions.");
    }

    internal static string StructuredResponse(BidRecommendation result) => JsonSerializer.Serialize(new {
        scenarios = result.Scenarios.Select(s => new { s.FinancialWeight, s.RecommendedBidId, reason = new {
            decisionBasis = "The recommendation uses the saved financial and technical percentages to balance the documented offer details. Committee members still make the final supplier choice.",
            costAndTerms = "The complete offer totals, including stated taxes and fees, are the basis for cost comparison. The lower priced alternative offers a financial advantage that must be acknowledged.",
            technicalFitAndDelivery = "Delivery and documented product specifications must be checked against the actual requested requirements. Faster delivery is an advantage only to the extent that it serves the documented need.",
            tradeoffsAndLimitations = "Supplier statements remain claims to verify before purchase. Missing evidence must be disclosed, and no unsupported urgency, budget or technical superiority should be invented."
        }}), result.MissingInformation, result.Comparisons, result.Report
    }, BidRecommendationService.Json);

    private sealed class PromptHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var system = input.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            if (!system.Contains("140–220 words") || !system.Contains("Tradeoffs and limitations") || !system.Contains("Do not output HTML") || !system.Contains("HIGHER cost"))
                throw new Exception("AI prompt lost detail, format or price-accuracy instructions.");
            var result = new BidRecommendation([new(50, 1, "Decision basis\nFaster delivery carries a higher total cost; the committee must weigh this tradeoff.")], [],
                [new(1, "Faster delivery", "Higher total cost"), new(2, "Lower total cost", "Slower delivery")], "Chosen offer\nOffer #1");
            var response = System.Text.Json.Nodes.JsonNode.Parse(StructuredResponse(result))!;
            if (Calls == 1) response["scenarios"]![0]!["reason"]!["technicalFitAndDelivery"] = "This alternative has a 7-day delivery, which is faster than the other supplier's three-day delivery. This is a technical advantage to review against the actual requested requirements.";
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                choices = new[] { new { finish_reason = "stop", message = new { content = response.ToJsonString() } } }
            }) };
        }
    }
}
