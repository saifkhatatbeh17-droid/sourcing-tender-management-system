using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.Chat;

internal enum AiChatRecord
{
    DepartmentRequest, OfficerRequest, OfficerTender, EvaluationTender, ManagerReport,
    TopTender, SupplierInvitation, SupplierOffer, SupplierApplication, ContactMessage
}

internal static class AiChatPage
{
    public static AiChatRecord? Record(string workspace, string? action) => (workspace, action) switch
    {
        (RoleNames.Department, "Details") => AiChatRecord.DepartmentRequest,
        (RoleNames.SourcingOfficer, "Requests") => AiChatRecord.OfficerRequest,
        (RoleNames.SourcingOfficer, "Offers") => AiChatRecord.OfficerTender,
        (RoleNames.EvaluationCommittee, "Evaluations" or "Reports") => AiChatRecord.EvaluationTender,
        (RoleNames.SourcingManager, "Submissions") => AiChatRecord.ManagerReport,
        (RoleNames.TopCommittee, "TenderDetails") => AiChatRecord.TopTender,
        (RoleNames.Supplier, "Invitation") => AiChatRecord.SupplierInvitation,
        (RoleNames.Supplier, "Offer") => AiChatRecord.SupplierOffer,
        (RoleNames.Admin, "SupplierDetails") => AiChatRecord.SupplierApplication,
        (RoleNames.Admin, "ContactMessageDetails") => AiChatRecord.ContactMessage,
        _ => null
    };

    public static string Guidance(string workspace) => workspace switch
    {
        RoleNames.Department => "Use New request to enter request details and items, then confirm submission. Requests history shows the department's requests. Only the requester can edit a returned request.",
        RoleNames.SourcingOfficer => "Use Request Review for incoming requests and Tender workspace for invitations, offer compliance, and evaluation handoff. Normal handoff requires three distinct compliant suppliers. Evaluation reports is where officer report review happens.",
        RoleNames.EvaluationCommittee => "Use Evaluations to review the tender and compliant offers. Agreed weights lock and four members score each eligible offer. The highest average leads, and a tie requires unanimous resolution. Use Combined Reports to review committee reports.",
        RoleNames.SourcingManager => "Use Committee Submissions to review reports available to the manager, Chosen Offers for awards, and Reports for decision history. An approved recommendation can be referred for CEO & Chiefs review from the appropriate submission.",
        RoleNames.TopCommittee => "Use Decisions to review assigned tenders. Open a tender to see the recommendation and current voting readiness. Four valid approvals award the nominated offer. A rejection sends the tender back to manager review. Use Tender History and Reports to revisit records available to the member.",
        RoleNames.Supplier => "Use Invitations for tenders your company was invited to and My Offers for your own quotations. Offer entry has Items/prices, Terms/documents, and Review/submit. Tax is entered as a percentage and extra costs contribute to the inclusive total.",
        RoleNames.Admin => "Use Users to manage employee accounts, Departments for the department directory, Supplier Applications to review applications, and Contact Messages for the inbox.",
        _ => ""
    };
}
