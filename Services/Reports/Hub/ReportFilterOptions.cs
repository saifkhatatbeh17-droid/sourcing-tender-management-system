using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Reports;
using SourcingTenderManagement.Services.Reports.Data;

namespace SourcingTenderManagement.Services.Reports.Hub;

// Loads the choices for the report filters (departments, suppliers, tenders) that the user may see.
public sealed class ReportFilterOptions(ApplicationDbContext db)
{
    public async Task<ReportFilterChoices> LoadAsync(IReport report, ReportScope scope, CancellationToken ct)
    {
        // Use the same tender visibility queries as Person B's reports, including committee assignments.
        var tenders = ReportRows.Tenders(db, scope);
        if (!scope.IsAdmin && scope.Role == RoleNames.Department)
            tenders = db.Tenders.Where(tender => scope.DepartmentId != null &&
                tender.PurchaseRequest.DepartmentId == scope.DepartmentId);
        if (report is TendersReport) tenders = TendersReport.VisibleTenders(db, scope);
        var tenderIds = tenders.Select(tender => tender.TenderId);
        IReadOnlyList<ReportFilterOption> departments = [], suppliers = [], tenderOptions = [];

        if (report.Filters.Contains("department"))
        {
            var rows = db.Departments.AsNoTracking();
            if (report is PurchaseRequestsReport)
            {
                var requests = PurchaseRequestsReport.VisibleRequests(db, scope);
                rows = rows.Where(department => requests.Any(request => request.DepartmentId == department.DepartmentId));
            }
            else if (report is TendersReport)
            {
                rows = rows.Where(department => tenders.Any(tender => tender.PurchaseRequest.DepartmentId == department.DepartmentId));
            }
            else if (!scope.IsAdmin)
            {
                var requests = db.PurchaseRequests.AsQueryable();
                requests = scope.Role switch
                {
                    RoleNames.Department => requests.InDepartment(scope.DepartmentId),
                    RoleNames.SourcingOfficer => requests.ReviewableBy(scope.UserId),
                    _ => requests.Where(request => tenders.Any(tender => tender.PurchaseRequestId == request.PurchaseRequestId))
                };
                rows = rows.Where(department => requests.Any(request => request.DepartmentId == department.DepartmentId));
            }
            departments = await rows.OrderBy(department => department.Name)
                .Select(department => new ReportFilterOption(department.DepartmentId, department.Name)).ToListAsync(ct);
        }

        if (report.Filters.Contains("supplier"))
        {
            var rows = db.SupplierProfiles.AsNoTracking();
            if (!scope.IsAdmin || report is TendersReport)
                rows = rows.Where(supplier => supplier.Invitations.Any(invitation => tenderIds.Contains(invitation.TenderId)));
            suppliers = await rows.OrderBy(supplier => supplier.CompanyName)
                .Select(supplier => new ReportFilterOption(supplier.SupplierProfileId, supplier.CompanyName)).ToListAsync(ct);
        }

        if (report.Filters.Contains("tender"))
            tenderOptions = await tenders.AsNoTracking().OrderByDescending(tender => tender.CreatedAt)
                .ThenByDescending(tender => tender.TenderId)
                .Select(tender => new ReportFilterOption(tender.TenderId, "#" + tender.TenderId + " · " + tender.Title))
                .ToListAsync(ct);

        return new ReportFilterChoices(departments, suppliers, tenderOptions);
    }
}
