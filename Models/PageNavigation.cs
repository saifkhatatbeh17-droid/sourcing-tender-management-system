namespace SourcingTenderManagement.Models;
public static class PageNavigation
{
    public static readonly Dictionary<string, (string Action, string Title)[]> Menus = new()
    {
        ["Admin"] = [("Dashboard", "Dashboard"), ("Users", "Users"), ("Departments", "Departments"), ("SupplierApplications", "Supplier Applications"), ("Suppliers", "Suppliers")],
        ["Department"] = [("Dashboard", "Dashboard"), ("Requests", "Purchase Requests"), ("Clarifications", "Clarifications")],
        ["SourcingOfficer"] = [("Dashboard", "Dashboard"), ("Requests", "Request Review"), ("Tenders", "Tenders"), ("Invitations", "Supplier Invitations"), ("Offers", "Received Offers"), ("Compliance", "Compliance Review"), ("Comparison", "Comparison & AI Analysis"), ("Evaluations", "Evaluations & Recommendations"), ("Clarifications", "Clarifications"), ("Reports", "Reports")],
        ["EvaluationCommittee"] = [("Dashboard", "Dashboard"), ("Evaluations", "Evaluations"), ("Reports", "Combined Reports"), ("Clarifications", "Clarifications")],
        ["SourcingManager"] = [("Dashboard", "Dashboard"), ("Submissions", "Committee Submissions"), ("ChosenOffers", "Chosen Offers"), ("Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["TopCommittee"] = [("Dashboard", "Dashboard"), ("Decisions", "Decisions"), ("History", "Tender History"), ("Reports", "Reports"), ("Clarifications", "Clarifications"), ("Notifications", "Notifications")],
        ["Supplier"] = [("Dashboard", "Dashboard"), ("Invitations", "Invitations"), ("Offers", "My Offers"), ("Clarifications", "Clarifications"), ("History", "History")],
    };
}
