using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
namespace SourcingTenderManagement.Controllers;
[Authorize(Roles = RoleNames.SourcingOfficer)]
public class SourcingOfficerController(ApplicationDbContext db) : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public IActionResult Dashboard() => View();
    public IActionResult Requests() => View();
    public async Task<IActionResult> Tenders(int? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownedTenders = db.Tenders.AsNoTracking().Where(t => t.CreatedByUserId == userId);
        var model = new TendersPageViewModel
        {
            Tenders = await ownedTenders.OrderByDescending(t => t.CreatedAt).ToListAsync()
        };
        if (id.HasValue)
        {
            model.SelectedTender = await ownedTenders
                .Include(t => t.Bid).ThenInclude(b => b!.Invitation).ThenInclude(i => i.SupplierProfile)
                .Include(t => t.Bid).ThenInclude(b => b!.BidItems)
                .SingleOrDefaultAsync(t => t.TenderId == id);
            if (model.SelectedTender == null) return NotFound();
        }
        return View(model);
    }
    public IActionResult Invitations() => View();
    public IActionResult Offers() => View();
    public IActionResult Compliance() => View();
    public IActionResult Comparison() => View();
    public IActionResult Evaluations() => View();
    public IActionResult Clarifications() => View();
    public IActionResult Reports() => View();
}
