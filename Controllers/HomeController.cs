using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models;
namespace SourcingTenderManagement.Controllers;
public class HomeController : Controller
{
    public IActionResult Index() => View();
    public IActionResult About() => View();
    public IActionResult HowItWorks() => View();
    public IActionResult Contact() => View();
    public IActionResult Team() => View(TeamMember.Roster);
    public IActionResult Error() { Response.StatusCode = 500; return View(); }
}
