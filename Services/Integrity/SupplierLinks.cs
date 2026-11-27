using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Packaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.Integrity;

/// <summary>One warning sign between two suppliers. TenderId is null for signs drawn from several tenders.</summary>
public sealed record LinkEvidence(string Kind, string Text, int? TenderId, int Points);

/// <summary>Two suppliers whose offers look coordinated. SupplierA always has the lower id.</summary>
public sealed record SupplierLink(int SupplierA, string NameA, int SupplierB, string NameB, string Level, int Score,
    IReadOnlyList<LinkEvidence> Evidence, IReadOnlyList<int> SharedTenders);

public sealed record SupplierNode(int Id, string Name, int Offers);

public sealed record SupplierLinkMap(IReadOnlyList<SupplierNode> Nodes, IReadOnlyList<SupplierLink> Links, int TendersChecked, int OffersChecked);

/// <summary>Facts about one submitted offer, enough to compare it with the other offers.</summary>
public sealed record OfferFacts(int BidId, int TenderId, int SupplierId, string SupplierName, DateTime? SubmittedAt, decimal Total,
    string Currency, bool IsWinner, IReadOnlyList<OfferLineFacts> Lines, string? TechnicalDetails, IReadOnlyList<OfferFileFacts> Files);

public sealed record OfferLineFacts(int ItemId, string ItemName, decimal UnitPrice, string? OfferedSpec, string? RequestedSpec);

public sealed record OfferFileFacts(string FileName, string Sha256, string? Author);

/// <summary>
/// Finds signs that two suppliers coordinated their offers (bid rigging): near-identical prices, copied
/// specification text, the same uploaded file or document author, offers sent minutes apart, and, across
/// tenders, taking turns to win or losing by a hair. Everything is computed in code from stored offers;
/// the result is a prompt to look closer, never a finding of wrongdoing.
/// </summary>
public sealed partial class SupplierLinks(ApplicationDbContext db, IWebHostEnvironment env)
{
    public const string High = "high", Medium = "medium";
    private const int MinimumScore = 2;
    private static readonly ConcurrentDictionary<string, OfferFileFacts?> FileCache = new();

    /// <summary>Every supplier with a submitted offer and every linked pair, across all tenders.</summary>
    public async Task<SupplierLinkMap> MapAsync(CancellationToken ct = default)
    {
        var offers = await LoadAsync(null, ct);
        var links = Analyze(offers);
        var nodes = offers.GroupBy(o => o.SupplierId)
            .Select(g => new SupplierNode(g.Key, g.First().SupplierName, g.Count()))
            .OrderByDescending(n => n.Offers).ThenBy(n => n.Name).ToList();
        return new SupplierLinkMap(nodes, links, offers.Select(o => o.TenderId).Distinct().Count(), offers.Count);
    }

    /// <summary>Linked pairs among the suppliers that sent offers to one tender, including their history together.</summary>
    public async Task<IReadOnlyList<SupplierLink>> ForTenderAsync(int tenderId, CancellationToken ct = default)
    {
        var offers = await LoadAsync(tenderId, ct);
        var here = offers.Where(o => o.TenderId == tenderId).Select(o => o.SupplierId).ToHashSet();
        if (here.Count < 2) return [];
        return Analyze(offers).Where(l => here.Contains(l.SupplierA) && here.Contains(l.SupplierB)).ToList();
    }

    public async Task<SupplierLink?> PairAsync(int supplierA, int supplierB, CancellationToken ct = default) =>
        (await MapAsync(ct)).Links.FirstOrDefault(l =>
            (l.SupplierA == supplierA && l.SupplierB == supplierB) || (l.SupplierA == supplierB && l.SupplierB == supplierA));

