using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Compliance;

namespace SourcingTenderManagement.Services.Evaluation;

// Spec match: compares each offered item's specification with what was requested,
// first with simple code rules and then with the AI for the unclear cases.
public sealed record SpecMatchCell(int BidId, int ItemId, string Verdict, string? Reason, string? Evidence, string Source);
public sealed record SpecMatchResult(IReadOnlyList<SpecMatchCell> Cells, bool AiAvailable, string? ModelName);

public sealed record SpecMatchInput(int TenderId, IReadOnlyList<PurchaseRequestItem> Items, IReadOnlyList<Bid> Offers);

public sealed class SpecMatchService(HttpClient http, IConfiguration configuration, AiGateway? gateway = null)
{
    private sealed record Candidate(int BidId, int ItemId, PurchaseRequestItem Request, Bid Offer, BidItem[] Lines);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>("""
        {"type":"object","additionalProperties":false,"required":["cells"],"properties":{"cells":{
        "type":"array","items":{"type":"object","additionalProperties":false,
        "required":["bidId","itemId","verdict","reason","evidence"],"properties":{
        "bidId":{"type":"integer"},"itemId":{"type":"integer"},
        "verdict":{"type":"string","enum":["match","partial","missing"]},
        "reason":{"type":"string","maxLength":160},"evidence":{"type":"string","maxLength":200}
        }}}}}
        """);

    public static string Fingerprint(SpecMatchInput input)
    {
        var data = new
        {
            Items = input.Items.OrderBy(x => x.PurchaseRequestItemId)
                .Select(x => new { x.PurchaseRequestItemId, x.ItemName, x.Quantity, x.Unit, x.Specifications }),
            Offers = input.Offers.OrderBy(x => x.BidId).Select(x => new
            {
                x.BidId,
                x.TechnicalDetails,
                Lines = x.BidItems.OrderBy(y => y.PurchaseRequestItemId).ThenBy(y => y.BidItemId)
                    .Select(y => new { y.PurchaseRequestItemId, y.Quantity, y.OfferedSpecifications })
            })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data, Json)));
    }

    public async Task<SpecMatchResult> RunAsync(SpecMatchInput input, CancellationToken ct)
    {
        var cells = new Dictionary<(int, int), SpecMatchCell>();
        var candidates = new List<Candidate>();
        foreach (var bid in input.Offers)
        foreach (var item in input.Items)
        {
            var key = (bid.BidId, item.PurchaseRequestItemId);
            var lines = bid.BidItems.Where(x => x.PurchaseRequestItemId == item.PurchaseRequestItemId).ToArray();
            var quantity = lines.Sum(x => x.Quantity);
            if (lines.Length == 0)
                cells[key] = new(key.BidId, key.PurchaseRequestItemId, "Missing", "Not offered", null, "code");
            else if (quantity < item.Quantity)
                cells[key] = new(key.BidId, key.PurchaseRequestItemId, "Partial",
                    string.Create(CultureInfo.InvariantCulture, $"Offered {quantity:0.###} of {item.Quantity:0.###} {item.Unit}"), null, "code");
            else
            {
                cells[key] = new(key.BidId, key.PurchaseRequestItemId, "NotAssessed", "No verified specification assessment is available.", null, "code");
                candidates.Add(new(key.BidId, key.PurchaseRequestItemId, item, bid, lines));
            }
        }
        if (candidates.Count == 0) return new(cells.Values.ToArray(), true, null);

        try
        {
            var prompt = Prompt(candidates);
            var batches = Encoding.UTF8.GetByteCount(prompt) <= 30_000
                ? new[] { (Pairs: candidates.ToArray(), Prompt: prompt) }
                : candidates.GroupBy(x => x.BidId).Select(x => (Pairs: x.ToArray(), Prompt: Prompt(x))).ToArray();
            var verified = new List<SpecMatchCell>();
            string? model = null;
            foreach (var batch in batches)
            {
                var response = await (gateway ?? AiGateway.Detached).CompleteJsonAsync(http, configuration, new AiRequest(
                    "evaluation.spec-match", """
                    Compare the requested technical requirements with the supplied offer. Match only if every
                    stated requirement is addressed. Use partial if any requirement is vague, generic
                    (such as equivalent or compatible), or partly met. Use missing for an absent or contradicted
                    specification. Copy evidence word-for-word from the supplier text. Ignore price, delivery
                    and warranty. Use plain English and return JSON only, with one cell per supplied pair.
                    """ + "\n" + AiUntrusted.Rule,
                    batch.Prompt, "spec_match", Schema, 1800, Interactive: true, Workspace: "SourcingOfficer",
                    RecordType: "Tender", RecordId: input.TenderId, LogOutput: false), ct);
                model = Limit(response.Model, 200);
                verified.AddRange(Verify(response.Content, batch.Pairs));
            }
            foreach (var cell in verified) cells[(cell.BidId, cell.ItemId)] = cell;
            return new(cells.Values.ToArray(), true, model);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return new(cells.Values.ToArray(), false, null);
        }
    }

    private static string Prompt(IEnumerable<Candidate> candidates) => JsonSerializer.Serialize(new
    {
        Offers = candidates.GroupBy(x => x.BidId).Select(offer => new
        {
            BidId = offer.Key,
            TechnicalDetails = AiUntrusted.Wrap("supplier technical details", offer.First().Offer.TechnicalDetails ?? ""),
            Items = offer.Select(x => new
            {
                x.ItemId,
                x.Request.ItemName,
                x.Request.Quantity,
                x.Request.Unit,
                x.Request.Specifications,
                OfferedSpecifications = x.Lines.Select(y => AiUntrusted.Wrap("supplier offered specifications", y.OfferedSpecifications ?? ""))
            })
        })
    }, Json);

    private static IEnumerable<SpecMatchCell> Verify(string content, Candidate[] candidates)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array)
            throw new JsonException("The spec match response must contain a cells array.");

        var validPairs = candidates.ToDictionary(x => (x.BidId, x.ItemId));
        var seen = new HashSet<(int, int)>();
        foreach (var cell in cells.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.Object || !ReadId(cell, "bidId", out var bidId) ||
                !ReadId(cell, "itemId", out var itemId) || !validPairs.TryGetValue((bidId, itemId), out var candidate) ||
                !seen.Add((bidId, itemId)))
                continue;

            var verdict = ReadText(cell, "verdict") switch
            {
                "match" => "Match",
                "partial" => "Partial",
                "missing" => "Missing",
                _ => null
            };
            var reason = ReadText(cell, "reason");
            var evidence = ReadText(cell, "evidence");
            if (verdict is null || string.IsNullOrWhiteSpace(reason))
            {
                yield return new(bidId, itemId, "NeedsCheck", "AI verdict could not be validated", null, "code");
                continue;
            }

            var quote = OfferPreCheck.Normalize(evidence ?? "");
            var offerText = candidate.Lines.Select(x => x.OfferedSpecifications).Append(candidate.Offer.TechnicalDetails);
            if (quote.Length == 0 || !offerText.Any(x => OfferPreCheck.Normalize(x ?? "").Contains(quote, StringComparison.Ordinal)))
            {
                yield return new(bidId, itemId, "NeedsCheck", "AI quote not found in the offer", null, "code");
                continue;
            }
            yield return new(bidId, itemId, verdict, Limit(reason, 160), Limit(evidence!.Trim(), 200), "ai");
        }
    }

    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];

    private static bool ReadId(JsonElement cell, string name, out int id)
    {
        id = 0;
        return cell.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out id);
    }

    private static string? ReadText(JsonElement cell, string name) =>
        cell.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
