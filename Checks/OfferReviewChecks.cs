using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website.
public static class OfferReviewChecks
{
    public static Task RunAsync()
    {
        var submitted = new Bid { BidId = 1, Status = "Submitted" };
        var second = new Bid { BidId = 2, Status = "UnderReview" };
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
        Check(SourcingOfficerController.ValidateOfferDecisions([], new Dictionary<int,string>()) != null, "No bids");
        Check(SourcingOfficerController.ValidateOfferDecisions([submitted, second], new Dictionary<int,string>{{1,"Compliant"}}) != null, "Incomplete review");
        Check(SourcingOfficerController.ValidateOfferDecisions([submitted], new Dictionary<int,string>{{1,"Accepted"}}) != null, "Unsupported decision");
        Check(SourcingOfficerController.ValidateOfferDecisions([submitted], new Dictionary<int,string>{{1,"NonCompliant"}}) != null, "All non-compliant");
        Check(SourcingOfficerController.ValidateOfferDecisions([submitted,
            second], new Dictionary<int,
            string>{{1,"Compliant"},
            {2,"NonCompliant"}}) == null, "Mixed decisions");
        Check(submitted.ComplianceStatus == null
            && second.ComplianceStatus == null, "Validation must not partially save");
        second.ComplianceStatus = "NonCompliant";
        Check(SourcingOfficerController.ValidateOfferDecisions([second], new Dictionary<int,string>{{2,"Compliant"}}) != null, "Cannot override recorded rejection");
        second.ComplianceStatus = "Compliant";
        Check(SourcingOfficerController.ValidateOfferDecisions([second], new Dictionary<int,string>()) == null, "Previously reviewed offer");
        submitted.Status = "Withdrawn";
        Check(SourcingOfficerController.ValidateOfferDecisions([submitted], new Dictionary<int,string>{{1,"Compliant"}}) != null, "Withdrawn cannot be reviewed");
        Console.WriteLine("PASS: 9 offer review validation checks");
        return Task.CompletedTask;
    }
}
