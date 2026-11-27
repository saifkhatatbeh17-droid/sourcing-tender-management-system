using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers
{
    // Sourcing Manager clarifications: ask, reply to and resolve questions about a submission.
    public partial class SourcingManagerController
    {
        // Clarifications
        // GET /SourcingManager/Clarifications: Shows the "Clarifications" page.
        [HttpGet]
        public IActionResult Clarifications(int? tenderId, int? id)
        {
            // التحقق من صلاحية عرض المحادثة المطلوبة
            Clarification? selected = null;
            // إذا وصل رقم محادثة، يحمّل المحادثة المطلوبة.
            if (id.HasValue)
            {
                selected = _context.Clarifications.Find(id.Value);

                // إذا المحادثة غير موجودة أو المانجر مش طرف فيها، يمنع عرضها.
                if (selected == null || (selected.SenderUserId != ManagerId && selected.RecipientUserId != ManagerId))
                {
                    return NotFound();
                }
            }

            // تحميل المحادثات المرتبطة بالمانجر
            var questions = _context.Clarifications
                .Include("SenderUser")
                .Include("RecipientUser")
                .Include("Tender")
                .Include("Bid.Invitation.Tender")
                .Include("PurchaseRequest")
                .Include("ClarificationResponses.RespondedByUser")
                .ToList();

            List<Clarification> conversations = new List<Clarification>();

            foreach (var question in questions)
            {
                // إذا السؤال مش مرسل من المانجر ولا موجّه إله، يتخطاه.
                if (question.SenderUserId != ManagerId && question.RecipientUserId != ManagerId)
                {
                    continue;
                }

                var responses = question.ClarificationResponses.ToList();
                responses.Sort(delegate (ClarificationResponse first, ClarificationResponse second)
                {
                    return first.CreatedAt.CompareTo(second.CreatedAt);
                });
                question.ClarificationResponses = responses;
                conversations.Add(question);
            }

            conversations.Sort(delegate (Clarification first, Clarification second)
            {
                return second.CreatedAt.CompareTo(first.CreatedAt);
            });
            // إذا المحادثة المختارة مش ضمن المحادثات المسموحة، يمنع عرضها.
            if (selected != null && !conversations.Contains(selected))
            {
                return NotFound();
            }

            // تحميل العطاءات والأعضاء المتاح مراسلتهم
            var committeeAssignments = EligibleClarificationRecipients();
            committeeAssignments.Sort(delegate (CommitteeAssignment first, CommitteeAssignment second)
            {
                int titleOrder = string.Compare(first.Tender.Title, second.Tender.Title);
                // إذا أسماء العطاءات مختلفة، يرتّبها حسب الاسم قبل ترتيب أعضاء اللجنة.
                if (titleOrder != 0)
                {
                    return titleOrder;
                }
                return first.Position.CompareTo(second.Position);
            });

            List<Tender> tenders = new List<Tender>();
            HashSet<int> tenderIds = new HashSet<int>();

            foreach (var assignment in committeeAssignments)
            {
                // إذا رقم العطاء انضاف لأول مرة، يضيف العطاء للقائمة بدون تكرار.
                if (tenderIds.Add(assignment.TenderId))
                {
                    tenders.Add(assignment.Tender);
                }
            }

            var selectedTenderId = tenderId;
            // إذا ما تم اختيار عطاء وفي عطاءات متاحة، يختار أول عطاء.
            if (!selectedTenderId.HasValue && tenders.Count > 0)
            {
                selectedTenderId = tenders[0].TenderId;
            }

            List<CommitteeAssignment> recipients = new List<CommitteeAssignment>();
            foreach (var assignment in committeeAssignments)
            {
                // إذا تعيين العضو تابع للعطاء المختار، يضيفه لقائمة المستلمين.
                if (assignment.TenderId == selectedTenderId)
                {
                    recipients.Add(assignment);
                }
            }

            bool committeeQuestion = false;
            // إذا في محادثة مختارة، يفحص إذا كانت بين اللجنة العليا والمانجر.
            if (selected != null)
            {
                var committeeQuestions = SourcingTenderManagement.Services.TopCommittee
                    .TopCommitteeClarificationScope.Query(_context);

                foreach (var question in committeeQuestions)
                {
                    // إذا رقم السؤال مطابق للمحادثة المختارة، يحددها كمحادثة لجنة عليا.
                    if (question.ClarificationId == selected.ClarificationId)
                    {
                        committeeQuestion = true;
                        break;
                    }
                }
            }
            var canReply = selected != null && selected.Status != "Resolved" &&
                (!committeeQuestion || (selected.RecipientUserId == ManagerId && selected.Status == "Pending"));
            var confirmations = SourcingTenderManagement.Services.ClarificationResolution.ConfirmedUsers(selected?.ClarificationResponses
                ?? []);
            ViewBag.HasConfirmed = confirmations.Contains(ManagerId);
            ViewBag.CanResolve = selected != null && selected.Status != "Resolved" && !confirmations.Contains(ManagerId) &&
                (!committeeQuestion || (selected.RecipientUserId == ManagerId && selected.Status == "Answered"));

            var page = new ManagerClarificationPage(
                tenders, selectedTenderId, recipients, conversations, selected, ManagerId, canReply);
            ViewData.Model = page;
            return View();
        }

        // POST /SourcingManager/CreateClarification: Create clarification. Runs when its form or button is submitted.
        [HttpPost]
        public IActionResult CreateClarification(ManagerClarificationInput input)
        {
            // إذا بيانات السؤال غير صحيحة، يرجع لصفحة الاستفسارات مع رسالة توضيح.
            if (!ModelState.IsValid)
            {
                TempData["ManagerMessage"] = "Enter a subject, description, and an eligible committee member.";
                return RedirectToAction("Clarifications", new { tenderId = input.TenderId, id = (int?)null });
            }

            using var transaction = _context.Database.BeginTransaction(IsolationLevel.RepeatableRead);
            CommitteeAssignment? recipientAssignment = null;
            foreach (var assignment in EligibleClarificationRecipients())
            {
                // إذا العضو معيّن على نفس العطاء ومطابق للمستلم المختار، يعتمد تعيينه.
                if (assignment.TenderId == input.TenderId && assignment.UserId == input.RecipientId)
                {
                    // إذا وجد أكثر من تعيين مطابق للمستلم، يوقف العملية بسبب تكرار البيانات.
                    if (recipientAssignment != null)
                    {
                        throw new InvalidOperationException("Multiple recipient assignments were found.");
                    }
                    recipientAssignment = assignment;
                }
            }
            // إذا المستلم لم يعد مؤهلاً لهذا العطاء، يمنع إرسال السؤال.
            if (recipientAssignment == null)
            {
                TempData["ManagerMessage"] = "The recipient is no longer eligible for this tender.";
                return RedirectToAction("Clarifications", new { tenderId = input.TenderId, id = (int?)null });
            }

            // إنشاء السؤال وحفظه للحصول على رقمه
            var clarification = new Clarification
            {
                TenderId = input.TenderId,
                SenderUserId = ManagerId,
                RecipientUserId = input.RecipientId,
                Subject = input.Subject.Trim(),
                Description = input.Description.Trim(),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.Clarifications.Add(clarification);
            _context.SaveChanges();
            // إرسال إشعار إلى العضو المختار
            _context.Notifications.Add(new Notification
            {
                UserId = clarification.RecipientUserId,
                Title = "New clarification",
                Message = "Open the conversation to respond.",
                RelativeUrl = Url.Action("Index", "Clarification", new { id = clarification.ClarificationId }),
                CreatedAt = DateTime.UtcNow
            });
            _context.SaveChanges();
            transaction.Commit();

            TempData["ManagerMessage"] = "Question sent.";
            return RedirectToAction("Clarifications", new { tenderId = input.TenderId, id = clarification.ClarificationId });
        }

        // POST /SourcingManager/ReplyToClarification: Reply to clarification. Runs when its form or button is submitted.
        [HttpPost]
        public IActionResult ReplyToClarification(ManagerClarificationReply input)
        {
            // إذا الرد غير صالح أو يساوي عبارة تأكيد الحل المحجوزة للنظام، يمنع حفظه.
            if (!ModelState.IsValid
                || input.Message.Trim() == SourcingTenderManagement.Services.ClarificationResolution.Confirmation)
            {
                TempData["ManagerMessage"] = "Enter a response of up to 4,000 characters.";
                return RedirectToAction("Clarifications", new { tenderId = (int?)null, id = input.Id });
            }

            // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);
            var clarification = _context.Clarifications.Find(input.Id);

            // إذا السؤال غير موجود أو المانجر مش المستلم، يمنع الرد عليه.
            if (clarification == null || clarification.RecipientUserId != ManagerId)
            {
                return NotFound();
            }

            var tenderId = clarification.TenderId;
            // إذا السؤال مرتبط بعرض مورد، يأخذ رقم العطاء من ذلك العرض.
            if (clarification.BidId.HasValue)
            {
                var bid = _context.Bids.Find(clarification.BidId.Value);
                tenderId = bid?.TenderId;
            }

            CommitteeAssignment? senderAssignment = null;
            foreach (var assignment in EligibleClarificationRecipients())
            {
                // إذا مرسل السؤال عضو مؤهل على نفس العطاء، يعتمد تعيينه للرد والإشعار.
                if (assignment.TenderId == tenderId && assignment.UserId == clarification.SenderUserId)
                {
                    // إذا وجد أكثر من تعيين مطابق للمرسل، يوقف العملية بسبب تكرار البيانات.
                    if (senderAssignment != null)
                    {
                        throw new InvalidOperationException("Multiple sender assignments were found.");
                    }
                    senderAssignment = assignment;
                }
            }
            // إذا المرسل لم يعد مؤهلاً للمراسلة بهذا العطاء، يمنع إرسال الرد.
            if (senderAssignment == null)
            {
                TempData["ManagerMessage"] = "This tender is no longer in a stage that allows a reply to this sender.";
                return RedirectToAction("Clarifications", new { tenderId = (int?)null, id = input.Id });
            }

            // إذا السؤال لم يعد بانتظار الرد، يمنع حفظ رد مكرر.
            if (clarification.Status != "Pending")
            {
                TempData["ManagerMessage"] = "This question has already been answered or resolved.";
                return RedirectToAction("Clarifications", new { tenderId = (int?)null, id = input.Id });
            }

            // تسجيل الرد وإشعار المرسل
            clarification.Status = "Answered";

            var response = new ClarificationResponse
            {
                ClarificationId = clarification.ClarificationId,
                RespondedByUserId = ManagerId,
                Message = input.Message.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.ClarificationResponses.Add(response);
            NotifyCommittee(senderAssignment, "Clarification answered", senderAssignment.TenderId);

            _context.SaveChanges();
            transaction.Commit();

            TempData["ManagerMessage"] = "Response sent.";
            return RedirectToAction("Clarifications", new { tenderId = (int?)null, id = input.Id });
        }

        // POST /SourcingManager/ResolveClarification: Resolve clarification. Runs when its form or button is submitted.
        [HttpPost]
        public IActionResult ResolveClarification(int id)
        {
            // Serializable is the strictest transaction setting: nobody else can change the rows read here until it finishes.
            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

            var clarification = _context.Clarifications.Find(id);

            // إذا السؤال غير موجود أو المانجر مش مرسله أو لم يتم الرد عليه، يمنع طلب الحل.
            if (clarification == null || clarification.SenderUserId != ManagerId || clarification.Status != "Answered")
            {
                return NotFound();
            }

            return RedirectToAction("Index", "Clarification", new { id });
        }

        private List<CommitteeAssignment> EligibleClarificationRecipients()
        {
            var roles = _context.Roles.AsNoTracking().ToList();
            var memberships = _context.UserRoles.AsNoTracking().ToList();
            HashSet<string> evaluationMemberIds = new HashSet<string>();
            HashSet<string> topMemberIds = new HashSet<string>();

            foreach (var membership in memberships)
            {
                foreach (var role in roles)
                {
                    // إذا الصلاحية لا تطابق صلاحية العضوية الحالية، يتخطاها.
                    if (role.Id != membership.RoleId)
                    {
                        continue;
                    }

                    // إذا الصلاحية تخص لجنة التقييم أو أحد مناصبها، يسجل المستخدم كعضو تقييم.
                    if (role.Name == RoleNames.EvaluationCommittee
                        || RoleNames.EvaluationPositions.Contains(role.Name!))
                    {
                        evaluationMemberIds.Add(membership.UserId);
                    }

                    // إذا الصلاحية تخص اللجنة العليا أو أحد مناصبها، يسجل المستخدم كعضو لجنة عليا.
                    if (role.Name == RoleNames.TopCommittee
                        || RoleNames.TopPositions.Contains(role.Name!))
                    {
                        topMemberIds.Add(membership.UserId);
                    }
                }
            }

            var assignments = _context.CommitteeAssignments.AsNoTracking()
                .Include("User")
                .Include("Tender")
                .ToList();

            List<CommitteeAssignment> recipients = new List<CommitteeAssignment>();

            foreach (var assignment in assignments)
            {
                // إذا العضو هو المانجر نفسه أو حسابه غير فعّال، يستبعده من المستلمين.
                if (assignment.UserId == ManagerId || !assignment.User.IsActive)
                {
                    continue;
                }

                // إذا التعيين تابع للجنة التقييم، يفحص شروط مراسلة عضو التقييم.
                if (assignment.CommitteeType == "Evaluation")
                {
                    // إذا مرحلة العطاء تسمح بمراسلة لجنة التقييم والعضو يحمل صلاحيتها، يضيفه للمستلمين.
                    if ((assignment.Tender.Status == "UnderEvaluation"
                        || assignment.Tender.Status == AwaitingManagerReview)
                        && evaluationMemberIds.Contains(assignment.UserId))
                    {
                        recipients.Add(assignment);
                    }
                }
                // إذا التعيين تابع للجنة العليا، يفحص شروط مراسلة عضو اللجنة العليا.
                else if (assignment.CommitteeType == "Top")
                {
                    // إذا العطاء عند اللجنة العليا والعضو يحمل صلاحيتها، يضيفه للمستلمين.
                    if (assignment.Tender.Status == AwaitingCommitteeDecision
                        && topMemberIds.Contains(assignment.UserId))
                    {
                        recipients.Add(assignment);
                    }
                }
            }

            return recipients;
        }

        private void NotifyCommittee(CommitteeAssignment assignment, string title, int tenderId)
        {
            var controller = "EvaluationCommittee";
            // إذا العضو من اللجنة العليا، يوجّه رابط الإشعار لقسم اللجنة العليا.
            if (assignment.CommitteeType == "Top")
            {
                controller = "TopCommittee";
            }

            Notify(assignment.UserId, title, $"A clarification for tender #{tenderId} was updated.", controller, "Clarifications");
        }
    }
}
