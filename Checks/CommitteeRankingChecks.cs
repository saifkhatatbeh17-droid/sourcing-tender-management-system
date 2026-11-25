using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Checks;

public static class CommitteeRankingChecks
{
    public static void Run()
    {
        var tender = new Tender { Status = "UnderEvaluation" };
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
        page.Offers[1].Currency = "USD";
        Check(!page.Ready, "Different currencies were compared.");
        page.Offers[1].Currency = "JOD";
        page.Evaluations[0].EvaluationScores.Clear();
        Check(!page.Ready, "Incomplete forms were ready.");
        Console.WriteLine("PASS: complete forms, currencies and incomplete forms.");
    }
}
