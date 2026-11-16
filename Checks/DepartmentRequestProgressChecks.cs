using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Checks;

public static class DepartmentRequestProgressChecks
{
    public static void Run()
    {
        var submitted = new PurchaseRequest { Status = "Submitted" };
        var returned = new PurchaseRequest { Status = "Returned", Tenders = [new() { TenderId = 1, Status = "Published" }] };
        var converted = new PurchaseRequest { Status = "ConvertedToTender" };
        var requests = new[] { submitted, returned, converted };
        Check(DepartmentRequestProgress.Code(submitted) == "Submitted", "Unconverted request keeps its status");
        Check(DepartmentRequestProgress.Code(returned) == "Returned", "Request decision takes precedence before conversion");
        Check(DepartmentRequestProgress.Code(converted) == "ConvertedToTender", "Missing tender retains conversion status");
        foreach (var stage in new[] { "Draft", "Published", "UnderEvaluation", "AwaitingOfficerSummary", "AwaitingManagerReview", "AwaitingCommitteeDecision", "Awarded", "Cancelled" })
        {
            converted.Tenders = [new() { TenderId = 2, Status = stage }];
            Check(DepartmentRequestProgress.Code(converted) == stage, "Converted request follows " + stage);
            Check(DepartmentRequestProgress.Filter(requests.AsQueryable(), stage).SequenceEqual([converted]), "Filter follows " + stage);
            Check(!DepartmentRequestProgress.Filter(requests.AsQueryable(), "ConvertedToTender").Any(), "Advanced tender leaves conversion filter");
        }
        converted.Tenders = [new() { TenderId = 5, Status = "Awarded" }, new() { TenderId = 2, Status = "Draft" }];
        Check(DepartmentRequestProgress.Code(converted) == "Awarded", "Most recent tender chosen independently of collection order");
        Check(DepartmentRequestProgress.Filter(requests.AsQueryable(), "Awarded").Single() == converted, "Filter chooses same tender as badge");
        Console.WriteLine("PASS: Department request progress, all tender stages, fallbacks and matching filters.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
