using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Data;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.SourcingManager)]
public class SourcingManagerController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public IActionResult Submissions() => View();
    public IActionResult ChosenOffers() => View();
    public IActionResult Reports() => View();
    public IActionResult Clarifications() => View();
}
