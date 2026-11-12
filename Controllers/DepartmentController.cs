using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Data;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.Department)]
public class DepartmentController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public IActionResult Requests() => View();
    public IActionResult Clarifications() => View();
}