    // Documents are only compared inside one tender, so a tender page reads only that tender's files.
    private async Task<List<OfferFacts>> LoadAsync(int? filesForTender, CancellationToken ct)
    {
        var bids = await db.Bids.AsNoTracking()
            .Where(b => b.SubmittedAt != null && b.Status != "Draft" && b.Status != "Withdrawn")
            .Select(b => new
            {
                b.BidId, b.TenderId, SupplierId = b.Invitation.SupplierProfileId, b.Invitation.SupplierProfile.CompanyName,
                b.SubmittedAt, b.Currency, b.AdditionalCosts, b.TechnicalDetails,
                Winner = b.Invitation.Tender.AwardedBidId ?? (b.Invitation.Tender.EvaluationReport == null ? null : b.Invitation.Tender.EvaluationReport.RecommendedBidId),
                Lines = b.BidItems.Select(i => new { i.PurchaseRequestItemId, i.PurchaseRequestItem.ItemName, i.Quantity, i.UnitPrice, i.OfferedSpecifications, i.PurchaseRequestItem.Specifications }).ToList(),
                Files = b.BidDocuments.Select(d => new { d.OriginalFileName, d.StoredFileName }).ToList()
            })
            .ToListAsync(ct);

        return bids.Select(b => new OfferFacts(b.BidId, b.TenderId, b.SupplierId, b.CompanyName, b.SubmittedAt,
            b.Lines.Sum(l => l.Quantity * l.UnitPrice) + b.AdditionalCosts, b.Currency, b.Winner == b.BidId,
            b.Lines.Select(l => new OfferLineFacts(l.PurchaseRequestItemId, l.ItemName, l.UnitPrice, l.OfferedSpecifications, l.Specifications)).ToList(),
            b.TechnicalDetails,
            filesForTender is { } t && b.TenderId != t ? [] : b.Files.Select(f => ReadFile(f.OriginalFileName, f.StoredFileName)).OfType<OfferFileFacts>().ToList()))
            .ToList();
    }

