using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers
{
    // Sourcing Manager handling of tenders the CEO & Chiefs rejected or asked to edit.
    public partial class SourcingManagerController
    {
        // Officer Submissions
        private List<Tender> ManagedRejections()
        {
            var tenders = _context.Tenders.AsNoTracking()
                .Include("TenderApprovals.User")
                .ToList();

            List<Tender> rejections = new List<Tender>();

            foreach (var tender in tenders)
            {
                bool rejectedByCommittee = false;
                bool approvedByManager = false;

                foreach (var approval in tender.TenderApprovals)
                {
                    // إذا في قرار رفض من اللجنة العليا، يسجل أن العطاء مرفوض.
                    if (approval.Stage == "Top" && approval.Decision == "Rejected")
                    {
                        rejectedByCommittee = true;
                    }
                    // إذا المانجر الحالي وافق سابقاً على العطاء، يسجل ارتباطه بهذا المانجر.
                    if (approval.Stage == "Manager"
                        && approval.UserId == ManagerId
                        && approval.Decision == "Approved")
                    {
                        approvedByManager = true;
                    }
                }

                // إذا العطاء مرفوض من اللجنة العليا ومعتمد سابقاً من المانجر الحالي، يضيفه لقائمة الرفض.
                if (rejectedByCommittee && approvedByManager)
                {
                    rejections.Add(tender);
                }
            }

            return rejections;
        }

        private List<Tender> ManagedTopEditRequests()
        {
            var tenders = _context.Tenders.AsNoTracking()
                .AsSplitQuery()
                .Include("EvaluationReport.Bid.Invitation.SupplierProfile")
                .Include("EvaluationReport.Bid.BidItems")
                .Include("TenderApprovals")
                .ToList();

            List<Tender> editRequests = new List<Tender>();
            foreach (var tender in tenders)
            {
                // إذا العطاء ما عنده طلب تعديل من اللجنة العليا، يتخطاه.
                if (tender.TopEditState == null)
                {
                    continue;
                }

                foreach (var approval in tender.TenderApprovals)
                {
                    // إذا المانجر الحالي وافق سابقاً على العطاء المطلوب تعديله، يضيفه لقائمة طلبات التعديل.
                    if (approval.Stage == "Manager"
                        && approval.UserId == ManagerId
                        && approval.Decision == "Approved")
                    {
                        editRequests.Add(tender);
                        break;
                    }
                }
            }

            return editRequests;
        }

        // Keep links from existing notifications working without a standalone rejection page.
        [HttpGet]
        public IActionResult Rejections(int? id)
        {
            return RedirectToAction(
                nameof(Submissions),
                new { rejectedTenderId = id, showRejections = true });
        }
        // POST /SourcingManager/ForwardRejection: Forward rejection. Runs when its form or button is submitted.
        [HttpPost]
        public async Task<IActionResult> ForwardRejection(int id, [FromServices] ITopCommitteeTenderTransaction coordinator, CancellationToken ct)
        {
            var outcome = await coordinator.ExecuteAsync(id, async delegate (ApplicationDbContext db, CancellationToken token)
            {
                // التحقق من حساب المانجر وصلاحيته على العطاء
                var access = await Services.TopCommittee.TopCommitteeClarificationStore.LoadAsync(db, ManagerId, id, token);
                var tender = access.Tender;
                bool assignedManager = false;
                foreach (var manager in access.Managers)
                {
                    // إذا الحساب هو المانجر الحالي وفعّال ويحمل صلاحية المانجر، يعتبره مانجراً مرتبطاً بالعطاء.
                    if (manager.UserId == ManagerId && manager.IsActive && manager.HasManagerRole)
                    {
                        assignedManager = true;
                        break;
                    }
                }

                // إذا الحساب غير مؤهل أو العطاء غير موجود أو المانجر غير مرتبط به، يمنع تحويل الرفض.
                if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.SourcingManager)
                    || tender == null || !assignedManager)
                {
                    return 403;
                }
                bool committeeRejected = false;
                foreach (var approval in tender.TenderApprovals)
                {
                    // إذا وجد قرار رفض من اللجنة العليا، يسجل توفر الرفض المطلوب تحويله.
                    if (approval.Stage == "Top" && approval.Decision == "Rejected")
                    {
                        committeeRejected = true;
                        break;
                    }
                }

                // إذا العطاء ليس بانتظار المانجر أو تم تحويل رفضه سابقاً أو لا يوجد رفض لجنة، يمنع التحويل.
                if (tender.Status != "AwaitingManagerReview"
                    || tender.RejectionForwardedAt.HasValue
                    || !committeeRejected)
                {
                    return 409;
                }
                bool officerActive = false;
                var officer = await db.Users.FindAsync(new object[] { tender.CreatedByUserId }, token);

                // إذا موظف التوريد المسؤول موجود وحسابه فعّال، يبدأ التحقق من صلاحيته.
                if (officer != null && officer.IsActive)
                {
                    var memberships = await db.UserRoles.ToListAsync(token);
                    var roles = await db.Roles.ToListAsync(token);
                    foreach (var membership in memberships)
                    {
                        // إذا العضوية لا تخص الموظف المسؤول، يتخطاها.
                        if (membership.UserId != officer.Id)
                        {
                            continue;
                        }
                        foreach (var role in roles)
                        {
                            // إذا العضوية تمنح صلاحية موظف التوريد، يسجل أن الموظف مؤهل لاستلام المعاملة.
                            if (role.Id == membership.RoleId && role.Name == RoleNames.SourcingOfficer)
                            {
                                officerActive = true;
                            }
                        }
                    }
                }
                // إذا الموظف المسؤول غير فعّال أو لا يحمل صلاحيته، يمنع تحويل الرفض إليه.
                if (!officerActive)
                {
                    return 409;
                }
                // تسجيل تحويل الرفض وإشعار موظف التوريد
                tender.RejectionForwardedAt = DateTime.UtcNow;
                tender.RejectionForwardedByUserId = ManagerId;
                tender.Status = "Closed";
                db.Notifications.Add(new Notification
                {
                    UserId = tender.CreatedByUserId,
                    Title = "Rejected tender forwarded to you",
                    Message = $"Review tender #{id} and prepare a replacement tender. The original rejection is preserved.",
                    RelativeUrl = $"/SourcingOfficer/Offers?tenderId={id}",
                    CreatedAt = DateTime.UtcNow
                });
                // حفظ التغييرات داخل معاملة العطاء
                await db.SaveChangesAsync(token);
                return 200;
            }, ct);
            // إذا نتيجة المعاملة تدل على عدم السماح، يرجع منع الوصول.
            if (outcome == 403)
            {
                return Forbid();
            }
            // إذا حالة العطاء لا تسمح بالتحويل، يرجع رسالة تعارض توضح السبب.
            if (outcome == 409)
            {
                return Conflict("This rejection is unavailable, already forwarded, or its responsible officer is inactive.");
            }
            return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
        }

        // POST /SourcingManager/AnswerTopEditRequest: Answer top edit request. Runs when its form or button is submitted.
        [HttpPost]
        public async Task<IActionResult> AnswerTopEditRequest(
            int id,
            string? answerNotes,
            [FromServices] ITopCommitteeTenderTransaction coordinator,
            CancellationToken ct)
        {
            var trimmed = answerNotes?.Trim();
            // إذا جواب المانجر فارغ أو أكثر من 4000 حرف، يمنع إرسال الرد.
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            {
                TempData["ManagerMessage"] = "Enter your answer (1–4,000 characters) before returning the tender to the CEO & Chiefs.";
                return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
            }

            var (status, errorMsg) = await coordinator.ExecuteAsync(id, async delegate (ApplicationDbContext db, CancellationToken token)
            {
                // التحقق من حساب المانجر وصلاحيته على العطاء
                var access = await Services.TopCommittee.TopCommitteeClarificationStore.LoadAsync(db, ManagerId, id, token);
                var tender = access.Tender;
                bool assignedManager = false;
                foreach (var manager in access.Managers)
                {
                    // إذا الحساب هو المانجر الحالي وفعّال ويحمل الصلاحية، يعتبره مرتبطاً بالعطاء.
                    if (manager.UserId == ManagerId && manager.IsActive && manager.HasManagerRole)
                    {
                        assignedManager = true;
                        break;
                    }
                }

                // إذا المانجر غير مؤهل أو غير مرتبط بالعطاء أو العطاء غير موجود، يمنع حفظ الجواب.
                if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.SourcingManager)
                    || tender == null || !assignedManager)
                {
                    return (403, null);
                }
                // إذا العطاء أو طلب التعديل ليس بانتظار جواب المانجر، يمنع إرجاعه للجنة.
                if (tender.Status != "AwaitingManagerReview"
                    || tender.TopEditState is not ("WithManager" or "OfficerAnswered"))
                {
                    return (409, "This tender is not currently awaiting your edit response.");
                }
                bool hasOpenClarifications = false;
                var questions = await db.Clarifications.Include("Bid").ToListAsync(token);
                foreach (var question in questions)
                {
                    // إذا السؤال غير محلول ومرتبط بنفس العطاء أو أحد عروضه، يسجل وجود استفسار مفتوح.
                    if (question.Status != "Resolved"
                        && (question.TenderId == id
                            || (question.Bid != null && question.Bid.TenderId == id)))
                    {
                        hasOpenClarifications = true;
                        break;
                    }
                }
                // إذا في استفسارات مفتوحة، يمنع إرسال الجواب للجنة حتى يتم حلها.
                if (hasOpenClarifications)
                {
                    return (400, "Resolve open clarifications before returning the tender to the CEO & Chiefs.");
                }
                EvaluationReport? report = null;
                var reports = await db.EvaluationReports.ToListAsync(token);
                foreach (var savedReport in reports)
                {
                    // إذا التقرير يخص نفس العطاء، يختاره لتجهيز جواب المانجر.
                    if (savedReport.TenderId == id)
                    {
                        // إذا وجد تقريراً مطابقاً سابقاً، يوقف العملية بسبب وجود أكثر من تقرير للعطاء.
                        if (report != null)
                        {
                            throw new InvalidOperationException("Multiple reports were found for the tender.");
                        }
                        report = savedReport;
                    }
                }
                // إذا التقرير غير موجود أو بدون عرض مرشح، يمنع إعادة العطاء للجنة.
                if (report == null || !report.RecommendedBidId.HasValue)
                {
                    return (400, "An eligible recommended offer is required before returning to the CEO & Chiefs.");
                }
                Bid? winningOffer = null;
                var bids = await db.Bids.Include("Invitation.SupplierProfile")
                    .Include("BidItems")
                    .ToListAsync(token);

                foreach (var bid in bids)
                {
                    // إذا العرض المرشح مقدم ومطابق ومؤهل وتابع للعطاء، يختاره للجواب.
                    if (bid.TenderId == id
                        && bid.BidId == report.RecommendedBidId.Value
                        && bid.SubmittedAt != null
                        && bid.ComplianceStatus == "Compliant"
                        && bid.Status != "Draft"
                        && bid.Status != "Withdrawn"
                        && bid.Status != "Rejected"
                        && bid.Status != "NonCompliant")
                    {
                        // إذا وجد أكثر من عرض مطابق للمرشح، يوقف العملية بسبب التكرار.
                        if (winningOffer != null)
                        {
                            throw new InvalidOperationException("Multiple nominated offers were found.");
                        }
                        winningOffer = bid;
                    }
                }
                // إذا العرض المرشح لم يعد مؤهلاً، يطلب إعادة المعاملة لموظف التوريد.
                if (winningOffer == null)
                {
                    return (400, "The nominated offer is no longer eligible. Push back to the Sourcing Officer to select a compliant offer.");
                }
                var topMembers = access.Members.ToList();
                for (int index = topMembers.Count - 1; index >= 0; index--)
                {
                    // إذا عضو اللجنة غير مؤهل، يستبعده من قائمة مستلمي الرد.
                    if (!topMembers[index].IsEligible)
                    {
                        topMembers.RemoveAt(index);
                    }
                }
                // إذا ما في أربعة أعضاء مؤهلين للجنة العليا، يمنع إعادة العطاء إليها.
                if (topMembers.Count != 4)
                {
                    return (400, "Four active assigned CEO & Chiefs members are required before returning the tender.");
                }
                await db.Entry(tender).Reference<PurchaseRequest>("PurchaseRequest").Query()
                    .Include("Department")
                    .Include("PurchaseRequestItems")
                    .LoadAsync(token);
                await db.Entry(tender).Collection("OfferExtensions").LoadAsync(token);

                List<TenderApproval> existingTopVotes = new List<TenderApproval>();
                foreach (var approval in tender.TenderApprovals)
                {
                    // إذا التصويت السابق للجنة العليا ليس رفضاً نهائياً، يجهزه للإزالة لإعادة التصويت.
                    if (approval.Stage == "Top" && approval.Decision != "Rejected")
                    {
                        existingTopVotes.Add(approval);
                    }
                }
                // إذا في تصويتات سابقة قابلة للإعادة، يزيلها ضمن معاملة حفظ الجواب.
                if (existingTopVotes.Count > 0)
                {
                    db.TenderApprovals.RemoveRange(existingTopVotes);
                }
                string cycleLog = $"CEO & Chiefs requested edit: {tender.TopEditRequestNotes}\n";
                // إذا الموظف أضاف جواباً على طلب التعديل، يضمّه لسجل رد المانجر.
                if (!string.IsNullOrWhiteSpace(tender.TopEditOfficerNotes))
                {
                    cycleLog += $"Sourcing Officer update: {tender.TopEditOfficerNotes}\n";
                }
                cycleLog += $"Sourcing Manager answer: {trimmed}";

                report.ManagerSummary = Services.Reports.ManagerReferralContext.Describe(tender, winningOffer)
                    + "\n\n## Manager assessment & edit response\n" + cycleLog;
                report.Status = ManagerApproved;

                TenderApproval? managerApproval = null;
                foreach (var approval in tender.TenderApprovals)
                {
                    // إذا القرار يخص المانجر الحالي، يختاره لتحديث موافقته بعد الجواب.
                    if (approval.Stage == "Manager" && approval.UserId == ManagerId)
                    {
                        // إذا وجد أكثر من موافقة لنفس المانجر، يوقف العملية بسبب تكرار البيانات.
                        if (managerApproval != null)
                        {
                            throw new InvalidOperationException("Multiple manager approvals were found.");
                        }
                        managerApproval = approval;
                    }
                }
                // إذا موافقة المانجر موجودة، يحدثها مع الجواب ووقت الاعتماد.
                if (managerApproval != null)
                {
                    managerApproval.Decision = "Approved";
                    managerApproval.Notes = trimmed;
                    managerApproval.DecidedAt = DateTime.UtcNow;
                }

                tender.TopEditManagerNotes = trimmed;
                tender.TopEditState = null;
                tender.Status = AwaitingCommitteeDecision;

                foreach (var member in topMembers)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = member.UserId,
                        Title = "Edits answered — tender ready for decision",
                        Message = $"Sourcing Manager answered the edit request on Tender #{id} (nominated offer #{winningOffer.BidId} " +
                                $"· {winningOffer.Invitation.SupplierProfile.CompanyName}). Please record your decision.",
                        RelativeUrl = $"/TopCommittee/TenderDetails/{id}",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // حفظ التغييرات داخل معاملة العطاء
                await db.SaveChangesAsync(token);
                return (200, (string?)null);
            }, ct);

            // إذا نتيجة العملية منع وصول، يرجع منع الوصول للمانجر.
            if (status == 403)
            {
                return Forbid();
            }
            // إذا المعاملة لم تنجح، يعرض رسالة السبب ويرجع لطلب التعديل.
            if (status != 200)
            {
                TempData["ManagerMessage"] = errorMsg ?? "Could not return this tender right now.";
                return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
            }

            TempData["ManagerMessage"] = $"Answered and returned Tender #{id} to the CEO & Chiefs for voting.";
            return RedirectToAction(nameof(Submissions));
        }

        // POST /SourcingManager/PushTopEditToOfficer: Push top edit to officer. Runs when its form or button is submitted.
        [HttpPost]
        public async Task<IActionResult> PushTopEditToOfficer(
            int id,
            string? managerNotes,
            [FromServices] ITopCommitteeTenderTransaction coordinator,
            CancellationToken ct)
        {
            var trimmed = managerNotes?.Trim();
            // إذا تعليمات المانجر فارغة أو أكثر من 4000 حرف، يمنع إعادة الطلب للموظف.
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            {
                TempData["ManagerMessage"] = "Enter instructions (1–4,000 characters) before pushing back to the Sourcing Officer.";
                return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
            }

            var (status, errorMsg) = await coordinator.ExecuteAsync(id, async delegate (ApplicationDbContext db, CancellationToken token)
            {
                // التحقق من حساب المانجر وصلاحيته على العطاء
                var access = await Services.TopCommittee.TopCommitteeClarificationStore.LoadAsync(db, ManagerId, id, token);
                var tender = access.Tender;
                bool assignedManager = false;
                foreach (var manager in access.Managers)
                {
                    // إذا الحساب هو المانجر الحالي وفعّال ويحمل الصلاحية، يعتبره مرتبطاً بالعطاء.
                    if (manager.UserId == ManagerId && manager.IsActive && manager.HasManagerRole)
                    {
                        assignedManager = true;
                        break;
                    }
                }

                // إذا المانجر غير مؤهل أو غير مرتبط بالعطاء أو العطاء غير موجود، يمنع الإعادة للموظف.
                if (!access.ActorIsActive || !access.ActorRoles.Contains(RoleNames.SourcingManager)
                    || tender == null || !assignedManager)
                {
                    return (403, null);
                }
                // إذا حالة العطاء أو طلب التعديل لا تسمح بتدخل المانجر، يمنع إرجاعه للموظف.
                if (tender.Status != "AwaitingManagerReview"
                    || tender.TopEditState is not ("WithManager" or "OfficerAnswered"))
                {
                    return (409, "This edit request is not currently awaiting your action.");
                }
                bool officerActive = false;
                var officer = await db.Users.FindAsync(new object[] { tender.CreatedByUserId }, token);

                // إذا الموظف المسؤول موجود وحسابه فعّال، يفحص صلاحياته لاستلام الطلب.
                if (officer != null && officer.IsActive)
                {
                    var memberships = await db.UserRoles.ToListAsync(token);
                    var roles = await db.Roles.ToListAsync(token);
                    foreach (var membership in memberships)
                    {
                        // إذا العضوية لا تخص الموظف المسؤول، يتخطاها.
                        if (membership.UserId != officer.Id)
                        {
                            continue;
                        }
                        foreach (var role in roles)
                        {
                            // إذا العضوية تمنح صلاحية موظف التوريد، يعتبر الموظف مؤهلاً لاستلام الطلب.
                            if (role.Id == membership.RoleId && role.Name == RoleNames.SourcingOfficer)
                            {
                                officerActive = true;
                            }
                        }
                    }
                }
                // إذا الموظف المسؤول غير فعّال أو بدون الصلاحية المطلوبة، يمنع إعادة المعاملة إليه.
                if (!officerActive)
                {
                    return (409, "The responsible Sourcing Officer account is inactive.");
                }
                EvaluationReport? report = null;
                var reports = await db.EvaluationReports.ToListAsync(token);
                foreach (var savedReport in reports)
                {
                    // إذا التقرير يخص نفس العطاء، يختاره لتحديث حالته.
                    if (savedReport.TenderId == id)
                    {
                        // إذا وجد تقريراً مطابقاً سابقاً، يوقف العملية بسبب تكرار تقارير العطاء.
                        if (report != null)
                        {
                            throw new InvalidOperationException("Multiple reports were found for the tender.");
                        }
                        report = savedReport;
                    }
                }
                // إذا التقرير موجود، يغيّر حالته إلى معاد للمراجعة ضمن نفس المعاملة.
                if (report != null)
                {
                    report.Status = ReturnedForReview;
                }
                tender.TopEditManagerNotes = trimmed;
                tender.TopEditState = "WithOfficer";
                tender.Status = "AwaitingOfficerSummary";

                db.Notifications.Add(new Notification
                {
                    UserId = tender.CreatedByUserId,
                    Title = "CEO & Chiefs edit request pushed to you",
                    Message = $"Tender #{id}: Sourcing Manager forwarded an edit request from CEO & Chiefs. Answer and return " +
                            $"to Manager, or choose another compliant offer instead.",
                    RelativeUrl = $"/SourcingOfficer/Offers?tenderId={id}",
                    CreatedAt = DateTime.UtcNow
                });

                // حفظ التغييرات داخل معاملة العطاء
                await db.SaveChangesAsync(token);
                return (200, (string?)null);
            }, ct);

            // إذا نتيجة العملية منع وصول، يرجع منع الوصول للمانجر.
            if (status == 403)
            {
                return Forbid();
            }
            // إذا المعاملة لم تنجح، يعرض السبب ويرجع لصفحة طلب التعديل.
            if (status != 200)
            {
                TempData["ManagerMessage"] = errorMsg ?? "Could not push this edit request to the Sourcing Officer.";
                return RedirectToAction(nameof(Submissions), new { rejectedTenderId = id, showRejections = true });
            }

            TempData["ManagerMessage"] = $"Pushed Tender #{id} back to the Sourcing Officer to answer or select another offer.";
            return RedirectToAction(nameof(Submissions));
        }
    }
}
