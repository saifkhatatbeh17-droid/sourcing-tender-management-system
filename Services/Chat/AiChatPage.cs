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
            "Department workspace: purchase requests (title, reason, priority, target date, line items), request history and progress, returned requests that need edits, clarifications. Cannot view supplier bids, pricing, or committee scores.",
        RoleNames.SourcingOfficer =>
            "Sourcing Officer workspace: review incoming purchase requests, publish tenders and invite approved suppliers, check offer compliance (normal committee handoff needs 3 distinct compliant suppliers, or round 4 with a written reason), and review committee evaluation reports.",
        RoleNames.EvaluationCommittee =>
            "Evaluation Committee workspace: 4 seats evaluate tenders in UnderEvaluation. Members confirm a financial weight (technical = 100 - financial), then score every compliant offer; the highest average wins and ties need all 4 tie votes. Position 1 (Department Manager) submits the combined report.",
        RoleNames.SourcingManager =>
            "Sourcing Manager workspace: review officer-submitted evaluation reports (approve, return for review, reject), forward approved recommendations to CEO & Chiefs with a referral reason, and see Chosen Offers.",
        RoleNames.TopCommittee =>
            "CEO & Chiefs workspace: 4 executive seats vote on referred tenders on the Decisions page. Four approvals award the tender; a rejection returns it to Sourcing Manager review.",
        RoleNames.Supplier =>
            "Supplier workspace: invitations sent to your company, offers (quotations) in JOD with line prices, delivery days, payment terms, 16% tax on the item subtotal and additional costs. Strictly confidential: never competitor names, competitor bids, or committee evaluations.",
        RoleNames.Admin =>
            "Administrator workspace: full cross-role visibility. Users, departments, supplier applications and statuses, contact messages. Admins can create employee accounts from an Excel sheet with the import button in this chat panel.",
        _ => ""
    };

    public static string[] StarterPrompts(string workspace, bool hasRecord) => workspace switch
    {
        RoleNames.Department =>
        [
            hasRecord ? "Summarize this request and its current status" : "Summarize my department's requests",
            "Which of my requests need changes?",
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
            hasRecord ? "Compare the eligible offers and scores on this tender" : "Summarize my evaluation queue",
            "How do weights and scoring work here?",
            "Take me to Committee report"
        ],
        RoleNames.SourcingManager =>
        [
            hasRecord ? "Summarize this submission and winning offer" : "Which committee submissions need my decision?",
            "Which tenders are with CEO & Chiefs?",
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
            "How is the tax on my offer calculated?",
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
            hasRecord ? "I'm Safi! Want me to check this request's progress?" : "Hey! I'm Safi — ask me about your department's requests.",
            "Need to jump to Requests history or Clarifications? Just say the word."
        ],
        RoleNames.SourcingOfficer =>
        [
            hasRecord ? "Safi here! Want a summary of this record, or to open another page?" : "Safi on duty! Ask me about pending requests and tenders, or jump pages.",
            "Tell me 'take me to Tender workspace' or 'check incoming requests' anytime."
        ],
        RoleNames.EvaluationCommittee =>
        [
            hasRecord
                ? "I'm Safi! Need a breakdown of these offers or the weight lock status?"
                : (action == "Reports"
                    ? "Reviewing the committee report? Ask me to summarize rankings or jump to Evaluations."
                    : "Hey! I'm Safi — ready to check your evaluation queue or switch pages."),
            "Say 'take me to Committee report' or ask how your committee seat works."
        ],
        RoleNames.SourcingManager =>
        [
            hasRecord ? "Safi here! Want a crisp brief on this winning offer before you decide?" : "Hi! I'm Safi — ask me which submissions await your decision.",
            "Ask me for a summary or to open Chosen Offers anytime."
        ],
        RoleNames.TopCommittee =>
        [
            hasRecord ? "Good day! Safi here — want me to check voting readiness on this tender?" : "Welcome! I'm Safi — ask me which referred tenders need your vote.",
            "Tell me 'take me to Decisions' or 'open Tender History' to jump straight there."
        ],
        RoleNames.Supplier =>
        [
            hasRecord ? "Hi! I'm Safi — need help understanding this invitation or offer?" : "Welcome! I'm Safi — ask me about your open invitations, deadlines, or offers.",
            "Need to jump to Invitations or My Offers? Just ask and I'll take you there."
        ],
        RoleNames.Admin =>
        [
            "All lines clear! I'm Safi — ask for a live Tiryaq pulse or jump to any Admin page.",
            "Need to check supplier applications or unread messages? Or import accounts from an Excel sheet with the import button in this chat."
        ],
        _ => ["Hi! I'm Safi, your Tiryaq co-pilot. Ask me anything about your workspace."]
    };
}