    private OfferFileFacts? ReadFile(string originalName, string storedName)
    {
        var path = Path.Combine(env.ContentRootPath, "App_Data", "private-files", "offers", Path.GetFileName(storedName));
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 25_000_000) return null;
        var key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return FileCache.GetOrAdd(key, _ =>
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                return new OfferFileFacts(originalName, Convert.ToHexString(SHA256.HashData(bytes)), ReadAuthor(path, bytes));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        });
    }

    internal static string? ReadAuthor(string path, byte[] bytes)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext is ".docx" or ".xlsx" or ".pptx")
            {
                using var stream = new MemoryStream(bytes, false);
                using var package = Package.Open(stream, FileMode.Open, FileAccess.Read);
                return CleanAuthor(package.PackageProperties.Creator);
            }
            if (ext == ".pdf")
            {
                var text = Encoding.Latin1.GetString(bytes);
                var match = PdfAuthorRegex().Match(text);
                return match.Success ? CleanAuthor(match.Groups[1].Value) : null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FileFormatException or InvalidOperationException) { }
        return null;
    }

    // Default names that every copy of Office or a PDF tool writes say nothing about who made the file.
    private static readonly HashSet<string> GenericAuthors = new(StringComparer.OrdinalIgnoreCase)
    { "user", "admin", "administrator", "owner", "author", "unknown", "microsoft office user", "windows user", "pc", "home", "default" };

    private static string? CleanAuthor(string? author)
    {
        var value = author?.Trim();
        return string.IsNullOrEmpty(value) || value.Length < 3 || GenericAuthors.Contains(value) ? null : value;
    }

    // ---------- Analysis (pure, tested in Checks/SupplierLinkChecks.cs) ----------

    public static IReadOnlyList<SupplierLink> Analyze(IReadOnlyList<OfferFacts> offers)
    {
        var evidence = new Dictionary<(int, int), List<LinkEvidence>>();
        var shared = new Dictionary<(int, int), SortedSet<int>>();
        void Add((int, int) key, LinkEvidence item)
        {
            if (!evidence.TryGetValue(key, out var list)) evidence[key] = list = [];
            list.Add(item);
        }

        foreach (var tender in offers.GroupBy(o => o.TenderId))
        {
            // One offer per supplier per tender: the latest submission counts.
            var list = tender.GroupBy(o => o.SupplierId).Select(g => g.OrderByDescending(o => o.SubmittedAt).First()).ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var (a, b) = list[i].SupplierId < list[j].SupplierId ? (list[i], list[j]) : (list[j], list[i]);
                    var key = (a.SupplierId, b.SupplierId);
                    if (!shared.TryGetValue(key, out var set)) shared[key] = set = [];
                    set.Add(tender.Key);
                    foreach (var item in CompareOffers(a, b)) Add(key, item);
                }
        }

        foreach (var (key, tenders) in shared)
            foreach (var item in CompareHistory(offers, key.Item1, key.Item2, tenders)) Add(key, item);

        var names = offers.GroupBy(o => o.SupplierId).ToDictionary(g => g.Key, g => g.First().SupplierName);
        return evidence
            .Select(e => (e.Key, Items: MergeTiming(e.Value)))
            // Timing only supports other signs: a pair needs real signs worth MinimumScore on their own.
            .Where(e => e.Items.Where(x => x.Kind != "timing").Sum(x => x.Points) >= MinimumScore)
            .Select(e => (e.Key, e.Items, Score: e.Items.Sum(x => x.Points)))
            .Select(e => new SupplierLink(e.Key.Item1, names[e.Key.Item1], e.Key.Item2, names[e.Key.Item2],
                e.Score >= 4 ? High : Medium, e.Score, e.Items.OrderByDescending(x => x.Points).ToList(), shared[e.Key].ToList()))
            .OrderByDescending(l => l.Score).ThenBy(l => l.NameA)
            .ToList();
    }

    // ---------- Plain wording shared by the map and the banner ----------

    public static string LevelLabel(string level) => level == High ? "High risk" : "Worth checking";

    public static string KindLabel(string kind) => kind switch
    {
        "prices" => "Same prices",
        "text" => "Same description",
        "file" => "Same file",
        "author" => "Same author",
        "timing" => "Same time",
        "turns" => "Take turns",
        "cover" => "Lose by a little",
        _ => "Reason"
    };

    /// <summary>The reasons as one short phrase, e.g. "almost the same prices, the same description".</summary>
    public static string Reasons(SupplierLink link) => string.Join(", ", link.Evidence.Select(e => e.Kind).Distinct().Select(kind => kind switch
    {
        "prices" => "almost the same prices",
        "text" => "the same written description",
        "file" => "the same uploaded file",
        "author" => "documents made by the same person",
        "timing" => "sent at the same time",
        "turns" => "they take turns winning",
        "cover" => "the loser is always just above the winner",
        _ => "other signs"
    }));

    // Close timing in several tenders is one sign, not several.
    private static List<LinkEvidence> MergeTiming(List<LinkEvidence> items)
    {
        var timing = items.Where(x => x.Kind == "timing").ToList();
        if (timing.Count <= 1) return items;
        return items.Where(x => x.Kind != "timing")
            .Append(new LinkEvidence("timing", $"They sent their offers within minutes of each other, {timing.Count} times.", null, 1))
            .ToList();
    }

    internal static IEnumerable<LinkEvidence> CompareOffers(OfferFacts a, OfferFacts b)
    {
        var t = a.TenderId;

        // Unit prices on the items both quoted.
        var pairs = a.Lines.Join(b.Lines, x => x.ItemId, y => y.ItemId, (x, y) => (x, y))
            .Where(p => p.x.UnitPrice > 0 && p.y.UnitPrice > 0).ToList();
        var close = pairs.Count(p => Math.Abs(p.x.UnitPrice - p.y.UnitPrice) / Math.Max(p.x.UnitPrice, p.y.UnitPrice) <= 0.01m);
        var pricesMatched = pairs.Count >= 2 && close >= 2 && close * 10 >= pairs.Count * 6;
        if (pricesMatched)
            yield return new("prices", $"Their prices are almost the same: {close} of {pairs.Count} items differ by less than 1%.", t, 3);
        else if (a.Total > 0 && b.Total > 0 && Math.Abs(a.Total - b.Total) / Math.Max(a.Total, b.Total) <= 0.005m)
            yield return new("prices", $"Their total prices are almost the same: {Money(a.Total)} and {Money(b.Total)} {a.Currency}.", t, 2);

        // Text each supplier wrote themselves (not the requested specification the form starts with).
        var copied = a.Lines.Join(b.Lines, x => x.ItemId, y => y.ItemId, (x, y) => (x, y))
            .Where(p => IsOwnText(p.x.OfferedSpec, p.x.RequestedSpec) && IsOwnText(p.y.OfferedSpec, p.y.RequestedSpec) && Similar(p.x.OfferedSpec!, p.y.OfferedSpec!))
            .Select(p => p.x.ItemName).Distinct().ToList();
        var notesCopied = Words(a.TechnicalDetails).Count >= 8 && Similar(a.TechnicalDetails!, b.TechnicalDetails ?? "");
        if (copied.Count > 0 || notesCopied)
        {
            var where = copied.Count switch
            {
                0 => "technical notes",
                1 => $"description for “{copied[0]}”",
                _ => $"descriptions for {copied.Count} items"
            };
            if (copied.Count > 0 && notesCopied) where += " and technical notes";
            yield return new("text", $"They wrote the same {where}, almost word for word.", t, 3);
        }

        // The same file, or files made by the same named person.
        var sameFile = a.Files.Join(b.Files, x => x.Sha256, y => y.Sha256, (x, y) => (x, y)).FirstOrDefault();
        if (sameFile != default)
            yield return new("file", $"They uploaded the exact same file (“{sameFile.x.FileName}” and “{sameFile.y.FileName}”).", t, 3);
        else
        {
            var author = a.Files.Select(f => f.Author).OfType<string>()
                .Intersect(b.Files.Select(f => f.Author).OfType<string>(), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (author != null)
                yield return new("author", $"Their documents were made by the same person: “{author}”.", t, 2);
        }

        // Sent within minutes of each other. Weak alone, so it only adds to other signs.
        if (a.SubmittedAt is { } sa && b.SubmittedAt is { } sb && Math.Abs((sa - sb).TotalMinutes) <= 10)
        {
            var minutes = (int)Math.Round(Math.Abs((sa - sb).TotalMinutes));
            yield return new("timing", minutes == 0 ? "They sent their offers in the same minute." : $"They sent their offers {minutes} minute{(minutes == 1 ? "" : "s")} apart.", t, 1);
        }
    }

    internal static IEnumerable<LinkEvidence> CompareHistory(IReadOnlyList<OfferFacts> offers, int supplierA, int supplierB, IEnumerable<int> tenders)
    {
        var decided = new List<(int Tender, int Winner, decimal Gap)>();
        foreach (var tender in tenders)
        {
            var a = offers.Where(o => o.TenderId == tender && o.SupplierId == supplierA).OrderByDescending(o => o.SubmittedAt).FirstOrDefault();
            var b = offers.Where(o => o.TenderId == tender && o.SupplierId == supplierB).OrderByDescending(o => o.SubmittedAt).FirstOrDefault();
            if (a == null || b == null || a.IsWinner == b.IsWinner || a.Total <= 0 || b.Total <= 0) continue;
            var (win, lose) = a.IsWinner ? (a, b) : (b, a);
            decided.Add((tender, win.SupplierId, (lose.Total - win.Total) / win.Total));
        }
        if (decided.Count < 2) yield break;

        var nameA = offers.First(o => o.SupplierId == supplierA).SupplierName;
        var nameB = offers.First(o => o.SupplierId == supplierB).SupplierName;
        var winsA = decided.Where(d => d.Winner == supplierA).Select(d => d.Tender).ToList();
        var winsB = decided.Where(d => d.Winner == supplierB).Select(d => d.Tender).ToList();
        if (winsA.Count > 0 && winsB.Count > 0)
            yield return new("turns", $"They take turns winning: {nameA} won {Tenders(winsA)}, and {nameB} won {Tenders(winsB)}.", null, 2);

        var narrow = decided.Where(d => d.Gap is >= 0 and <= 0.03m).ToList();
        if (narrow.Count >= 2)
            yield return new("cover", $"When one of them wins, the other loses by a tiny amount (under 3%). This happened {narrow.Count} of {decided.Count} times.", null, 2);
    }

    internal static bool IsOwnText(string? offered, string? requested) =>
        Words(offered).Count >= 4 && Normal(offered) != Normal(requested);

    // Word-triplet overlap for longer text; short text must match exactly after normalising.
    internal static bool Similar(string x, string y)
    {
        var wx = Words(x); var wy = Words(y);
        if (wx.Count == 0 || wy.Count == 0) return false;
        if (wx.Count < 6 || wy.Count < 6) return Normal(x) == Normal(y);
        var sx = Shingles(wx); var sy = Shingles(wy);
        var overlap = sx.Intersect(sy).Count();
        return overlap / (double)sx.Union(sy).Count() >= 0.8;
    }

    private static HashSet<string> Shingles(List<string> words) =>
        Enumerable.Range(0, words.Count - 2).Select(i => $"{words[i]} {words[i + 1]} {words[i + 2]}").ToHashSet();

    private static List<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : WordRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();

    private static string Normal(string? text) => string.Join(' ', Words(text));
    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    private static string Tenders(List<int> ids) => string.Join(", ", ids.Select(id => "#" + id));

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"/Author\s*\(([^)]{1,120})\)")]
    private static partial Regex PdfAuthorRegex();
}
