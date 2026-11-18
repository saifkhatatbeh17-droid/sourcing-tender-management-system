using System.Text.RegularExpressions;
using SourcingTenderManagement.Models;

namespace SourcingTenderManagement.Services.Evaluation;

// Validate comparative prose using saved quotes, including comparisons without explicit numbers.
public static class BidNarrativeValidation
{
    public static void Check(CommitteeEvaluationPage page, int subjectId, string text, bool directComparison = false)
    {
        var offers = page.Offers;
        var subject = offers.Single(b => b.BidId == subjectId);
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?;])\s+|\n+"))
        {
            var mentions = offers.SelectMany(b => Regex.Matches(sentence,
                Regex.Escape(b.Invitation.SupplierProfile.CompanyName) + @"|\b(?:offer|bid)(?:\s+ID)?\s*#?" + b.BidId + @"\b",
                RegexOptions.IgnoreCase).Select(m => (Index: m.Index, Bid: b))).OrderBy(m => m.Index).ToList();
            foreach (Match claim in Regex.Matches(sentence,
                @"\bfaster\b|\bquicker\b|\bslower\b|\bfastest\b|\bslowest\b|\bcheaper\b|\bmore expensive\b|\bless expensive\b|\b(?:higher|lower|lowest|highest) (?:total )?(?:cost|price)\b", RegexOptions.IgnoreCase))
            {
                var prefix = sentence[..claim.Index];
                var postClaim = sentence[(claim.Index + claim.Length)..];
                var before = mentions.LastOrDefault(m => m.Index < claim.Index);
                var after = mentions.FirstOrDefault(m => m.Index > claim.Index);
                var otherOffersSubject =
                    (before.Bid == null && Regex.IsMatch(prefix,
                        @"\b(?:other|remaining|competing|alternative)\s+(?:bids?|offers?|suppliers?|alternatives?|options?)\b|\b(?:competitors?|alternatives)\b",
                        RegexOptions.IgnoreCase)) ||
                    Regex.IsMatch(postClaim,
                        @"^\s+(?:of|from|among|with)\s+(?:the\s+)?(?:other|remaining|competing|alternative)\s+(?:bids?|offers?|suppliers?|alternatives?|options?)\b",
                        RegexOptions.IgnoreCase);
                if (otherOffersSubject)
                {
                    var anchor = after.Bid ?? subject;
                    var wordOther = claim.Value.ToLowerInvariant();
                    var isDeliveryOther = wordOther is "faster" or "quicker" or "slower" or "fastest" or "slowest";
                    var anchorVal = isDeliveryOther ? anchor.DeliveryDays : CommitteeEvaluationPage.Total(anchor);
                    var otherPeers = offers.Where(b => b.BidId != anchor.BidId &&
                        (isDeliveryOther || string.Equals(b.Currency, anchor.Currency, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (otherPeers.Count > 0)
                    {
                        var ok = wordOther switch
                        {
                            "faster" or "quicker" or "cheaper" or "less expensive" or "lower cost" or "lower price" or "lower total cost" =>
                                otherPeers.Any(b => (isDeliveryOther ? b.DeliveryDays : CommitteeEvaluationPage.Total(b)) < anchorVal),
                            "slower" or "more expensive" or "higher cost" or "higher price" or "higher total cost" =>
                                otherPeers.Any(b => (isDeliveryOther ? b.DeliveryDays : CommitteeEvaluationPage.Total(b)) > anchorVal),
                            _ => true
                        };
                        if (!ok)
                            throw new InvalidOperationException($"The claim '{claim.Value}' for competing offers against offer #{anchor.BidId} contradicts the saved {(isDeliveryOther ? "delivery days" : "complete quote totals")}. Follow comparisonStatements; do not reverse the comparison.");
                    }
                    continue;
                }

                var beforeIsBaseline = before.Bid != null && Regex.IsMatch(
                    sentence[..before.Index],
                    @"\b(?:compared\s+(?:with|to)|than|unlike|versus|vs\.?)\s+$",
                    RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(sentence[before.Index..claim.Index], @",\s*(?:this\b|it\b|the\s+(?:selected|chosen|recommended)\b)", RegexOptions.IgnoreCase);

                var afterIsTarget = after.Bid != null && (directComparison || Regex.IsMatch(
                    sentence[claim.Index..after.Index],
                    @"\b(?:than|compared\s+(?:with|to)|versus|vs\.?|over|against|making)\b",
                    RegexOptions.IgnoreCase));

                var named = beforeIsBaseline
                    ? null
                    : before.Bid ?? (afterIsTarget ? null : after.Bid);
                var specific = named != null || directComparison || Regex.IsMatch(sentence,
                    @"\b(?:selected|chosen|recommended|this offer|this alternative|its|the delivery)\b|\b\d+[- ]days?\b", RegexOptions.IgnoreCase);
                if (!specific) continue; // General statements such as "faster delivery can be a benefit" are not bid claims.
                var bid = named ?? subject;
                var other = mentions.FirstOrDefault(m => m.Index > claim.Index && m.Bid.BidId != bid.BidId).Bid
                    ?? mentions.LastOrDefault(m => m.Index < claim.Index && m.Bid.BidId != bid.BidId).Bid
                    ?? (offers.Count == 2 ? offers.Single(b => b.BidId != bid.BidId) : null);
                var word = claim.Value.ToLowerInvariant();
                var delivery = word is "faster" or "quicker" or "slower" or "fastest" or "slowest";
                if (!delivery && other != null && !string.Equals(bid.Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Prices in different currencies cannot be described as higher or lower without a supplied exchange rate.");
                decimal value = delivery ? bid.DeliveryDays : CommitteeEvaluationPage.Total(bid);
                var peers = offers.Where(b => delivery || string.Equals(b.Currency, bid.Currency, StringComparison.OrdinalIgnoreCase)).ToList();
                decimal Minimum() => peers.Min(b => delivery ? b.DeliveryDays : CommitteeEvaluationPage.Total(b));
                decimal Maximum() => peers.Max(b => delivery ? b.DeliveryDays : CommitteeEvaluationPage.Total(b));
                decimal comparison = other == null ? value : delivery ? other.DeliveryDays : CommitteeEvaluationPage.Total(other);
                var valid = word switch
                {
                    "fastest" or "lowest cost" or "lowest price" or "lowest total cost" => value == Minimum(),
                    "slowest" or "highest cost" or "highest price" or "highest total cost" => value == Maximum(),
                    "faster" or "quicker" or "cheaper" or "less expensive" or "lower cost" or "lower price" or "lower total cost" =>
                        other != null ? value < comparison : peers.Count < 2 || value < Maximum(),
                    _ => other != null ? value > comparison : peers.Count < 2 || value > Minimum()
                };
                if (Regex.IsMatch(prefix, @"\b(?:not|isn't|aren't|doesn't|does not|never|no|without)\b(?:\s+(?:a|an|the|be|have|any|materially|significantly))*\s+$", RegexOptions.IgnoreCase) ||
                    (Regex.IsMatch(prefix, @"\bneither\b", RegexOptions.IgnoreCase) && Regex.IsMatch(prefix, @"\bnor\b", RegexOptions.IgnoreCase)))
                    valid = !valid;
                if (!valid) throw new InvalidOperationException($"The claim '{claim.Value}' for offer #{bid.BidId} contradicts the saved {(delivery ? "delivery days" : "complete quote totals")}. Follow comparisonStatements; do not reverse the comparison.");
            }
            if (!directComparison && mentions.Count > 0) subject = mentions[0].Bid;
        }
    }
}
