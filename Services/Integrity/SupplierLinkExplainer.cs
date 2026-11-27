using System.Text.Json;
using System.Text.RegularExpressions;
using SourcingTenderManagement.Services.Ai;

namespace SourcingTenderManagement.Services.Integrity;

// Asks the AI to explain, in plain words, why two suppliers were flagged as possibly working together.
public sealed record SupplierLinkExplanation(string Summary, IReadOnlyList<string> Checks);

/// <summary>
/// Turns the evidence for one supplier pair into a short plain-language brief. The AI only rewords the
/// evidence computed in <see cref="SupplierLinks"/>; any sentence quoting a number that is not in that
/// evidence is dropped, so the brief cannot invent figures.
/// </summary>
public sealed partial class SupplierLinkExplainer(HttpClient http, IConfiguration configuration, AiGateway? gateway = null)
{
    private const string SystemPrompt =
        "You help a procurement administrator review warning signs that two suppliers may have coordinated their offers. " +
        "Use only the evidence given. Never say they colluded or committed fraud: these are warning signs to check. " +
        "Write 'summary' as at most two short sentences saying what stands out and why it matters. " +
        "Write 'checks' as up to three short, practical things the administrator can verify next (for example, ownership, addresses, phone numbers). " +
        "Plain English, no headings, no markdown. Return JSON only.";

    private static readonly object Schema = new
    {
        type = "object",
        additionalProperties = false,
        properties = new Dictionary<string, object>
        {
            ["summary"] = new { type = "string" },
            ["checks"] = new { type = "array", items = new { type = "string" }, maxItems = 3 }
        },
        required = new[] { "summary", "checks" }
    };

    public async Task<SupplierLinkExplanation> ExplainAsync(SupplierLink link, CancellationToken ct, string workspace = Data.RoleNames.Admin)
    {
        var payload = new
        {
            supplierA = link.NameA,
            supplierB = link.NameB,
            level = link.Level,
            sharedTenders = link.SharedTenders.Count,
            evidence = link.Evidence.Select(e => new { e.Kind, e.Text, tender = e.TenderId })
        };
        var completion = await (gateway ?? AiGateway.Detached).CompleteJsonAsync(http, configuration, new AiRequest(
            "integrity.supplier-links",
            SystemPrompt,
            JsonSerializer.Serialize(payload),
            "supplier_link_brief",
            Schema,
            500,
            Interactive: true,
            Workspace: workspace,
            RecordType: "SupplierPair",
            RecordId: link.SupplierA), ct);

        using var doc = JsonDocument.Parse(completion.Content);
        var root = doc.RootElement;
        var allowed = string.Join(' ', link.Evidence.Select(e => e.Text).Append(link.NameA).Append(link.NameB)
            .Append(link.SharedTenders.Count.ToString()));
        var summary = Keep(root.TryGetProperty("summary", out var s)
            && s.ValueKind == JsonValueKind.String ? s
            .GetString() : null, allowed);
        var checks = root.TryGetProperty("checks", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => Keep(x.GetString(), allowed))
                .Where(x => x.Length > 0).Take(3).ToList()
            : [];
        if (summary.Length == 0) throw new InvalidOperationException("The AI brief did not match the evidence.");
        return new SupplierLinkExplanation(summary, checks);
    }

    // Drops each sentence that quotes a number the evidence does not contain.
    internal static string Keep(string? text, string evidence)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var known = NumberRegex().Matches(evidence).Select(m => Plain(m.Value)).ToHashSet();
        var kept = SentenceRegex().Split(text.Trim())
            .Where(sentence => NumberRegex().Matches(sentence).All(m => known.Contains(Plain(m.Value))));
        return string.Join(' ', kept).Trim();
    }

    private static string Plain(string number) => number.Replace(",", "").TrimEnd('.');

    [GeneratedRegex(@"\d[\d,]*(\.\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceRegex();
}
