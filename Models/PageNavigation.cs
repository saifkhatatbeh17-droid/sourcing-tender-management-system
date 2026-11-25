namespace SourcingTenderManagement.Models;
public sealed record PageNavigationItem(string Action, string Title, string? Controller = null)
{
    public static implicit operator PageNavigationItem((string Action, string Title) item) => new(item.Action, item.Title);
}

public static class PageNavigation
{
    public static readonly Dictionary<string, PageNavigationItem[]> Menus = new()
    {
        ["Admin"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("AiLog", "AI log"), ("Users", "Users"), ("Departments", "Departments"), ("SupplierApplications", "Supplier Applications"), ("Suppliers", "Suppliers"), new("Index", "Reports", "Reports"), new("Access", "Report access", "Reports"), ("ContactMessages", "Contact Messages")],
        ["Department"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Create", "New request"), ("Requests", "Requests history"), ("Clarifications", "Clarifications"), new("Index", "Reports", "Reports")],
        ["SourcingOfficer"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Requests", "Request Review"), ("Offers", "Tender workspace"), ("Clarifications", "Clarifications"), ("Reports", "Report review"), new("Index", "Reports", "Reports")],
        ["EvaluationCommittee"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Evaluations", "Evaluations"), ("Reports", "Committee report"), ("Clarifications", "Clarifications"), new("Index", "Reports", "Reports")],
        ["SourcingManager"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Submissions", "Officer Submissions"), ("ChosenOffers", "Chosen Offers"), new("Index", "Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["TopCommittee"] = [("Dashboard", "Dashboard"), ("AiIntelligence", "AI Intelligence"), ("Decisions", "Decisions"), ("History", "Tender History"), new("Index", "Reports", "Reports"), ("Clarifications", "Clarifications")],
        ["Supplier"] = [("Dashboard", "Dashboard"), ("Invitations", "Invitations"), ("Offers", "My Offers"), ("Clarifications", "Clarifications"), ("History", "History")],
    };
}
