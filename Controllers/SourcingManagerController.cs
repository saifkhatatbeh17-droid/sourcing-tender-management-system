using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SourcingTenderManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers
{
    // The Sourcing Manager workspace: review the officer's submitted reports and forward them to the CEO & Chiefs.
    // This class is split into several files (SourcingManagerController.*.cs).
    [Authorize(Roles = RoleNames.SourcingManager)]
    [AutoValidateAntiforgeryToken]
    public partial class SourcingManagerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SourcingManagerController> _logger;

        public SourcingManagerController(ApplicationDbContext context, ILogger<SourcingManagerController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private const string AwaitingManagerReview = "AwaitingManagerReview";
        private const string AwaitingCommitteeDecision = "AwaitingCommitteeDecision";
        private const string ManagerApproved = "ManagerApproved";
        private const string ReturnedForReview = "ReturnedForReview";

        private string ManagerId
        {
            get
            {
                return User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            }
        }
        // GET /SourcingManager/Index: Opens the start page of this area.
        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Dashboard));
        }

        // Dashboard
        // GET /SourcingManager/Dashboard: Shows the "Sourcing Manager Dashboard" page.
        [HttpGet]
        public IActionResult Dashboard()
        {
            int reportsAwaitingReview = 0;
            int approvedRecommendations = 0;
            int tendersWithTopCommittee = 0;
            int pendingQuestions = 0;

            List<ManagerReportStatus> stateCounts = new List<ManagerReportStatus>();
            var reports = _context.EvaluationReports
                .AsNoTracking()
                .Include("Tender.TenderApprovals")
                .ToList();

            foreach (var report in reports)
            {
                // إذا العطاء بمرحلة مراجعة المانجر، يحسب التقارير التي تحتاج قراراً أو إحالة.
                if (report.Tender.Status == AwaitingManagerReview)
                {
                    // إذا التقرير مرسل من الموظف أو معتمد من اللجنة، يزيد عدد التقارير التي تحتاج مراجعة.
                    if (report.Status == "OfficerSubmitted"
                        || report.Status == "CommitteeApproved")
                    {
                        reportsAwaitingReview++;
                    }

                    // إذا التقرير معتمد من المانجر، يزيد عدد التوصيات الجاهزة للإحالة.
                    if (report.Status == ManagerApproved)
                    {
                        approvedRecommendations++;
                    }
                }

                // التقارير التي تظهر ضمن تصنيف حالات الداشبورد
                // إذا التقرير خارج الحالات التي يعرضها ملخص الداشبورد، يتخطاه.
                if (report.Status != "OfficerSubmitted"
                    && report.Status != "ManagerApproved"
                    && report.Status != "ManagerRejected"
                    && report.Status != "TopApproved"
                    && report.ManagerSummary == null
                    && report.Tender.Status != AwaitingManagerReview
                    && report.Tender.Status != AwaitingCommitteeDecision
                    && report.Tender.Status != "Awarded")
                {
                    continue;
                }

                bool rejectedByTopCommittee = false;

                foreach (var approval in report.Tender.TenderApprovals)
                {
                    // إذا في قرار رفض من اللجنة العليا، يسجل أن العطاء مرفوض من اللجنة.
                    if (approval.Stage == "Top" && approval.Decision == "Rejected")
                    {
                        rejectedByTopCommittee = true;
                        break;
                    }
                }

                string status;

                // إذا التقرير مرفوض من المانجر أو العطاء مرفوض من اللجنة العليا، يصنّفه كمرفوض.
                if (report.Status == "ManagerRejected" || rejectedByTopCommittee)
                {
                    status = "Rejected";
                }
                // إذا العطاء عند المانجر والتقرير معتمد منه، يصنّفه كجاهز للإحالة.
                else if (report.Tender.Status == AwaitingManagerReview
                    && report.Status == ManagerApproved)
                {
                    status = "Ready to forward";
                }
                // إذا العطاء عند المانجر والتقرير مرسل للمراجعة، يصنّفه كبحاجة لمراجعتك.
                else if (report.Tender.Status == AwaitingManagerReview
                    && (report.Status == "OfficerSubmitted"
                        || report.Status == "CommitteeApproved"))
                {
                    status = "Needs your review";
                }
                // إذا العطاء بانتظار قرار اللجنة العليا، يصنّفه كعطاء عند اللجنة العليا.
                else if (report.Tender.Status == AwaitingCommitteeDecision)
                {
                    status = "With CEO & Chiefs";
                }
                // إذا تمت ترسية العطاء، يصنّفه كعطاء مُرسّى.
                else if (report.Tender.Status == "Awarded")
                {
                    status = "Awarded";
                }
                // إذا العطاء ملغى، يصنّفه كملغى.
                else if (report.Tender.Status == "Cancelled")
                {
                    status = "Cancelled";
                }
                // إذا التقرير معاد للتقييم، يصنّفه كمعاملة رجعت للجنة.
                else if (report.Status == ReturnedForReview)
                {
                    status = "Back with committee";
                }
                else
                {
                    status = "With sourcing team";
                }

                ManagerReportStatus? savedStatus = null;

                foreach (var item in stateCounts)
                {
                    // إذا الحالة موجودة مسبقاً بالقائمة، يحتفظ بسجلها لزيادة العدد.
                    if (item.Status == status)
                    {
                        savedStatus = item;
                        break;
                    }
                }

                // إذا الحالة جديدة، ينشئ لها سجلاً في القائمة ويبدأ عددها بواحد.
                if (savedStatus == null)
                {
                    stateCounts.Add(new ManagerReportStatus
                    {
                        Status = status,
                        Count = 1
                    });
                }
                else
                {
                    savedStatus.Count++;
                }
            }

            foreach (var tender in _context.Tenders.AsNoTracking())
            {
                // إذا العطاء بانتظار اللجنة العليا، يزيد عدّاد العطاءات الموجودة عندها.
                if (tender.Status == AwaitingCommitteeDecision)
                {
                    tendersWithTopCommittee++;
                }
            }

            foreach (var question in _context.Clarifications.AsNoTracking())
            {
                // إذا السؤال موجّه للمانجر وبانتظار الرد، يزيد عدّاد الأسئلة المطلوبة منه.
                if (question.RecipientUserId == ManagerId && question.Status == "Pending")
                {
                    pendingQuestions++;
                }
            }

            ViewBag.DashboardReportStates = stateCounts;

            var dashboard = new ManagerDashboard(
                reportsAwaitingReview,
                approvedRecommendations,
                tendersWithTopCommittee,
                pendingQuestions);

            ViewData.Model = dashboard;
            return View();
        }

        // Officer Submissions
        // GET /SourcingManager/Submissions: Shows the "Officer Submissions" page.
        [HttpGet]
        public IActionResult Submissions(int? id, int? rejectedTenderId = null, bool showRejections = false)
        {
            // تحميل طلبات التعديل والعطاءات المرفوضة
            var rejections = ManagedRejections();
            var editRequests = ManagedTopEditRequests();
            // إذا وصل رقم عطاء مرفوض أو مطلوب تعديله، يحمّل بيانات ذلك العطاء.
            if (rejectedTenderId.HasValue)
            {
                List<Tender> selectedRejections = new List<Tender>();
                List<Tender> selectedEditRequests = new List<Tender>();

                foreach (var tender in rejections)
                {
                    // إذا رقم العطاء مطابق للمطلوب، يضيفه لقائمة الرفض المختارة.
                    if (tender.TenderId == rejectedTenderId)
                    {
                        selectedRejections.Add(tender);
                    }
                }

                foreach (var tender in editRequests)
                {
                    // إذا رقم العطاء مطابق للمطلوب، يضيفه لقائمة طلبات التعديل المختارة.
                    if (tender.TenderId == rejectedTenderId)
                    {
                        selectedEditRequests.Add(tender);
                    }
                }

                // إذا العطاء غير موجود بقوائم الرفض والتعديل المسموحة، يمنع عرضه.
                if (selectedRejections.Count == 0 && selectedEditRequests.Count == 0)
                {
                    return NotFound();
                }

                // إذا وجد عطاء مرفوض مطابق، يحصر قائمة الرفض فيه.
                if (selectedRejections.Count > 0)
                {
                    rejections = selectedRejections;
                }

                // إذا وجد طلب تعديل مطابق، يحصر قائمة التعديلات فيه.
                if (selectedEditRequests.Count > 0)
                {
                    editRequests = selectedEditRequests;
                }
            }

            var editList = editRequests.ToList();
            editList.Sort(delegate (Tender first, Tender second)
            {
                return (second.TopEditRequestedAt ?? second.CreatedAt)
                    .CompareTo(first.TopEditRequestedAt ?? first.CreatedAt);
            });

            HashSet<string> requesterIds = new HashSet<string>();
            foreach (var tender in editList)
            {
                // إذا رقم طالب التعديل موجود، يضيفه لقائمة الحسابات المطلوب تحميل أسمائها.
                if (!string.IsNullOrWhiteSpace(tender.TopEditRequestedByUserId))
                {
                    requesterIds.Add(tender.TopEditRequestedByUserId);
                }
            }

            List<ManagerRequester> requesters = new List<ManagerRequester>();
            // إذا في حسابات طالبة للتعديل، يحمّل أسماءها لعرضها بالصفحة.
            if (requesterIds.Count > 0)
            {
                foreach (var user in _context.Users.AsNoTracking())
                {
                    // إذا المستخدم من طالبي التعديل المطلوبين، يضيف رقمه واسمه للقائمة.
                    if (requesterIds.Contains(user.Id))
                    {
                        requesters.Add(new ManagerRequester
                        {
                            UserId = user.Id,
                            FullName = user.FullName
                        });
                    }
                }
            }

            var rejectedTenders = rejections.ToList();
            rejectedTenders.Sort(delegate (Tender first, Tender second)
            {
                return second.CreatedAt.CompareTo(first.CreatedAt);
            });

            ViewBag.TopEditRequests = editList;
            ViewBag.TopEditRequesters = requesters;
            ViewBag.RejectedTenders = rejectedTenders;
            ViewBag.OpenRejections = showRejections || rejectedTenderId.HasValue;

            var evaluationReports = _context.EvaluationReports
                .Include("Tender.OfferExtensions")
                .InManagerReview()
                .ToList();

            evaluationReports.Sort(delegate (EvaluationReport first, EvaluationReport second)
            {
                int dateOrder = Nullable.Compare(second.SubmittedAt, first.SubmittedAt);
                // إذا تاريخا التقريرين مختلفان، يعرض الأحدث أولاً.
                if (dateOrder != 0)
                {
                    return dateOrder;
                }

                return second.EvaluationReportId.CompareTo(first.EvaluationReportId);
            });

            // اختيار التقرير المطلوب أو أول تقرير متاح
            EvaluationReport? selectedReport;
            // إذا وصل رقم تقرير، يبحث عن التقرير المطلوب عرضه.
            if (id.HasValue)
            {
                selectedReport = _context.EvaluationReports.Find(id.Value);

                // إذا التقرير غير موجود أو خارج قائمة المراجعة المسموحة، يمنع عرضه.
                if (selectedReport == null || !evaluationReports.Contains(selectedReport))
                {
                    return NotFound();
                }
            }
            else
            {
                selectedReport = evaluationReports.FirstOrDefault();
            }

            // إذا ما في تقرير متاح للاختيار، يعرض الصفحة بقائمة عروض فارغة.
            if (selectedReport == null)
            {
                var emptyPage = new ManagerReviewPage(evaluationReports, null, new List<Bid>(), null);
                ViewData.Model = emptyPage;
                return View();
            }

            // تحميل عروض العطاء وتقييمات اللجنة
            var tenderId = selectedReport.TenderId;
            ViewData["AiChatRecordId"] = selectedReport.EvaluationReportId;
            var bids = _context.Bids
                .AsNoTracking()
                .Include("Invitation.SupplierProfile")
                .Include("BidItems")
                .Include("Evaluations.EvaluationScores.EvaluationCriterion")
                .Include("Evaluations.CommitteeAssignment")
                .AsSplitQuery()
                .ToList();

            List<Bid> sortedOffers = new List<Bid>();

            foreach (var bid in bids)
            {
                // إذا العرض تابع لعطاء آخر، يتخطاه.
                if (bid.TenderId != tenderId)
                {
                    continue;
                }

                // إذا العرض مسودة أو مسحوب، يستبعده من عروض المراجعة.
                if (bid.Status == "Draft" || bid.Status == "Withdrawn")
                {
                    continue;
                }

                List<Evaluation> submittedEvaluations = new List<Evaluation>();

                foreach (var evaluation in bid.Evaluations)
                {
                    // إذا التقييم مرسل أو معتمد، يضيفه للتقييمات الظاهرة مع العرض.
                    if (evaluation.Status == "Submitted" || evaluation.Status == "Approved")
                    {
                        submittedEvaluations.Add(evaluation);
                    }
                }

                // هذه البيانات للعرض فقط وتم تحميلها بدون تتبع تغييرات
                bid.Evaluations = submittedEvaluations;
                sortedOffers.Add(bid);
            }

            List<Bid> supplierOffers = new List<Bid>();

            sortedOffers.Sort(delegate (Bid first, Bid second)
            {
                return first.BidId.CompareTo(second.BidId);
            });

            foreach (var bid in sortedOffers)
            {
                // إذا العرض هو المرشح بالتقرير، يضعه ببداية قائمة العروض.
                if (bid.BidId == selectedReport.RecommendedBidId)
                {
                    supplierOffers.Insert(0, bid);
                }
                else
                {
                    supplierOffers.Add(bid);
                }
            }

            var page = new ManagerReviewPage(evaluationReports, selectedReport, supplierOffers, selectedReport.SubmittedAt);
            ViewData.Model = page;
            return View();
        }

        // POST /SourcingManager/Decide: Decide. Runs when its form or button is submitted.
        [HttpPost]
        public IActionResult Decide(ManagerDecisionInput input)
        {
            // التحقق من القرار والملاحظات
            var decision = input.Decision;
            string? notes = input.Notes?.Trim();
            bool isApproval = decision == "Approved";
            bool isRejection = decision == "Rejected";
            bool isReturn = decision == ReturnedForReview;

            // إذا القرار أو البيانات غير صحيحة، أو سبب الرفض أو الإعادة فارغ، يمنع حفظ القرار.
            if (!ModelState.IsValid
                || (!isApproval && !isRejection && !isReturn)
                || (!isApproval && string.IsNullOrWhiteSpace(notes)))
            {
                TempData["ManagerMessage"] = "Choose a valid decision and provide comments when rejecting or returning a report.";
                return RedirectToAction("Submissions", new { id = input.ReportId });
            }

            // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

            var evaluationReport = _context.EvaluationReports.Find(input.ReportId);

            // إذا التقرير غير موجود، يوقف تسجيل القرار ويطلب مراجعة أحدث نسخة.
            if (evaluationReport == null)
            {
                TempData["ManagerMessage"] = "This report changed. Review the latest submission before deciding.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            // إذا حالة التقرير لم تعد تسمح بقرار المانجر، يمنع اتخاذ القرار عليه.
            if (evaluationReport.Status != "OfficerSubmitted"
                && evaluationReport.Status != "CommitteeApproved")
            {
                TempData["ManagerMessage"] = "This report changed. Review the latest submission before deciding.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            // إذا نسخة التقرير تغيرت بعد فتح الصفحة، يطلب إعادة مراجعتها قبل الحفظ.
            if (evaluationReport.SubmittedAt?.Ticks != input.ReviewVersion)
            {
                TempData["ManagerMessage"] = "This report changed. Review the latest submission before deciding.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            var tender = _context.Tenders.Find(evaluationReport.TenderId);

            bool rejectedByCommittee = false;
            // إذا العطاء موجود، يفحص قرارات الرفض السابقة من اللجنة العليا.
            if (tender != null)
            {
                foreach (var approval in _context.TenderApprovals)
                {
                    // إذا وجد قرار رفض من اللجنة العليا لنفس العطاء، يسجل وجود الرفض.
                    if (approval.TenderId == tender.TenderId
                        && approval.Stage == "Top"
                        && approval.Decision == "Rejected")
                    {
                        rejectedByCommittee = true;
                        break;
                    }
                }
            }

            // إذا العطاء غير موجود أو مرفوض من اللجنة العليا أو بمرحلة أخرى، يمنع تسجيل القرار.
            if (tender == null
                || rejectedByCommittee
                || tender.Status != AwaitingManagerReview)
            {
                TempData["ManagerMessage"] = "This report changed. Review the latest submission before deciding.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            // إذا في استفسارات مفتوحة مرتبطة بالعطاء، يمنع القرار حتى يتم حلها من الطرفين.
            if (HasOpenClarifications(tender.TenderId))
            {
                TempData["ManagerMessage"] = "Both sides must resolve open clarifications before recording a decision.";
                return RedirectToAction("Submissions", new { id = input.ReportId });
            }
            // تنفيذ الموافقة أو الرفض أو الإعادة للتقييم
            // إذا القرار موافقة، يتحقق من العرض المرشح ثم يعتمد التقرير.
            if (isApproval)
            {
                var recommendedBid = _context.Bids.Find(evaluationReport.RecommendedBidId);

                // إذا العرض المرشح غير موجود أو تابع لعطاء آخر أو حالته غير مؤهلة، يمنع الموافقة.
                if (recommendedBid == null || recommendedBid.TenderId != tender.TenderId ||
                    recommendedBid.Status == "Draft" || recommendedBid.Status == "Withdrawn" ||
                    recommendedBid.Status == "Rejected" || recommendedBid.Status == "NonCompliant" ||
                    recommendedBid.ComplianceStatus == "NonCompliant")
                {
                    TempData["ManagerMessage"] = "The report must identify an eligible recommended offer before approval.";
                    return RedirectToAction("Submissions", new { id = input.ReportId });
                }

                evaluationReport.Status = ManagerApproved;
            }
            // إذا القرار رفض، يسجل رفض المانجر ويغلق العطاء.
            else if (isRejection)
            {
                evaluationReport.Status = "ManagerRejected";
                tender.Status = "Closed";
            }
            else
            {
                evaluationReport.Status = ReturnedForReview;
                tender.Status = "UnderEvaluation";
                evaluationReport.RecommendedBidId = null;
                evaluationReport.CommitteeSummary = null;
                List<TenderApproval> approvalsToRemove = new List<TenderApproval>();
                foreach (var approval in _context.TenderApprovals)
                {
                    // إذا الموافقة السابقة تخص لجنة تقييم نفس العطاء، يجهزها للإزالة عند إعادة التقييم.
                    if (approval.TenderId == tender.TenderId && approval.Stage == "Evaluation")
                    {
                        approvalsToRemove.Add(approval);
                    }
                }
                _context.TenderApprovals.RemoveRange(approvalsToRemove);

                List<CommitteeTieVote> votesToRemove = new List<CommitteeTieVote>();
                foreach (var vote in _context.CommitteeTieVotes)
                {
                    // إذا تصويت التعادل تابع لنفس العطاء، يجهزه للإزالة عند إعادة التقييم.
                    if (vote.TenderId == tender.TenderId)
                    {
                        votesToRemove.Add(vote);
                    }
                }
                _context.CommitteeTieVotes.RemoveRange(votesToRemove);

                foreach (var review in _context.Evaluations)
                {
                    // إذا المراجعة تابعة لنفس العطاء، يعيد فتحها للتقييم ويزيل وقت اعتمادها.
                    if (review.TenderId == tender.TenderId)
                    {
                        review.Status = "Submitted";
                        review.ApprovedAt = null;
                    }
                }

            }

            evaluationReport.ManagerSummary = notes;

            // The existing schema keeps one approval row per manager and tender.
            // تسجيل قرار المانجر للعطاء
            TenderApproval? managerDecision = null;
            foreach (var approval in _context.TenderApprovals)
            {
                // إذا سجل القرار تابع لنفس المانجر والعطاء، يختاره لتحديث القرار.
                if (approval.TenderId == tender.TenderId
                    && approval.Stage == "Manager"
                    && approval.UserId == ManagerId)
                {
                    // إذا وجد أكثر من قرار لنفس المانجر والعطاء، يوقف الحفظ بسبب التكرار.
                    if (managerDecision != null)
                    {
                        throw new InvalidOperationException("Multiple manager decisions were found.");
                    }
                    managerDecision = approval;
                }
            }

            // إذا ما في سجل قرار سابق للمانجر، ينشئ سجل قرار جديداً.
            if (managerDecision == null)
            {
                managerDecision = new TenderApproval
                {
                    TenderId = tender.TenderId,
                    Stage = "Manager",
                    UserId = ManagerId
                };
                _context.TenderApprovals.Add(managerDecision);
            }

            var previousDecision = managerDecision.Decision;
            managerDecision.Decision = decision;
            managerDecision.Notes = notes;
            managerDecision.DecidedAt = DateTime.UtcNow;
            // إذا القرار رفض أو إعادة للتقييم، يرسل إشعاراً للجنة التقييم.
            if (!isApproval)
            {
                NotifyAssignedCommittee(tender.TenderId, "Evaluation", "Manager review completed",
                    $"Tender #{tender.TenderId}: {decision}.", "EvaluationCommittee", "Reports");
            }

            // حفظ كل التغييرات وتأكيد المعاملة
            _context.SaveChanges();
            transaction.Commit();
            _logger.LogInformation("Manager {ManagerId} recorded {Decision} for tender {TenderId}. Previous decision: {PreviousDecision}.",
                ManagerId, decision, tender.TenderId, previousDecision);

            // إذا القرار موافقة، يرجع لصفحة نفس التقرير بعد الحفظ.
            if (isApproval)
            {
                TempData["ManagerMessage"] = "Decision saved.";
                return RedirectToAction("Submissions", new { id = evaluationReport.EvaluationReportId });
            }

            TempData["ManagerMessage"] = "Decision saved.";
            return RedirectToAction("Submissions", new { id = (int?)null });
        }

        // POST /SourcingManager/DraftReferral: Draft referral: sends back data (JSON) for the page's script.
        [HttpPost]
        public async Task<IActionResult> DraftReferral(int reportId,
            [FromServices] SourcingTenderManagement.Services.Evaluation.BidRecommendationService ai, CancellationToken cancellationToken)
        {
            // تحميل التقرير وبيانات العرض المرشح للملخص
            var reports = await _context.EvaluationReports.AsNoTracking()
                .Include("Tender.PurchaseRequest.Department")
                .Include("Tender.OfferExtensions")
                .Include("Tender.PurchaseRequest.PurchaseRequestItems")
                .Include("Bid.Invitation.SupplierProfile")
                .Include("Bid.BidItems")
                .ToListAsync(cancellationToken);

            EvaluationReport? report = null;
            foreach (var savedReport in reports)
            {
                // إذا رقم التقرير مطابق للمطلوب، يختاره لتجهيز الملخص.
                if (savedReport.EvaluationReportId == reportId)
                {
                    report = savedReport;
                    break;
                }
            }

            // إذا التقرير غير موجود، يرفض طلب إنشاء الملخص.
            if (report == null)
            {
                return BadRequest(new { error = "Only an approved recommendation awaiting referral can be summarized." });
            }

            bool rejectedByCommittee = false;
            foreach (var approval in _context.TenderApprovals)
            {
                // إذا وجد رفض من اللجنة العليا لنفس العطاء، يسجل الرفض لمنع إنشاء الملخص.
                if (approval.TenderId == report.TenderId
                    && approval.Stage == "Top"
                    && approval.Decision == "Rejected")
                {
                    rejectedByCommittee = true;
                    break;
                }
            }

            // إذا التقرير أو العطاء أو العرض غير جاهز للإحالة، يمنع إنشاء ملخص الذكاء الاصطناعي.
            if (rejectedByCommittee || report.Status != ManagerApproved
                || report.Tender.Status != AwaitingManagerReview || report.Bid == null)
            {
                return BadRequest(new { error = "Only an approved recommendation awaiting referral can be summarized." });
            }
            try
            {
                // تجهيز العلامات وبيانات الموردين للذكاء الاصطناعي
                List<Evaluation> winnerMarks = new List<Evaluation>();
                var evaluations = await _context.Evaluations.AsNoTracking()
                    .ToListAsync(cancellationToken);

                foreach (var evaluation in evaluations)
                {
                    // إذا المراجعة معتمدة لنفس العرض والعطاء وعلامتاها مكتملتان، يضيفها لحساب علامة الفائز.
                    if (evaluation.TenderId == report.TenderId
                        && evaluation.BidId == report.Bid.BidId
                        && evaluation.Status == "Approved"
                        && evaluation.ApprovedAt != null
                        && evaluation.FinancialMark != null
                        && evaluation.TechnicalMark != null)
                    {
                        winnerMarks.Add(evaluation);
                    }
                }

                HashSet<string> excludedSuppliers = new HashSet<string>();
                var bids = await _context.Bids.AsNoTracking()
                    .Include("Invitation.SupplierProfile")
                    .ToListAsync(cancellationToken);

                foreach (var bid in bids)
                {
                    // إذا العرض لمورد آخر ضمن نفس العطاء، يسجل اسم المورد لمنع ذكره بملخص الفائز.
                    if (bid.TenderId == report.TenderId && bid.BidId != report.Bid.BidId)
                    {
                        excludedSuppliers.Add(bid.Invitation.SupplierProfile.CompanyName);
                    }
                }
                string selectionBasis;
                decimal? winningScore = null;

                // إذا في أربع مراجعات معتمدة مكتملة، يحسب متوسط علامة العرض الفائز.
                if (winnerMarks.Count == 4)
                {
                    selectionBasis = "Highest recorded committee score after every offer received four member reviews.";
                    decimal totalMarks = 0;
                    foreach (var evaluation in winnerMarks)
                    {
                        totalMarks += evaluation.FinancialMark!.Value
                            + evaluation.TechnicalMark!.Value;
                    }
                    winningScore = totalMarks / winnerMarks.Count;
                }
                else
                {
                    selectionBasis = "Recorded committee recommendation.";
                }

                // إرسال بيانات العرض الفائز لإنشاء مسودة الملخص
                var requestedItems = report.Tender.PurchaseRequest.PurchaseRequestItems.ToList();
                requestedItems.Sort(delegate (PurchaseRequestItem first, PurchaseRequestItem second)
                {
                    return first.PurchaseRequestItemId.CompareTo(second.PurchaseRequestItemId);
                });

                List<object> purchaseItems = new List<object>();
                foreach (var item in requestedItems)
                {
                    purchaseItems.Add(new
                    {
                        item.PurchaseRequestItemId,
                        item.ItemName,
                        item.Quantity,
                        item.Unit,
                        item.Specifications
                    });
                }

                List<object> offerItems = new List<object>();
                foreach (var item in report.Bid.BidItems)
                {
                    offerItems.Add(new
                    {
                        item.PurchaseRequestItemId,
                        item.Quantity,
                        item.UnitPrice,
                        item.OfferedSpecifications
                    });
                }

                var data = new
                {
                    tender = report.Tender.Title,
                    report.Tender.Description,
                    report.Tender.Terms,
                    offerRoundsAndException = SourcingTenderManagement.Services.Reports.OfferRoundContext.Describe(report.Tender),
                    purchaseRequest = new
                    {
                        report.Tender.PurchaseRequest.Title,
                        report.Tender.PurchaseRequest.Reason,
                        department = report.Tender.PurchaseRequest.Department.Name,
                        report.Tender.PurchaseRequest.Priority,
                        report.Tender.PurchaseRequest.TargetDate,
                        items = purchaseItems
                    },
                    outputScope = "Summarize only the first-ranked winning offer. The full top-three committee report is retained " +
                            "separately for reference, not summarized here.",
                    selectionBasis,
                    winningScore,
                    offer = new
                    {
                        report.Bid.BidId,
                        supplier = report.Bid.Invitation.SupplierProfile.CompanyName,
                        total = CommitteeEvaluationPage.Total(report.Bid),
                        report.Bid.Currency,
                        report.Bid.DeliveryDays,
                        report.Bid.PaymentTerms,
                        report.Bid.TechnicalDetails,
                        items = offerItems
                    }
                };

                var summary = await ai.DraftReferral(data, cancellationToken);

                string winningSupplier = report.Bid.Invitation.SupplierProfile.CompanyName;
                // إذا الملخص لا يذكر اسم المورد الفائز، يرفض نتيجة الذكاء الاصطناعي.
                if (!summary.Contains(winningSupplier, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The manager summary must cover only the winning supplier.");
                }

                foreach (string supplierName in excludedSuppliers)
                {
                    // إذا الاسم هو اسم المورد الفائز نفسه، يتخطاه أثناء فحص أسماء الموردين الآخرين.
                    if (string.Equals(supplierName, winningSupplier, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // إذا الملخص يذكر اسم مورد آخر، يرفضه حتى يبقى الملخص خاصاً بالفائز.
                    if (summary.Contains(supplierName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The manager summary must cover only the winning supplier.");
                    }
                }

                return Json(new { summary });
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException or InvalidOperationException or KeyNotFoundException)
            {
                return StatusCode(503, new
                {
                    error = "Could not generate the summary. Check your vLLM / AI endpoint or API key and try again."
                });
            }
        }

        // POST /SourcingManager/Forward: Forward. Runs when its form or button is submitted.
        [HttpPost]
        public IActionResult Forward(ManagerForwardInput input)
        {
            string? reason = input.Reason?.Trim();
            // إذا بيانات الإحالة غير صحيحة أو سببها فارغ، يمنع إرسال العطاء.
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason))
            {
                TempData["ManagerMessage"] = "Enter the policy reason for referral.";
                return RedirectToAction("Submissions", new { id = input.ReportId });
            }

            // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

            var evaluationReport = _context.EvaluationReports.Find(input.ReportId);

            // إذا التقرير غير موجود أو غير معتمد من المانجر، يمنع الإحالة.
            if (evaluationReport == null || evaluationReport.Status != ManagerApproved)
            {
                TempData["ManagerMessage"] = "Only an approved recommendation awaiting referral can be forwarded.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            var tender = _context.Tenders.Find(evaluationReport.TenderId);

            // إذا العطاء المرتبط بالتقرير غير موجود، يمنع الإحالة.
            if (tender == null)
            {
                TempData["ManagerMessage"] = "Only an approved recommendation awaiting referral can be forwarded.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            foreach (var approval in _context.TenderApprovals)
            {
                // إذا اللجنة العليا رفضت نفس العطاء، يمنع إحالته من هذا المسار.
                if (approval.TenderId == tender.TenderId
                    && approval.Stage == "Top"
                    && approval.Decision == "Rejected")
                {
                    TempData["ManagerMessage"] = "Only an approved recommendation awaiting referral can be forwarded.";
                    return RedirectToAction("Submissions", new { id = (int?)null });
                }
            }

            // إذا العطاء لم يعد بمرحلة مراجعة المانجر، يمنع إحالته مجدداً.
            if (tender.Status != AwaitingManagerReview)
            {
                TempData["ManagerMessage"] = "Only an approved recommendation awaiting referral can be forwarded.";
                return RedirectToAction("Submissions", new { id = (int?)null });
            }

            List<CommitteeAssignment> assignments = new List<CommitteeAssignment>();
            foreach (var assignment in _context.CommitteeAssignments)
            {
                // إذا التعيين يخص اللجنة العليا لنفس العطاء، يضيفه لقائمة أعضاء الإحالة.
                if (assignment.TenderId == tender.TenderId && assignment.CommitteeType == "Top")
                {
                    assignments.Add(assignment);
                }
            }

            var roles = _context.Roles.ToList();
            var memberships = _context.UserRoles.ToList();
            var users = _context.Users.ToList();

            // إذا ما في أعضاء لجنة عليا معيّنين للعطاء، يبدأ بتعيين الأعضاء الأربعة.
            if (assignments.Count == 0)
            {
                List<string> memberIds = new List<string>();

                foreach (string positionRole in RoleNames.TopPositions)
                {
                    List<string> candidates = new List<string>();

                    foreach (var user in users)
                    {
                        // إذا حساب المستخدم غير فعّال، يستبعده من المرشحين للجنة.
                        if (!user.IsActive)
                        {
                            continue;
                        }

                        bool hasPositionRole = false;
                        bool hasCommitteeRole = false;

                        foreach (var membership in memberships)
                        {
                            // إذا العضوية تخص مستخدماً آخر، يتخطاها.
                            if (membership.UserId != user.Id)
                            {
                                continue;
                            }

                            foreach (var role in roles)
                            {
                                // إذا الصلاحية لا تطابق صلاحية العضوية الحالية، يتخطاها.
                                if (role.Id != membership.RoleId)
                                {
                                    continue;
                                }

                                // إذا الصلاحية تطابق منصب اللجنة المطلوب، يسجل توفر صلاحية المنصب.
                                if (role.Name == positionRole)
                                {
                                    hasPositionRole = true;
                                }
                                // إذا الصلاحية هي صلاحية اللجنة العليا، يسجل توفر عضوية اللجنة.
                                if (role.Name == RoleNames.TopCommittee)
                                {
                                    hasCommitteeRole = true;
                                }
                            }
                        }

                        // إذا المستخدم يحمل صلاحية المنصب وعضوية اللجنة معاً، يضيفه للمرشحين.
                        if (hasPositionRole && hasCommitteeRole)
                        {
                            candidates.Add(user.Id);
                        }
                    }

                    // إذا عدد الحسابات المؤهلة للمنصب ليس واحداً، يطلب من الأدمن تصحيح الحسابات.
                    if (candidates.Count != 1)
                    {
                        TempData["ManagerMessage"] = $"Admin must provide exactly one active CEO & Chiefs account for {positionRole}.";
                        return RedirectToAction("Submissions", new { id = input.ReportId });
                    }

                    memberIds.Add(candidates[0]);
                }

                HashSet<string> differentMembers = new HashSet<string>(memberIds);
                // إذا الحسابات الأربعة ليست لأشخاص مختلفين، يمنع تعيين اللجنة.
                if (differentMembers.Count != 4)
                {
                    TempData["ManagerMessage"] = "Four different CEO & Chiefs accounts are required.";
                    return RedirectToAction("Submissions", new { id = input.ReportId });
                }

                for (int position = 1; position <= 4; position++)
                {
                    _context.CommitteeAssignments.Add(new CommitteeAssignment
                    {
                        TenderId = tender.TenderId,
                        UserId = memberIds[position - 1],
                        CommitteeType = "Top",
                        Position = position
                    });
                }

                _context.SaveChanges();
            }
            else
            {
                HashSet<string> differentMembers = new HashSet<string>();

                foreach (var assignment in assignments)
                {
                    differentMembers.Add(assignment.UserId);

                    bool eligibleMember = false;
                    foreach (var user in users)
                    {
                        // إذا الحساب لا يخص العضو المعيّن أو غير فعّال، يتخطاه أثناء التحقق من الأهلية.
                        if (user.Id != assignment.UserId || !user.IsActive)
                        {
                            continue;
                        }

                        foreach (var membership in memberships)
                        {
                            // إذا العضوية تخص مستخدماً آخر، يتخطاها أثناء فحص العضو المعيّن.
                            if (membership.UserId != user.Id)
                            {
                                continue;
                            }

                            foreach (var role in roles)
                            {
                                // إذا العضوية تمنح صلاحية اللجنة العليا، يسجل أن العضو المعيّن مؤهل.
                                if (role.Id == membership.RoleId
                                    && role.Name == RoleNames.TopCommittee)
                                {
                                    eligibleMember = true;
                                }
                            }
                        }
                    }

                    // إذا العضو المعيّن غير مؤهل، يمنع إحالة العطاء.
                    if (!eligibleMember)
                    {
                        TempData["ManagerMessage"] = "The tender needs four distinct, active CEO & Chiefs members before referral.";
                        return RedirectToAction("Submissions", new { id = input.ReportId });
                    }
                }

                // إذا التعيينات ليست لأربعة أعضاء مختلفين، يمنع الإحالة.
                if (assignments.Count != 4 || differentMembers.Count != 4)
                {
                    TempData["ManagerMessage"] = "The tender needs four distinct, active CEO & Chiefs members before referral.";
                    return RedirectToAction("Submissions", new { id = input.ReportId });
                }
            }

            // إذا في استفسارات مفتوحة، يمنع الإحالة حتى يتم حلها من الطرفين.
            if (HasOpenClarifications(tender.TenderId))
            {
                TempData["ManagerMessage"] = "Both sides must resolve open clarifications before referral.";
                return RedirectToAction("Submissions", new { id = input.ReportId });
            }
            // تحميل طلب الشراء والعرض المرشح للإحالة
            _context.Entry(tender).Reference<PurchaseRequest>("PurchaseRequest").Query()
                .Include("Department")
                .Include("PurchaseRequestItems")
                .Load();

            var bids = _context.Bids
                .Include("Invitation.SupplierProfile")
                .Include("BidItems")
                .ToList();

            Bid? winningOffer = null;
            foreach (var bid in bids)
            {
                // إذا العرض يطابق العطاء ورقم العرض المرشح، يختاره للإحالة.
                if (bid.TenderId == tender.TenderId
                    && bid.BidId == evaluationReport.RecommendedBidId)
                {
                    // إذا وجد أكثر من عرض مطابق للعرض المرشح، يوقف العملية بسبب تكرار البيانات.
                    if (winningOffer != null)
                    {
                        throw new InvalidOperationException("Multiple nominated offers were found.");
                    }
                    winningOffer = bid;
                }
            }
            // إذا العرض المرشح غير موجود، يمنع الإحالة ويطلب تصحيح التقرير.
            if (winningOffer == null)
            {
                TempData["ManagerMessage"] = "The recommended offer is missing. Return this report for correction.";
                return RedirectToAction("Submissions", new { id = input.ReportId });
            }
            // تجهيز نص الإحالة وتحديث حالة العطاء
            tender.Status = AwaitingCommitteeDecision;
            // إذا ملخص المانجر فارغ، يبدأه بسبب الإحالة؛ وإلا يضيف السبب للملخص الموجود.
            if (string.IsNullOrWhiteSpace(evaluationReport.ManagerSummary))
            {
                evaluationReport.ManagerSummary = $"Referral reason: {reason}";
            }
            else
            {
                evaluationReport.ManagerSummary += $"\n\nReferral reason: {reason}";
            }
            _context.Entry(tender).Collection("OfferExtensions").Load();
            evaluationReport.ManagerSummary = SourcingTenderManagement.Services.Reports.ManagerReferralContext.Describe(tender, winningOffer)
                + "\n\n## Manager assessment\n" + evaluationReport.ManagerSummary;

            NotifyAssignedCommittee(tender.TenderId, "Top", "Tender awaiting committee decision",
                $"Tender #{tender.TenderId} has been referred for final decision.", "TopCommittee", "Decisions");

            // حفظ كل التغييرات وتأكيد المعاملة
            _context.SaveChanges();
            transaction.Commit();
            _logger.LogInformation("Manager {ManagerId} referred tender {TenderId} to the CEO & Chiefs.", ManagerId, tender.TenderId);

            TempData["ManagerMessage"] = "Tender forwarded to the CEO & Chiefs.";
            return RedirectToAction("Submissions", new { id = (int?)null });
        }

        // Chosen Offers
        // GET /SourcingManager/ChosenOffers: Shows the "Chosen Offers" page.
        [HttpGet]
        public IActionResult ChosenOffers()
        {
            // تحميل العطاءات التي تمت ترسيتها مع المورد الفائز
            var databaseTenders = _context.Tenders.AsNoTracking()
                .Include("EvaluationReport")
                .Include("Bid.Invitation.SupplierProfile")
                .ToList();

            List<Tender> chosenOffers = new List<Tender>();
            foreach (var tender in databaseTenders)
            {
                // إذا العطاء تمت ترسيته وعنده تقرير تقييم، يضيفه لقائمة العروض الفائزة.
                if (tender.Status == "Awarded" && tender.EvaluationReport != null)
                {
                    chosenOffers.Add(tender);
                }
            }

            chosenOffers.Sort(delegate (Tender first, Tender second)
            {
                return Nullable.Compare(second.AwardedAt, first.AwardedAt);
            });
            ViewData.Model = chosenOffers;
            return View();
        }

        // Reports
        // GET /SourcingManager/Reports: Shows the "Reports & decisions" page.
        [HttpGet]
        public IActionResult Reports(string? search, string? decision)
        {
            // تحميل قرارات المانجر حسب البحث والفلتر
            var managerDecisions = SourcingTenderManagement.Services.Reports.Data
                .ApprovalsReport.ManagerDecisions(_context, search, decision, ManagerId)
                .Include("User")
                .Include("Tender")
                .ToList();

            managerDecisions.Sort(delegate (TenderApproval first, TenderApproval second)
            {
                return second.DecidedAt.CompareTo(first.DecidedAt);
            });

            var page = new ManagerReportPage(managerDecisions, search, decision);
            ViewData.Model = page;
            return View();
        }

        private bool HasOpenClarifications(int tenderId)
        {
            var questions = _context.Clarifications.Include("Bid").ToList();
            foreach (var question in questions)
            {
                // إذا السؤال محلول، يتخطاه أثناء فحص الاستفسارات المفتوحة.
                if (question.Status == "Resolved")
                {
                    continue;
                }
                // إذا السؤال المفتوح مرتبط بالعطاء مباشرة أو بأحد عروضه، يرجع أن العطاء عنده استفسار مفتوح.
                if (question.TenderId == tenderId
                    || (question.Bid != null && question.Bid.TenderId == tenderId))
                {
                    return true;
                }
            }

            return false;
        }

        private void NotifyAssignedCommittee(
            int tenderId, string committeeType, string title, string message, string controller, string action)
        {
            var assignments = _context.CommitteeAssignments
                .Include("User")
                .ToList();

            foreach (var assignment in assignments)
            {
                // إذا التعيين لنفس العطاء واللجنة ولعضو فعّال غير المانجر، يرسل له إشعاراً.
                if (assignment.TenderId == tenderId
                    && assignment.CommitteeType == committeeType
                    && assignment.UserId != ManagerId
                    && assignment.User.IsActive)
                {
                    Notify(assignment.UserId, title, message, controller, action);
                }
            }
        }

        private void Notify(string recipientId, string title, string message, string controller, string action)
        {
            var notification = new Notification
            {
                UserId = recipientId,
                Title = title,
                Message = message,
                RelativeUrl = Url.Action(action, controller),
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
        }
    }
}
