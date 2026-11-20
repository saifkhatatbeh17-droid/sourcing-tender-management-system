namespace SourcingTenderManagement.Models;
public static class PageNavigation
{
    public static readonly Dictionary<string, (string Action, string Title)[]> Menus = new()
    {
        ["Admin"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("AiLog", "AI log"), ("Users", "Users"), ("Departments", "Departments"), ("SupplierApplications", "Supplier Applications"), ("Suppliers", "Suppliers"), ("SupplierActivity", "Supplier activity report"), ("ContactMessages", "Contact Messages")],
        ["Department"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Create", "New request"), ("Requests", "Requests history"), ("Clarifications", "Clarifications")],
        ["SourcingOfficer"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Requests", "Request Review"), ("Offers", "Tender workspace"), ("Clarifications", "Clarifications"), ("Reports", "Evaluation reports")],
        ["EvaluationCommittee"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Evaluations", "Evaluations"), ("Reports", "Combined Reports"), ("Clarifications", "Clarifications")],
        ["SourcingManager"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Submissions", "Officer Submissions"), ("ChosenOffers", "Chosen Offers"), ("Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["TopCommittee"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Decisions", "Decisions"), ("History", "Tender History"), ("Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["Supplier"] = [("Dashboard", "Dashboard"), ("Invitations", "Invitations"), ("Offers", "My Offers"), ("Clarifications", "Clarifications"), ("History", "History")],
    };
}
