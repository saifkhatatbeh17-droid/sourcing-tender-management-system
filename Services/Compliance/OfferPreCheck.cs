using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Intelligence;

namespace SourcingTenderManagement.Services.Compliance;

public static class PreCheckSeverity
{
    public const string Issue = "issue";
    public const string Warning = "warning";
    public const string Info = "info";
    public static readonly string[] All = [Issue, Warning, Info];
}

public sealed record PreCheckFlag(string Severity, string Category, string Message, int? ItemId = null, string? Evidence = null, string? Source = null);

public sealed record OfferDocumentText(string FileName, string Text, string? Note);

public sealed record OfferAiReviewResult(
    string Summary,
    IReadOnlyList<PreCheckFlag> Findings,
    int HiddenUnverifiable,
    IReadOnlyList<string> DocumentNotes);

/// <summary>
/// Pre-check of a supplier offer for the Sourcing Officer's compliance review. Two parts:
/// checks made in code (always shown, no AI) and an optional AI document review whose findings
/// must quote evidence that really appears in the offer. Neither part changes the compliance decision.
/// </summary>
public sealed partial class OfferPreCheck(HttpClient http, IConfiguration configuration, IWebHostEnvironment environment, AiGateway? gateway = null)
{
    private const int PerDocumentChars = 4000;
    private const int TotalDocumentChars = 12000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Checks made in code ----------

    public static IReadOnlyList<PreCheckFlag> CodeChecks(Bid bid, Tender tender, IReadOnlyList<PurchaseRequestItem> requested,
        int mandatoryDocuments, Func<BidDocument, bool> fileExists, DateTime utcNow)
    {
        var flags = new List<PreCheckFlag>();
        foreach (var item in requested)
        {
            var lines = bid.BidItems.Where(i => i.PurchaseRequestItemId == item.PurchaseRequestItemId).ToList();
            if (lines.Count == 0)
            {
                flags.Add(new(PreCheckSeverity.Issue, "quantity", $"“{item.ItemName}” is not quoted in this offer.", item.PurchaseRequestItemId));
                continue;
            }
            var quantity = lines.Sum(l => l.Quantity);
            if (quantity < item.Quantity)
                flags.Add(new(PreCheckSeverity.Warning, "quantity",
                    $"“{item.ItemName}”: offers {Num(quantity)} {item.Unit}, but {Num(item.Quantity)} {item.Unit} were requested.", item.PurchaseRequestItemId));
            else if (quantity > item.Quantity)
                flags.Add(new(PreCheckSeverity.Info, "quantity",
                    $"“{item.ItemName}”: offers {Num(quantity)} {item.Unit}, more than the {Num(item.Quantity)} {item.Unit} requested.", item.PurchaseRequestItemId));
            if (lines.Any(l => l.UnitPrice <= 0))
                flags.Add(new(PreCheckSeverity.Issue, "price", $"“{item.ItemName}” has a zero unit price.", item.PurchaseRequestItemId));
        }

        if (string.IsNullOrWhiteSpace(bid.PaymentTerms))
            flags.Add(new(PreCheckSeverity.Warning, "terms", "Payment terms are not stated."));
        if (bid.DeliveryDays <= 0)
            flags.Add(new(PreCheckSeverity.Warning, "terms", "Delivery time is not stated."));

        var today = DateOnly.FromDateTime(utcNow);
        if (bid.ValidUntil is not { } validUntil)
            flags.Add(new(PreCheckSeverity.Warning, "validity", "The offer has no validity date."));
        else if (validUntil < today)
            flags.Add(new(PreCheckSeverity.Issue, "validity", $"The offer expired on {validUntil:dd MMM yyyy}."));
        else if (validUntil < DateOnly.FromDateTime(tender.ClosesAt))
            flags.Add(new(PreCheckSeverity.Warning, "validity", $"The offer is valid only until {validUntil:dd MMM yyyy}, before the tender closes on {tender.ClosesAt:dd MMM yyyy}."));

        var subtotal = bid.BidItems.Sum(i => i.Quantity * i.UnitPrice);
        if (subtotal > 0 && bid.TaxAmount != OfferTax.Amount(subtotal))
        {
            var rate = Math.Round(bid.TaxAmount / subtotal * 100m, 2);
            flags.Add((bid.SubmittedAt ?? bid.CreatedAt) >= OfferTax.FixedRateSinceUtc
                ? new(PreCheckSeverity.Warning, "tax", $"Tax is {bid.TaxAmount:N2} ({rate:0.##}% of the subtotal), not the fixed 16%.")
                : new(PreCheckSeverity.Info, "tax", $"Recorded with {rate:0.##}% tax before the fixed 16% rule; this is expected for older offers."));
        }

        var supportingCount = bid.BidDocuments.Count(d => !d.DocumentType.StartsWith(SourcingTenderManagement.Services.OfferProductPhotos.Prefix, StringComparison.Ordinal));
        if (mandatoryDocuments > 0 && supportingCount < mandatoryDocuments)
            flags.Add(new(PreCheckSeverity.Issue, "document",
                $"{supportingCount} {Plural(supportingCount, "document")} uploaded; the tender requires {mandatoryDocuments}."));
        else if (supportingCount == 0)
            flags.Add(new(PreCheckSeverity.Warning, "document", "No supporting documents were uploaded."));
        foreach (var document in bid.BidDocuments.Where(d => !fileExists(d)))
            flags.Add(new(PreCheckSeverity.Issue, "document", $"“{document.OriginalFileName}” is listed but the file is missing. Ask the supplier to upload it again."));

        return flags;
    }

