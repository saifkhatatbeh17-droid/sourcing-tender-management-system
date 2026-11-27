using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Checks;
// Self-test helper used by RankedReportChecks, ReportContentChecks.

public static class CommitteeScoringChecks
{
    public static void Run()
    {
        var now = DateTime.UtcNow;
        var page = Fixture();
        Check(CommitteeConsensus.ConfirmWeight(page, null, now) != null, "Missing proposal rejected");
        Check(CommitteeConsensus.ConfirmWeight(page, 100, now) != null, "Zero technical maximum rejected");
        Check(CommitteeConsensus.ConfirmWeight(page, 20.5m, now) != null, "Excess precision rejected");
        for (int i = 1; i <= 4; i++)
        {
            page.UserId = "member" + i;
            Check(CommitteeConsensus.ConfirmWeight(page, 20 + i * 10, now) == null, "Independent proposal accepted");
            Check(CommitteeConsensus.ConfirmWeight(page, 70, now) != null, "Confirmed proposal immutable");
            Check(page.ConsensusLocked == (i == 4), "No premature consensus lock");
        }
        Check(page.FinancialWeight == 45 && page.TechnicalWeight == 55 && page.Criteria.Sum(c => c.Weight) == 100, "Four-member average and complementary maximum");
        Check(page.WeightsReady, "Original approvals unlock after consensus");
        Check(page.Tender!.Status == "UnderEvaluation" && page.Approvals.Count == 0 && page.Tender.EvaluationReport == null,
            "Averaging cannot choose an offer, approve it or advance the tender");
        var rounding = Fixture();
        for (int i = 1; i <= 4; i++) { rounding.UserId = "member" + i; CommitteeConsensus.ConfirmWeight(rounding, i, now); }
        Check(rounding.FinancialWeight == 3m && rounding.TechnicalWeight == 97m, "Consensus rounding is deterministic");
        Console.WriteLine("PASS: independent percentage proposals, locked average and original approval gating.");
    }

    public static CommitteeEvaluationPage Fixture()
    {
        var tender = new Tender { TenderId = 1, Status = "UnderEvaluation", CreatedByUserId = "officer", Title = "Pharmacy supplies", PurchaseRequest = new() };
        tender.EvaluationCriteria = [new() { EvaluationCriterionId = 1, Category = "Financial", Weight = 40 }, new() { EvaluationCriterionId = 2, Category = "Requirements", Weight = 60 }];
        for (int i = 1; i <= 4; i++) tender.CommitteeAssignments.Add(new CommitteeAssignment { CommitteeAssignmentId = i, TenderId = 1, UserId = "member" + i, Position = i, CommitteeType = "Evaluation", User = new() { Id = "member" + i, IsActive = true, FullName = "Member " + i } });
        var invitation = new Invitation { SupplierProfile = new() { CompanyName = "Pharmacy supplier" } };
        for (int i = 1; i <= 2; i++) invitation.Bids.Add(new Bid { BidId = i, TenderId = 1, Currency = "JOD", Status = "Compliant", ComplianceStatus = "Compliant", Invitation = invitation, BidItems = [new() { Quantity = 10, UnitPrice = i * 5 }] });
        tender.Invitations.Add(invitation);
        return new CommitteeEvaluationPage { Tender = tender, UserId = "member1" };
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
