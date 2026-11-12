using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Data;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.TopCommittee)]
public class TopCommitteeController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public IActionResult Decisions() => View();
    public IActionResult History() => View();
    public IActionResult Reports() => View();
    public IActionResult Clarifications() => View();
}