    public string DocumentPath(BidDocument document) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "private-files", "offers", Path.GetFileName(document.StoredFileName));

    // ---------- Reading documents ----------

    public IReadOnlyList<OfferDocumentText> ReadDocuments(IEnumerable<BidDocument> documents)
    {
        var results = new List<OfferDocumentText>();
        var budget = TotalDocumentChars;
        foreach (var document in documents)
        {
            var path = DocumentPath(document);
            if (!File.Exists(path)) { results.Add(new(document.OriginalFileName, "", "File missing; not reviewed.")); continue; }
            if (budget <= 0) { results.Add(new(document.OriginalFileName, "", "Skipped: the review already reached its text limit.")); continue; }

            string text;
            string? note = null;
            try
            {
                text = ExtractText(path, Path.GetExtension(path).ToLowerInvariant(), out note);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                results.Add(new(document.OriginalFileName, "", "Could not be read; check it manually."));
                continue;
            }

            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length == 0) { results.Add(new(document.OriginalFileName, "", note ?? "No readable text (it may be a scan); check it manually.")); continue; }
            var limit = Math.Min(PerDocumentChars, budget);
            if (text.Length > limit) { text = text[..limit]; note = "Only the first part was reviewed."; }
            budget -= text.Length;
            results.Add(new(document.OriginalFileName, text, note));
        }
        return results;
    }

    internal static string ExtractText(string path, string extension, out string? note)
    {
        note = null;
        switch (extension)
        {
            case ".pdf":
            {
                using var stream = File.OpenRead(path);
                var text = AiIntelligenceService.ExtractPdfContent(stream, Path.GetFileName(path)).ExtractedText;
                // The extractor returns a placeholder sentence when a PDF has no text layer.
                if (text.Contains("No embedded selectable text stream was found", StringComparison.Ordinal)) return "";
                text = PageMarkerRegex().Replace(text, " ");
                // Fonts with custom encodings come back as glyph codes, not words; never send that to the model.
                if (!LooksReadable(text))
                {
                    note = "Its text could not be read reliably (unusual font encoding); check it manually.";
                    return "";
                }
                return text;
            }
            case ".docx":
            {
                using var word = WordprocessingDocument.Open(path, false);
                var body = word.MainDocumentPart?.Document?.Body;
                // Join paragraphs with spaces so quotes that cross a paragraph break can still be verified.
                return body == null ? "" : string.Join(" ",
                    body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText).Where(t => t.Length > 0));
            }
            case ".xlsx":
            {
                using var stream = File.OpenRead(path);
                return AiIntelligenceService.ExtractExcelContent(stream, Path.GetFileName(path)).ExtractedText;
            }
            case ".jpg" or ".jpeg" or ".png":
                note = "Image files are not read by the review yet; check them manually.";
                return "";
            default:
                note = "This file type is not read by the review; check it manually.";
                return "";
        }
    }

    // ---------- AI document review ----------

    private const string SystemPrompt = """
        You assist a Sourcing Officer who is checking whether a supplier offer meets the tender requirements.
        Compare each requested item with the supplier's offered specifications, notes and documents.
        Report only problems or points the officer should check: specifications that do not match or are missing,
        units that differ from the request, required certificates or registrations that are absent, and any
        expiry or validity dates stated in the documents.
        Rules:
        - Every finding must include `evidence`: an exact quote (5 to 200 characters) copied from the supplied
          offer text or documents. Do not paraphrase the quote. If you cannot quote evidence, do not report the finding.
        - `source` names where the quote is from: "offered specifications", "supplier notes", "payment terms", "requested specifications" or a document file name.
        - When something required is missing from the offer (for example a certificate or a specification), quote the requirement
          it is missing against from the requested specifications and set `source` to "requested specifications".
        - Put a date you find in `expiryDate` as yyyy-MM-dd only when the text states when something expires or is valid until.
          Do not decide whether it has expired; the application does that.
        - `itemId` is the requested item's id, or null when the finding is about the whole offer.
        - You advise only. Never say the offer is compliant or non-compliant, and never recommend awarding.
        - Write in English, concise and specific.
        """;

    private static readonly object Schema = new
    {
        type = "object",
        additionalProperties = false,
        properties = new Dictionary<string, object>
        {
            ["summary"] = new { type = "string" },
            ["findings"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new Dictionary<string, object>
                    {
                        ["itemId"] = new { type = new[] { "integer", "null" } },
                        ["category"] = new { type = "string", @enum = new[] { "specification", "quantity", "unit", "certificate", "other" } },
                        ["severity"] = new { type = "string", @enum = PreCheckSeverity.All },
                        ["finding"] = new { type = "string" },
                        ["evidence"] = new { type = "string" },
                        ["source"] = new { type = "string" },
                        ["expiryDate"] = new { type = new[] { "string", "null" } }
                    },
                    required = new[] { "itemId", "category", "severity", "finding", "evidence", "source", "expiryDate" }
                }
            }
        },
        required = new[] { "summary", "findings" }
    };

    public async Task<OfferAiReviewResult> ReviewAsync(Bid bid, Tender tender, IReadOnlyList<PurchaseRequestItem> requested,
        DateTime utcNow, CancellationToken ct)
    {
        var documents = ReadDocuments(bid.BidDocuments);
        // Everything the supplier wrote is fenced as untrusted data; the model reads it but must not follow it.
        var payload = new
        {
            tender = new { tender.TenderId, tender.Title },
            requestedItems = requested.Select(i => new { itemId = i.PurchaseRequestItemId, i.ItemName, quantity = i.Quantity, i.Unit, i.Specifications }),
            offer = new
            {
                bid.BidId,
                bid.DeliveryDays,
                validUntil = bid.ValidUntil?.ToString("yyyy-MM-dd"),
                paymentTerms = AiUntrusted.Wrap("payment terms", bid.PaymentTerms),
                supplierNotes = AiUntrusted.Wrap("supplier notes", bid.TechnicalDetails),
                items = bid.BidItems.Select(i => new
                {
                    itemId = i.PurchaseRequestItemId,
                    quantity = i.Quantity,
                    offeredSpecifications = AiUntrusted.Wrap("offered specifications", i.OfferedSpecifications)
                })
            },
            documents = documents.Where(d => d.Text.Length > 0).Select(d => new { fileName = d.FileName, text = AiUntrusted.Wrap(d.FileName, d.Text) })
        };

        var ai = gateway ?? AiGateway.Detached;
        var completion = await ai.CompleteJsonAsync(http, configuration, new AiRequest(
            "precheck.offer-documents",
            SystemPrompt + "\n- " + AiUntrusted.Rule,
            JsonSerializer.Serialize(payload, Json),
            "offer_precheck",
            Schema,
            1200,
            Interactive: true,
            Workspace: Data.RoleNames.SourcingOfficer,
            RecordType: "Bid",
            RecordId: bid.BidId), ct);

        // Evidence must be found in exactly what the model was given.
        // Requested specifications are included so a "missing requirement" finding can quote what is required.
        var corpus = new[] { bid.PaymentTerms, bid.TechnicalDetails }
            .Concat(bid.BidItems.Select(i => i.OfferedSpecifications))
            .Concat(requested.Select(i => i.ItemName))
            .Concat(requested.Select(i => i.Specifications))
            .Concat(documents.Select(d => d.Text));
        var (findings, hidden, summary) = Verify(completion.Content, corpus, requested.Select(i => i.PurchaseRequestItemId).ToHashSet(),
            bid.ValidUntil, utcNow);
        return new OfferAiReviewResult(summary, findings, hidden,
            documents.Where(d => d.Note != null).Select(d => $"{d.FileName}: {d.Note}").ToArray());
    }

    /// <summary>
    /// Keeps only findings whose evidence is a real quote from the offer, whose item exists and whose
    /// values are allowed. Expiry is judged here from the extracted date, never by the model.
    /// </summary>
    internal static (IReadOnlyList<PreCheckFlag> Findings, int Hidden, string Summary) Verify(
        string json, IEnumerable<string?> corpus, IReadOnlySet<int> itemIds, DateOnly? offerValidUntil, DateTime utcNow)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var summary = root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()!.Trim() : "";
        var haystack = Normalize(string.Join(" \n ", corpus.Where(t => !string.IsNullOrWhiteSpace(t))));
        var today = DateOnly.FromDateTime(utcNow);
        var findings = new List<PreCheckFlag>();
        var hidden = 0;

        if (root.TryGetProperty("findings", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray().Take(25))
            {
                string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
                var evidence = Text("evidence");
                var finding = Text("finding");
                var severity = Text("severity").ToLowerInvariant();
                var category = Text("category").ToLowerInvariant();
                int? itemId = item.TryGetProperty("itemId", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var parsed) ? parsed : null;

                var quoteFound = evidence.Length >= 5 && haystack.Contains(Normalize(evidence), StringComparison.Ordinal);
                if (!quoteFound || finding.Length == 0 || !PreCheckSeverity.All.Contains(severity) || (itemId.HasValue && !itemIds.Contains(itemId.Value)))
                {
                    hidden++;
                    continue;
                }

                // Dates: the application decides what is expired.
                var expiry = Text("expiryDate");
                if (DateOnly.TryParseExact(expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    if (date < today)
                        (severity, finding) = (PreCheckSeverity.Issue, $"{finding} (expired on {date:dd MMM yyyy})");
                    else if (offerValidUntil is { } valid && date < valid)
                        (severity, finding) = (PreCheckSeverity.Warning, $"{finding} (expires {date:dd MMM yyyy}, before the offer's validity date {valid:dd MMM yyyy})");
                    else if (date <= today.AddDays(30))
                        (severity, finding) = (PreCheckSeverity.Warning, $"{finding} (expires soon, on {date:dd MMM yyyy})");
                }

                if (category is not ("specification" or "quantity" or "unit" or "certificate")) category = "other";
                findings.Add(new PreCheckFlag(severity, category, Trim(finding, 400), itemId,
                    Trim(evidence, 200), Trim(Text("source"), 120)));
            }
        }
        return (findings, hidden, Trim(summary, 600));
    }

    /// <summary>Readable text is mostly letters, digits and spaces; extracted glyph codes are not.</summary>
    internal static bool LooksReadable(string text)
    {
        var sample = text.Length > 2000 ? text[..2000] : text;
        var visible = sample.Count(c => !char.IsWhiteSpace(c));
        if (visible < 20) return visible > 0;
        var wordLike = sample.Count(char.IsLetterOrDigit);
        return wordLike >= visible * 0.6;
    }

    [GeneratedRegex(@"--- Page \d+ ---")]
    private static partial Regex PageMarkerRegex();

    internal static string Normalize(string text) => WhitespaceRegex().Replace(text, " ").Trim().ToLowerInvariant();
    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";
    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}