using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.Chat;

internal enum AiChatRecord
{
    DepartmentRequest,
    OfficerRequest,
    OfficerTender,
    EvaluationTender,
    ManagerReport,
    TopTender,
    SupplierInvitation,
    SupplierOffer,
    SupplierApplication,
    ContactMessage
}

public static class AiChatPage
{
    internal static AiChatRecord? Record(string workspace, string? action) => (workspace, action) switch
    {
        (RoleNames.Department, "Details" or "Edit") => AiChatRecord.DepartmentRequest,
        (RoleNames.SourcingOfficer, "Requests" or "Details") => AiChatRecord.OfficerRequest,
        (RoleNames.SourcingOfficer, "Offers" or "Tenders" or "Comparison" or "Evaluations") => AiChatRecord.OfficerTender,
        (RoleNames.EvaluationCommittee, "Evaluations" or "Reports") => AiChatRecord.EvaluationTender,
        (RoleNames.SourcingManager, "Submissions") => AiChatRecord.ManagerReport,
        (RoleNames.TopCommittee, "TenderDetails") => AiChatRecord.TopTender,
        (RoleNames.Supplier, "Invitation" or "CreateOffer") => AiChatRecord.SupplierInvitation,
        (RoleNames.Supplier, "Offer") => AiChatRecord.SupplierOffer,
        (RoleNames.Admin, "SupplierDetails") => AiChatRecord.SupplierApplication,
        (RoleNames.Admin, "ContactMessageDetails") => AiChatRecord.ContactMessage,
        _ => null
    };

    public static string Guidance(string workspace) => workspace switch
    {
        RoleNames.Department =>
            "Department workspace: create and submit pharmaceutical/procurement purchase requests (Title, Reason, Priority [Low/Normal/High/Urgent], TargetDate >= today, and line items with Quantity > 0, Unit, Specifications), track department request progress in Requests history, and edit/resubmit Returned or unreviewed Submitted requests owned by the requester. Cannot view supplier bids, pricing, or committee scores.",
        RoleNames.SourcingOfficer =>
            "Sourcing Officer workspace: review incoming Purchase Requests (StartReview, Approve, Return with reason, Reject with reason), publish Tenders directly from eligible requests and invite approved suppliers in the category, review submitted supplier offers for Compliance (Compliant/NonCompliant), close or extend offer rounds (normal committee handoff requires 3 distinct compliant suppliers, or round 4 with a written shortfall reason), hand off compliant offers to the 4-member Evaluation Committee, and review CommitteeApproved evaluation reports (Approve -> sends OfficerSubmitted report to Sourcing Manager; RequestEdits -> returns to committee; Reject -> cancels tender).",
        RoleNames.EvaluationCommittee =>
            "Evaluation Committee workspace: 4 assigned seats review tenders in UnderEvaluation status. First, all 4 members confirm a whole-number FinancialWeight (1–99%), which locks the committee average FinancialWeight and TechnicalWeight (100 - FinancialWeight). Next, each of the 4 members scores every compliant offer with whole-number FinancialMark (0..FinancialWeight) and TechnicalMark (0..TechnicalWeight) plus optional notes. Highest arithmetic mean wins; ties require unanimous 4/4 tie votes. Once all 4 members have scored, Position 1 (Department Manager) generates/submits the combined top-three CommitteeApproved report to the Sourcing Officer.",
        RoleNames.SourcingManager =>
            "Sourcing Manager workspace: review OfficerSubmitted / CommitteeApproved evaluation reports in Officer Submissions (Approve -> ManagerApproved; ReturnedForReview -> sends back to Evaluation Committee; Rejected -> closes tender). Once a recommendation is ManagerApproved, forward/refer it with a policy referral reason to CEO & Chiefs (Top Committee) for final award voting. Also handles Top-Committee-rejected tenders and views Chosen Offers.",
        RoleNames.TopCommittee =>
            "CEO & Chiefs (Top Committee) workspace: 4 executive seats review referred tenders in AwaitingCommitteeDecision status on the Decisions page. Each eligible member casts a final vote (Approved or Rejected; rejection requires a written reason). Four valid approvals automatically award the tender to the nominated supplier offer; a rejection returns the tender to Sourcing Manager review.",
        RoleNames.Supplier =>
            "Supplier workspace: view Invitations sent to your company, Accept or Decline open tender invitations before ClosesAt, and prepare/save/submit Quotations (Offers) in JOD (JD) with line-item unit prices, delivery days, payment terms, technical details, percentage tax (0–100% applied to item subtotal), and additional costs, or withdraw a submitted offer before the deadline. Strictly confidential: a supplier can never see competitor names, competitor bids, or internal committee evaluations.",
        RoleNames.Admin =>
            "Administrator workspace: full cross-role visibility across Tiryaq. Manage employee Users (activate/deactivate, update details/departments while preserving active committee seat uniqueness), create/edit/toggle Departments, review Supplier Applications and Supplier statuses (Approved, ChangesRequested, Rejected, Suspended), and manage Contact Messages (Read/Unread).",
        _ => ""
    };

