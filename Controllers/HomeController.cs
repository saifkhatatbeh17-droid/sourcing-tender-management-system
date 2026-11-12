using Microsoft.AspNetCore.Mvc;
namespace SourcingTenderManagement.Controllers;
public class HomeController : Controller
{
    public IActionResult Index() => View();
    public IActionResult About() => View();
    public IActionResult HowItWorks() => View();
    public IActionResult Contact() => View();
    public IActionResult Error() { Response.StatusCode = 500; return View(); }
}
