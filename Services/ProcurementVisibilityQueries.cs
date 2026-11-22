using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services;

internal static class ProcurementVisibilityQueries
{
    public static IQueryable<PurchaseRequest> InDepartment(this IQueryable<PurchaseRequest> requests, int? departmentId) =>
        requests.Where(r => r.DepartmentId == departmentId);

    public static IQueryable<PurchaseRequest> ReviewableBy(this IQueryable<PurchaseRequest> requests, string userId) =>
        requests.Where(r => r.Status != "Draft" &&
            (r.ReviewedByUserId == userId || (r.ReviewedByUserId == null && r.Status == "Submitted")));

    public static IQueryable<Tender> InEvaluationWorkspace(this IQueryable<Tender> tenders, string userId) =>
        tenders.Where(t => t.Status == "UnderEvaluation" ||
            t.CommitteeAssignments.Any(a => a.CommitteeType == "Evaluation" && a.UserId == userId));

    public static IQueryable<EvaluationReport> InManagerReview(this IQueryable<EvaluationReport> reports) =>
        reports.Where(r => r.Tender.Status == "AwaitingManagerReview" &&
            r.Tender.TopEditState == null &&
            !r.Tender.TenderApprovals.Any(a => a.Stage == "Top" && a.Decision == "Rejected") &&
            (r.Status == "OfficerSubmitted" || r.Status == "CommitteeApproved" || r.Status == "ManagerApproved"));
}
