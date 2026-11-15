using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Checks;

public static class CommitteeRankingChecks
{
    public static void Run()
    {
        // Historical, completed evaluations retain their original category-based ranking.
        var tender = new Tender { Status = "AwaitingOfficerSummary" };
        tender.EvaluationCriteria = [
            new() { EvaluationCriterionId = 1, Category = "Financial", Weight = 40 },
            new() { EvaluationCriterionId = 2, Category = "Requirements", Weight = 60 }];
        for (var position = 1; position <= 4; position++)
            tender.CommitteeAssignments.Add(new() { CommitteeAssignmentId = position, Position = position, CommitteeType = "Evaluation" });
        var invitation = new Invitation { SupplierProfile = new() { CompanyName = "Test supplier" } };
        tender.Invitations.Add(invitation);
        var page = new CommitteeEvaluationPage { Tender = tender };
        for (var bidId = 1; bidId <= 2; bidId++)
        {
            var bid = new Bid { BidId = bidId, Currency = "JOD", Invitation = invitation,
                BidItems = [new() { Quantity = 2, UnitPrice = bidId == 1 ? 600 : 400 }] };
            invitation.Bids.Add(bid);
            foreach (var member in page.Members)
                page.Evaluations.Add(new Evaluation { BidId = bidId, CommitteeAssignmentId = member.CommitteeAssignmentId,
                    EvaluationScores = [new() { EvaluationCriterionId = member.Position == 3 ? 1 : 2,
                        Score = member.Position == 3 ? (bidId == 1 ? 100 : 60) : (bidId == 1 ? 60 : 100) }] });
        }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Check(page.Ready, "Complete forms were not ready.");
        Check(page.Rank()[0].BidId == 2 && page.Rank()[0].Score == 84, "Configured 40/60 ranking incorrect.");
        Check(page.Rank(60)[0].BidId == 1 && page.Rank(60)[0].Score == 84, "60/40 ranking incorrect.");
        Check(page.Rank(100)[0].BidId == 1 && page.Rank(100)[0].Score == 100, "Financial ranking incorrect.");
        Check(page.Rank(0)[0].BidId == 2 && page.Rank(0)[0].Score == 100, "Requirements ranking incorrect.");
        Check(page.PriceScore(page.Offers[0]) == 66.67m, "Price normalization incorrect.");
        page.Offers[0].BidItems.First().UnitPrice = 0;
        Check(page.PriceScore(page.Offers[0]) == 100 && page.PriceScore(page.Offers[1]) == 0, "Zero price handling incorrect.");
        page.Offers[1].Currency = "USD";
        Check(!page.Ready && page.Rank().Count == 0, "Different currencies were compared.");
        page.Offers[1].Currency = "JOD";
        page.Evaluations[0].EvaluationScores.Clear();
        Check(!page.Ready, "Incomplete forms were ready.");
        Console.WriteLine("PASS: configured weights, all three recommendation ratios, price normalization, zero prices, currencies and incomplete forms.");
    }
}
