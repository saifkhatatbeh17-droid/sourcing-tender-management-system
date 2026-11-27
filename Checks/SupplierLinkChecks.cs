using SourcingTenderManagement.Services.Integrity;

namespace SourcingTenderManagement.Checks;

// Pure checks of the supplier-link analysis. No database, no network. Run with --supplier-link-checks.
public static class SupplierLinkChecks
{
    public static void Run()
    {
        var checks = 0;
        void Require(bool ok, string label) { if (!ok) throw new Exception("Supplier link check failed: " + label); checks++; }
        var t0 = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        const string requested = "Stainless steel housing, 220V, CE marked";

        OfferFacts Offer(int bid, int tender, int supplier, decimal[] prices, DateTime at, bool winner = false,
            string? spec = null, string? notes = null, OfferFileFacts[]? files = null) =>
            new(bid, tender, supplier, "S" + supplier, at, prices.Sum() * 10, "JOD", winner,
                prices.Select((p, i) => new OfferLineFacts(i + 1, "Item " + (i + 1), p, spec ?? requested, requested))
                    .ToList(),
                notes, files ?? []);

        // 1. Independent offers: nothing to report.
        var clean = SupplierLinks.Analyze([
            Offer(1, 10, 1, [100, 200, 300], t0),
            Offer(2, 10, 2, [120, 170, 340], t0.AddDays(1)),
            Offer(3, 10, 3, [90, 230, 300], t0.AddDays(2))
        ]);
        Require(clean.Count == 0, "independent offers are not linked");

        // 2. Near-identical prices plus a close submission time: one strong pair, the third supplier stays clear.
        var priced = SupplierLinks.Analyze([
            Offer(1, 10, 1, [100, 200, 300], t0),
            Offer(2, 10, 2, [100.5m, 200, 301], t0.AddMinutes(4)),
            Offer(3, 10, 3, [90, 230, 300], t0.AddDays(2))
        ]);
        Require(priced.Count == 1 && priced[0].SupplierA == 1 && priced[0].SupplierB == 2, "price-matched pair found");
        Require(priced[0].Level == SupplierLinks.High, "prices plus timing is strong");
        Require(priced[0].Evidence.Any(e => e.Kind == "prices"
            && e.Text.Contains("3 of 3 items")), "price evidence counts items");
        Require(priced[0].Evidence.Any(e => e.Kind == "timing" && e.Text.Contains("4 minutes")), "timing evidence");

        // 3. Timing alone is too weak to show.
        var timing = SupplierLinks.Analyze([
            Offer(1, 10, 1, [100, 200, 300], t0),
            Offer(2, 10, 2, [130, 170, 340], t0.AddMinutes(2))
        ]);
        Require(timing.Count == 0, "timing alone is not shown");

        // 4. Copied own text counts; the requested specification the form starts with does not.
        const string own = "Our unit uses a brushless motor with a stainles drum and a two year onsite warranty";
        var copied = SupplierLinks.Analyze([
            Offer(1, 10, 1, [100, 200], t0, spec: own),
            Offer(2, 10, 2, [140, 150], t0.AddDays(1), spec: own + "."),
            Offer(3, 10, 3, [150, 160], t0.AddDays(1)),
            Offer(4, 10, 4, [160, 120], t0.AddDays(2))
        ]);
        Require(copied.Count == 1 && copied[0].SupplierA == 1 && copied[0].SupplierB == 2, "copied text pair only");
        Require(copied[0].Evidence.Any(e => e.Kind == "text"), "copied text evidence");

        // 5. Same uploaded file.
        var file = new OfferFileFacts("catalogue.pdf", "ABC", null);
        var same = SupplierLinks.Analyze([
            Offer(1, 10, 1, [100], t0, files: [file]),
            Offer(2, 10, 2, [150], t0.AddDays(1), files: [file with { FileName = "brochure.pdf" }])
        ]);
        Require(same.Count == 1 && same[0].Evidence.Single().Kind == "file", "identical file is a sign");

        // 6. History: taking turns to win and losing by a hair across tenders.
        var history = SupplierLinks.Analyze([
            Offer(1, 11, 1, [100], t0, winner: true), Offer(2, 11, 2, [101], t0.AddDays(1)),
            Offer(3, 12, 1, [202], t0, winner: false), Offer(4, 12, 2, [200], t0.AddDays(1), winner: true),
            Offer(5, 13, 1, [300], t0, winner: true), Offer(6, 13, 2, [305], t0.AddDays(1))
        ]);
        Require(history.Count == 1, "history pair found");
        Require(history[0].Evidence.Any(e => e.Kind == "turns" && e.TenderId == null), "taking turns evidence");
        Require(history[0].Evidence.Any(e => e.Kind == "cover"
            && e.Text.Contains("3 of 3 times")), "narrow losses evidence");
        Require(history[0].SharedTenders.Count == 3, "shared tenders listed");

        // 7. AI brief guard: a sentence with a number not in the evidence is dropped.
        var kept = SupplierLinkExplainer.Keep("They matched 3 of 3 prices. They also share 7 phone numbers.", "3 of 3 unit prices are within 1% of each other.");
        Require(kept == "They matched 3 of 3 prices.", "invented numbers removed");

        Console.WriteLine($"Supplier link checks passed: {checks}");
    }
}
