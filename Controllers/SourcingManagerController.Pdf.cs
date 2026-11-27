using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Services.Reports;
namespace SourcingTenderManagement.Controllers
{
    // Sourcing Manager PDF downloads: the reports list and one submission.
    public partial class SourcingManagerController
    {
        // Reports
        // GET /SourcingManager/ReportsPdf: Opens the reports pdf page.
        [HttpGet]
        public IActionResult ReportsPdf(string? search, string? decision)
        {
            var result = Reports(search, decision);
            // قراءة بيانات الصفحة قبل تجهيز ملف التقرير
            var view = result as ViewResult;
            var page = view?.Model as ManagerReportPage;

            // إذا بيانات صفحة التقارير غير متاحة، يرجع النتيجة الأصلية بدل إنشاء ملف PDF.
            if (page == null)
            {
                return result;
            }
            return ReportPdfDownload.Create(this, delegate { return ReportPdfExport.ManagerDecisions(page); }, "manager-decisions.pdf");
        }
        // Officer Submissions
        // GET /SourcingManager/SubmissionPdf: Opens the submission pdf page.
        [HttpGet]
        public IActionResult SubmissionPdf(int id)
        {
            var result = Submissions(id);
            // قراءة بيانات المعاملة والتحقق من التقرير المطلوب
            var view = result as ViewResult;
            var page = view?.Model as ManagerReviewPage;

            // إذا بيانات صفحة المعاملة غير متاحة، يرجع النتيجة الأصلية بدل إنشاء ملف PDF.
            if (page == null)
            {
                return result;
            }
            // إذا التقرير المحمّل غير التقرير المطلوب، يمنع تنزيل الملف.
            if (page.Selected?.EvaluationReportId != id)
            {
                return NotFound();
            }
            return ReportPdfDownload.Create(this, delegate { return ReportPdfExport.ManagerSubmission(page); }, $"manager-submission-{id}.pdf");
        }
    }
}
