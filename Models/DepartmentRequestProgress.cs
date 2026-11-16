using System.Text.RegularExpressions;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

public static class DepartmentRequestProgress
{
    public static readonly string[] Statuses =
    [
        "Submitted", "UnderReview", "Returned", "Approved", "ConvertedToTender",
        "Draft", "Published", "Open", "Closed", "UnderEvaluation",
        "AwaitingOfficerSummary", "AwaitingManagerReview", "AwaitingCommitteeDecision",
        "Awarded", "Rejected", "Cancelled"
    ];

    public static string Code(PurchaseRequest request) =>
        request.Status == "ConvertedToTender"
            ? request.Tenders.OrderByDescending(t => t.TenderId).FirstOrDefault()?.Status ?? request.Status
            : request.Status;

    // Keep filtering in SQL, using the same current stage shown by Code.
    public static IQueryable<PurchaseRequest> Filter(IQueryable<PurchaseRequest> requests, string status) =>
        requests.Where(request => (request.Status == "ConvertedToTender"
            ? request.Tenders.OrderByDescending(t => t.TenderId).Select(t => t.Status).FirstOrDefault() ?? request.Status
            : request.Status) == status);

    public static string Label(string status) => status switch
    {
        "UnderReview" => "Under review",
        "ConvertedToTender" => "Converted to tender",
        "UnderEvaluation" => "Under evaluation",
        "AwaitingOfficerSummary" => "Officer review",
        "AwaitingManagerReview" => "Manager review",
        "AwaitingCommitteeDecision" => "Committee decision",
        _ => Regex.Replace(status, "([a-z])([A-Z])", "$1 $2")
    };
}
