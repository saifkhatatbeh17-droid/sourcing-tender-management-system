using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Data;
namespace SourcingTenderManagement.Controllers;
[Authorize(Policy = "ApprovedSupplier")]
public class SupplierController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public IActionResult Profile() => View();
    public IActionResult Invitations() => View();
    public IActionResult Offers() => View();
    public IActionResult Clarifications() => View();
    public IActionResult History() => View();
}
