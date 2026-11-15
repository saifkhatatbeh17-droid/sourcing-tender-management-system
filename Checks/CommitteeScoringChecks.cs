using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Evaluation;

namespace SourcingTenderManagement.Checks;

public static class CommitteeScoringChecks
{
    public static void Run()
    {
        var now = DateTime.UtcNow;
        var page = Fixture();
        Check(CommitteeScoring.AcceptOffer(page, 1, 10, 20, null, now) != null, "Scoring before consensus rejected");
        Check(CommitteeScoring.ConfirmWeight(page, null, now) != null, "Missing proposal rejected");
        Check(CommitteeScoring.ConfirmWeight(page, 100, now) != null, "Zero technical maximum rejected");
        Check(CommitteeScoring.ConfirmWeight(page, 20.001m, now) != null, "Excess precision rejected");
        for (int i = 1; i <= 4; i++)
        {
            page.UserId = "member" + i;
            Check(CommitteeScoring.ConfirmWeight(page, 20 + i * 10, now) == null, "Independent proposal accepted");
            Check(CommitteeScoring.ConfirmWeight(page, 70, now) != null, "Confirmed proposal immutable");
            Check(page.ConsensusLocked == (i == 4), "No premature consensus lock");
        }
        Check(page.FinancialWeight == 45 && page.TechnicalWeight == 55 && page.Criteria.Sum(c => c.Weight) == 100, "Four-member average and complementary maximum");
        Check(CommitteeScoring.AcceptOffer(page, 1, null, 20, null, now) != null, "Both fields required");
        Check(CommitteeScoring.AcceptOffer(page, 1, 46, 20, null, now) != null, "Financial maximum enforced");
        Check(CommitteeScoring.AcceptOffer(page, 1, 20, 56, null, now) != null, "Technical maximum enforced");
        Check(CommitteeScoring.AcceptOffer(page, 99, 20, 30, null, now) != null, "Foreign offer rejected");
        for (int i = 1; i <= 4; i++)
        {
            page.UserId = "member" + i;
            Check(CommitteeScoring.AcceptOffer(page, 1, i * 10, i * 10 + 10, null, now) == null, "Every member scores both categories");
            Check(CommitteeScoring.AcceptOffer(page, 1, 0, 0, null, now) != null, "Accepted marks immutable");
            Check(page.AcceptedCount(page.Offers[0]) == i, "Acceptance counter follows independent forms");
            Check(!CommitteeScoring.FinalizeIfReady(page, now), "One fully reviewed offer cannot bypass other offers");
        }
        for (int i = 1; i <= 4; i++)
        {
            page.UserId = "member" + i;
            Check(CommitteeScoring.AcceptOffer(page, 2, 40, 50, null, now) == null, "Second offer reviewed");
        }
        Check(page.Rank()[0].BidId == 2 && page.Rank()[0].Score == 90 && page.Rank()[1].Score == 60, "Average sums ranked without double weighting");
        Check(CommitteeScoring.FinalizeIfReady(page, now), "All forms trigger winner progression");
        Check(page.Tender!.Status == "AwaitingOfficerSummary" && page.Tender.EvaluationReport!.RecommendedBidId == 2 && page.Approvals.Count == 4 && page.ReportReady, "Winner, report and four-member handoff saved together");
        Check(!CommitteeScoring.FinalizeIfReady(page, now), "Handoff cannot repeat");
        CommitteeScoring.ReopenReviews(page.Tender, page.Evaluations);
        Check(page.ConsensusLocked && page.FinancialWeight == 45 && !page.AllOffersAccepted
            && page.Evaluations.All(e => e.ApprovedAt == null && e.FinancialMark.HasValue)
            && page.Tender.EvaluationReport!.RecommendedBidId == null, "Explicit upstream return reopens reviews without changing the frozen percentage");

        var tie = Fixture();
        for (int i = 1; i <= 4; i++) { tie.UserId = "member" + i; CommitteeScoring.ConfirmWeight(tie, 50, now); }
        foreach (var offer in tie.Offers) for (int i = 1; i <= 4; i++) { tie.UserId = "member" + i; CommitteeScoring.AcceptOffer(tie, offer.BidId, 40, 40, null, now); }
        Check(tie.HasTie && tie.ScoredWinnerId == null && !CommitteeScoring.FinalizeIfReady(tie, now), "Equal highest scores pause progression");
        Check(CommitteeScoring.ResolveTie(tie, 999, now) != null, "Tie decision limited to tied highest offers");
        for (int i = 1; i <= 4; i++) { tie.UserId = "member" + i; CommitteeScoring.ResolveTie(tie, i == 4 ? 1 : 2, now); }
        Check(!CommitteeScoring.FinalizeIfReady(tie, now), "Split committee cannot resolve a tie");
        tie.UserId = "member4"; CommitteeScoring.ResolveTie(tie, 2, now);
        Check(CommitteeScoring.FinalizeIfReady(tie, now) && tie.Tender!.EvaluationReport!.RecommendedBidId == 2, "Explicit unanimous tie decision progresses without changing marks");

        var legacy = Fixture();
        legacy.Tender!.TenderApprovals = legacy.Members.Select(m => new TenderApproval { Stage = "Evaluation", Decision = "Approved", UserId = m.UserId }).ToList();
        legacy.Evaluations.Add(new Evaluation { BidId = 1, CommitteeAssignmentId = 1, Status = "Approved", ApprovedAt = now });
        Check(!legacy.AllOffersAccepted && legacy.AcceptedCount(legacy.Offers[0]) == 0, "Old bulk approvals do not count as scored forms");
        var rounding = Fixture();
        for (int i = 1; i <= 4; i++) { rounding.UserId = "member" + i; CommitteeScoring.ConfirmWeight(rounding, i / 100m, now); }
        Check(rounding.FinancialWeight == .03m && rounding.TechnicalWeight == 99.97m, "Consensus rounding is deterministic");
        var zero = Fixture();
        for (int i = 1; i <= 4; i++) { zero.UserId = "member" + i; CommitteeScoring.ConfirmWeight(zero, 50, now); }
        Check(CommitteeScoring.AcceptOffer(zero, 1, -1, 20, null, now) != null, "Negative marks rejected");
        Check(CommitteeScoring.AcceptOffer(zero, 1, 1.111m, 20, null, now) != null, "Excess mark precision rejected");
        Check(CommitteeScoring.AcceptOffer(zero, 1, 0, 0, null, now) == null, "Zero is a valid explicitly entered mark");
        zero.Offers[1].Currency = "USD";
        Check(!zero.WeightsReady && CommitteeScoring.AcceptOffer(zero, 2, 10, 10, null, now) != null, "Mixed-currency offers cannot be scored together");
        Console.WriteLine("PASS: committee consensus, immutability, per-offer marks, complete review gates, ranking, tie decisions and progression.");
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