    public static string[] StarterPrompts(string workspace, bool hasRecord) => workspace switch
    {
        RoleNames.Department =>
        [
            hasRecord ? "Summarize this request and its current status" : "Summarize my department's requests",
            "Create a High priority request for sterile amber vials",
            "Take me to Requests history"
        ],
        RoleNames.SourcingOfficer =>
        [
            hasRecord ? "Summarize this tender and export a downloadable PDF & Excel" : "What requests and tenders need my attention?",
            "Create a downloadable summary of my active tenders",
            "Take me to Tender workspace"
        ],
        RoleNames.EvaluationCommittee =>
        [
            hasRecord ? "Compare the eligible offers and scores on this tender" : "Summarize my evaluation queue and pending reviews",
            "What actions can you do for me in Evaluation Committee?",
            "Take me to Combined Reports"
        ],
        RoleNames.SourcingManager =>
        [
            hasRecord ? "Summarize this submission and winning offer" : "Which committee submissions need my decision?",
            "What actions can you run for me as Sourcing Manager?",
            "Take me to Chosen Offers"
        ],
        RoleNames.TopCommittee =>
        [
            hasRecord ? "Summarize this tender referral and voting readiness" : "Which referred tenders are waiting for my vote?",
            "Explain how voting and awarding work here",
            "Take me to Decisions"
        ],
        RoleNames.Supplier =>
        [
            hasRecord ? "Summarize this invitation or offer and deadline" : "Show my open tender invitations and offers",
            "What can you help me do as a Supplier?",
            "Take me to My Offers"
        ],
        RoleNames.Admin =>
        [
            "Give me a live overview of Tiryaq across all roles",
            "List active departments and approved suppliers",
            "Take me to Supplier Applications"
        ],
        _ => ["What can you help me do here?"]
    };

    public static string[] CharacterGreetings(string workspace, string? action, bool hasRecord) => workspace switch
    {
        RoleNames.Department =>
        [
            hasRecord
                ? "I'm Safi! Want me to check this request's progress or update its items?"
                : "Hey! I'm Safi — tell me what your lab needs and I'll submit the request.",
            "Need to jump to Requests history or Clarifications? Just say the word."
        ],
        RoleNames.SourcingOfficer =>
        [
            hasRecord
                ? "Safi here! Want me to review compliance on this record or open another page?"
                : "Safi on duty! Ask me to review pending requests, publish a tender, or jump pages.",
            "Tell me 'take me to Tender workspace' or 'check incoming requests' anytime."
        ],
        RoleNames.EvaluationCommittee =>
        [
            hasRecord
                ? "I'm Safi! Need a breakdown of these offers, weight lock status, or score help?"
                : (action == "Reports"
                    ? "Reviewing combined reports? Ask me to summarize rankings or jump to Evaluations."
                    : "Hey! I'm Safi — ready to check your evaluation queue, save marks, or switch pages."),
            "Say 'take me to Combined Reports' or ask how your committee seat works."
        ],
        RoleNames.SourcingManager =>
        [
            hasRecord
                ? "Safi here! Want a crisp brief on this winning offer before you decide?"
                : "Hi! I'm Safi — ask me which submissions await your decision or jump to Chosen Offers.",
            "Ready to approve or forward a recommendation to CEO & Chiefs? Ask me anytime."
        ],
        RoleNames.TopCommittee =>
        [
            hasRecord
                ? "Good day! Safi here — want me to verify voting readiness or record your decision?"
                : "Welcome! I'm Safi — ask me which referred tenders need your vote right now.",
            "Tell me 'take me to Decisions' or 'open Tender History' to jump straight there."
        ],
        RoleNames.Supplier =>
        [
            hasRecord
                ? "Hi! I'm Safi — need help calculating JOD tax totals or submitting this quotation?"
                : "Welcome! I'm Safi — ask me about your open invitations, deadlines, or offers.",
            "Need to jump to Invitations or My Offers? Just ask and I'll take you there."
        ],
        RoleNames.Admin =>
        [
            "All lines clear! I'm Safi — ask for a live Tiryaq pulse or jump to any Admin page.",
            "Need to review a supplier, add a department, or check unread messages? Just tell me."
        ],
        _ => ["Hi! I'm Safi, your Tiryaq co-pilot. Ask me anything about your workspace."]
    };
}
