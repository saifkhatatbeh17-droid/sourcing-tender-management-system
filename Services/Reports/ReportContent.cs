using System.Text.RegularExpressions;

namespace SourcingTenderManagement.Services.Reports;

// Interpret a small text format. All content is still encoded by Razor; AI HTML is never executed.
public static class ReportContent
{
    private static readonly HashSet<string> Headings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Committee marks", "Member reviews", "Chosen offer", "Why it was chosen", "Comparison", "Observations", "Conclusion",
        "Evaluation basis", "Recommended offer", "Financial review", "Technical review", "Alternatives",
        "Limitations and conclusion", "Decision basis", "Cost and terms", "Technical fit and delivery",
        "Tradeoffs and limitations", "Why this offer", "Cost comparison", "Technical fit", "Tradeoffs",
        "Recommendation", "Cost", "Technical fit & delivery", "Offer score comparison"
    };
    public record Block(string Kind, string[] Lines);
    public record Span(string Text, bool Emphasis);

    public static List<Block> Parse(string? text)
    {
        var blocks = new List<Block>();
        var pending = new List<string>();
        string kind = "paragraph";
        void Flush()
        {
            if (pending.Count > 0) blocks.Add(new(kind, pending.ToArray()));
            pending.Clear();
        }
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            var title = Regex.Replace(line, @"^(?:#{1,6}\s+|\d+[.)]\s+)", "").Trim('*', ' ', ':');
            if (Headings.Contains(title) || Regex.IsMatch(line, @"^#{1,6}\s+.{1,100}$"))
            { Flush(); blocks.Add(new("heading", [title])); continue; }
            var bullet = Regex.IsMatch(line, @"^[-*•]\s+");
            var nextKind = bullet ? "list" : "paragraph";
            if (nextKind != kind) Flush();
            kind = nextKind;
            pending.Add(bullet ? line[2..].TrimStart() : line);
        }
        Flush();
        return blocks;
    }

    public static IEnumerable<Span> Inline(string text)
    {
        var offset = 0;
        foreach (Match match in Regex.Matches(text, @"\*\*(.+?)\*\*"))
        {
            if (match.Index > offset) yield return new(text[offset..match.Index], false);
            yield return new(match.Groups[1].Success ? match.Groups[1].Value : match.Value, true);
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) yield return new(text[offset..], false);
    }
}
