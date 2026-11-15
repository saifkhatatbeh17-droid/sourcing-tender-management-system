namespace SourcingTenderManagement.Models;
public static class PageNavigation
{
    public static readonly Dictionary<string, (string Action, string Title)[]> Menus = new()
    {
        ["Admin"] = [("Dashboard", "Dashboard"), ("Users", "Users"), ("Departments", "Departments"), ("SupplierApplications", "Supplier Applications"), ("Suppliers", "Suppliers")],
        ["Department"] = [("Dashboard", "Dashboard"), ("Create", "New request"), ("Requests", "Requests history"), ("Clarifications", "Clarifications")],
        ["SourcingOfficer"] = [("Dashboard", "Dashboard"), ("Requests", "Request Review"), ("Offers", "Tender workspace"), ("Clarifications", "Clarifications"), ("Reports", "Reports")],
        ["EvaluationCommittee"] = [("Dashboard", "Dashboard"), ("Evaluations", "Evaluations"), ("Reports", "Combined Reports"), ("Clarifications", "Clarifications")],
        ["SourcingManager"] = [("Dashboard", "Dashboard"), ("Submissions", "Committee Submissions"), ("ChosenOffers", "Chosen Offers"), ("Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["TopCommittee"] = [("Dashboard", "Dashboard"), ("Decisions", "Decisions"), ("History", "Tender History"), ("Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["Supplier"] = [("Dashboard", "Dashboard"), ("Invitations", "Invitations"), ("Offers", "My Offers"), ("Clarifications", "Clarifications"), ("History", "History")],
    };
}
