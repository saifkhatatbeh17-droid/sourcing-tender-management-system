using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Data;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.EvaluationCommittee)]
public class EvaluationCommitteeController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public IActionResult Evaluations() => View();
    public IActionResult Reports() => View();
    public IActionResult Clarifications() => View();
}
