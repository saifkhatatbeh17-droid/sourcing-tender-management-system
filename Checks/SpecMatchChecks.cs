using System.Net;
using System.Text;
using System.Text.Json;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Checks;

public static class SpecMatchChecks
{
    public static async Task RunAsync()
    {
        var items = Enumerable.Range(1, 6).Select(id => new PurchaseRequestItem
        {
            PurchaseRequestItemId = id, ItemName = $"Item {id}", Unit = "each", Quantity = 2, Specifications = "316L steel"
        }).ToArray();
        var bid = new Bid
        {
            BidId = 49,
            TechnicalDetails = "Six-month support",
            BidItems = Enumerable.Range(2, 5).Select(id => new BidItem
            {
                PurchaseRequestItemId = id, Quantity = id == 2 ? 1 : 2, OfferedSpecifications = "316L   STEEL"
            }).ToList()
        };
        var input = new SpecMatchInput(41, items, [bid]);
        var config = VllmAiChecks.LocalFixtureConfiguration();
        var store = new InMemoryAiInteractionStore();
        var fixture = new Fixture("""
            {"cells":[
              {"bidId":49,"itemId":1,"verdict":"match","reason":"Override","evidence":"316L steel"},
              {"bidId":49,"itemId":2,"verdict":"match","reason":"Override","evidence":"316L steel"},
              {"bidId":49,"itemId":3,"verdict":"match","reason":"Meets the grade.","evidence":"316L steel"},
              {"bidId":49,"itemId":4,"verdict":"match","reason":"Invented quote.","evidence":"Titanium"},
              {"bidId":49,"itemId":6,"verdict":42,"reason":"Malformed.","evidence":"316L steel"},
              {"bidId":999,"itemId":1,"verdict":"match","reason":"Unknown bid.","evidence":"316L steel"},
              {"bidId":49,"itemId":999,"verdict":"match","reason":"Unknown item.","evidence":"316L steel"}
            ]}
            """);
        using var http = new HttpClient(fixture);
        var service = new SpecMatchService(http, config, new AiGateway(store));
        var result = await service.RunAsync(input, default);
        var cells = result.Cells.ToDictionary(c => c.ItemId);
        Require(result.AiAvailable && result.ModelName == "fixture/model" && cells.Count == 6, "Unexpected AI outcome or unknown pair leaked.");
        Require(cells[1] is { Verdict: "Missing", Source: "code" }, "AI overrode the missing item.");
        Require(cells[2] is { Verdict: "Partial", Source: "code" }, "AI overrode the short quantity.");
        Require(cells[3] is { Verdict: "Match", Source: "ai", Evidence: "316L steel" }, "Normalized quote did not verify.");
        Require(cells[4] is { Verdict: "NeedsCheck", Evidence: null }, "Unverifiable quote surfaced.");
        Require(cells[5].Verdict == "NotAssessed", "Omitted cell was not marked.");
        Require(cells[6].Verdict == "NeedsCheck", "Malformed cell was not downgraded.");
        Require(fixture.Calls == 1 && store.Entries.Single() is { Feature: "evaluation.spec-match", RecordId: 41 },
            "Expected one logged call per tender.");
        using var promptJson = JsonDocument.Parse(fixture.UserPrompt!);
        var offer = promptJson.RootElement.GetProperty("offers")[0];
        Require(offer.GetProperty("technicalDetails").GetString()!.StartsWith("<untrusted_data ", StringComparison.Ordinal),
            "Supplier general text is missing its untrusted wrapper.");
        var sent = offer.GetProperty("items").EnumerateArray().ToArray();
        Require(sent.Select(x => x.GetProperty("itemId").GetInt32()).SequenceEqual([3, 4, 5, 6]),
            "Only quantity-eligible pairs should be sent.");
        Require(sent[0].GetProperty("offeredSpecifications")[0].GetString()!.StartsWith("<untrusted_data ", StringComparison.Ordinal),
            "Supplier item text is missing its untrusted wrapper.");

        var fingerprint = SpecMatchService.Fingerprint(input);
        Require(fingerprint == SpecMatchService.Fingerprint(input with { Items = items.Reverse().ToArray() }), "Item order changed the fingerprint.");
        bid.BidItems.First().OfferedSpecifications = "304 steel";
        Require(fingerprint != SpecMatchService.Fingerprint(input), "Offer text did not affect the fingerprint.");

        foreach (var failure in new[] { new Fixture(null, HttpStatusCode.TooManyRequests), new Fixture(null), new Fixture("{\"other\":true}") })
        {
            using var unavailable = new HttpClient(failure);
            var fallback = await new SpecMatchService(unavailable, config).RunAsync(input, default);
            Require(!fallback.AiAvailable && fallback.ModelName == null &&
                fallback.Cells.Single(c => c.ItemId == 1).Verdict == "Missing" &&
                fallback.Cells.Single(c => c.ItemId == 2).Verdict == "Partial" &&
                fallback.Cells.Where(c => c.ItemId > 2).All(c => c.Verdict == "NotAssessed"), "Incorrect AI failure fallback.");
        }
        Console.WriteLine("PASS: spec match quantity, evidence, malformed and omitted cells, fingerprint, logging and AI fallback.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(string? answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? UserPrompt { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            UserPrompt = body.RootElement.GetProperty("messages").EnumerateArray()
                .Last(x => x.GetProperty("role").GetString() == "user").GetProperty("content").GetString();
            if (status != HttpStatusCode.OK) return new HttpResponseMessage(status) { Content = new StringContent("{}") };
            if (answer is null) throw new HttpRequestException("Fixture network failure.");
            var payload = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } });
            return new HttpResponseMessage(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
}
