using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Controllers;

[Authorize(Roles = RoleNames.Department)]
[AutoValidateAntiforgeryToken]
public class DepartmentController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    public async Task<IActionResult> Dashboard()
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();
        return View(await DepartmentRequests(user).OrderByDescending(r => r.CreatedAt).ToListAsync());
    }

    public async Task<IActionResult> Requests(string? q, string? status)
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();
        var requests = DepartmentRequests(user).Include(r => r.RequestedByUser).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            requests = requests.Where(r => r.Title.Contains(q) || r.PurchaseRequestId.ToString() == q);
        }
        if (!string.IsNullOrWhiteSpace(status)) requests = DepartmentRequestProgress.Filter(requests, status);
        ViewBag.Query = q;
        ViewBag.Status = status;
        return View(await requests.OrderByDescending(r => r.CreatedAt).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();

        var request = await DepartmentRequests(user)
            .Include(r => r.RequestedByUser).Include(r => r.ReviewedByUser)
            .Include(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == id);
        if (request == null) return NotFound();

        ViewData["CanEdit"] = request.RequestedByUserId == user.Id && request.Status == "Returned";

        return View(request);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();
        return View("RequestForm", new PurchaseRequestForm { Items = [new()] });
    }

    [HttpPost]
    public async Task<IActionResult> Create(PurchaseRequestForm form)
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();
        if (!ModelState.IsValid) return View("RequestForm", form);

        var request = new PurchaseRequest
        {
            DepartmentId = user.DepartmentId.Value,
            RequestedByUserId = user.Id
        };
        form.ApplyTo(request);
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Purchase request submitted to the sourcing officer for review.";
        return RedirectToAction(nameof(Details), new { id = request.PurchaseRequestId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();
        var request = await DepartmentRequests(user).Include(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == id && r.RequestedByUserId == user.Id);
        if (request == null) return NotFound();
        if (request.Status != "Returned") return Conflict("Only returned requests can be resubmitted.");

        SetEditViewData(request);
        return View("RequestForm", new PurchaseRequestForm
        {
            Title = request.Title,
            Reason = request.Reason,
            Priority = request.Priority,
            TargetDate = request.TargetDate,
            Items = request.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId)
                .Select(i => new PurchaseRequestItemForm
                {
                    ItemName = i.ItemName,
                    Quantity = i.Quantity,
                    Unit = i.Unit,
                    Specifications = i.Specifications
                }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, PurchaseRequestForm form)
    {
        var user = await users.GetUserAsync(User);
        if (user?.DepartmentId == null) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await db.PurchaseRequests.Include(r => r.PurchaseRequestItems)
            .SingleOrDefaultAsync(r => r.PurchaseRequestId == id && r.DepartmentId == user.DepartmentId
                && r.RequestedByUserId == user.Id);
        if (request == null) return NotFound();
        if (request.Status != "Returned") return Conflict("Only returned requests can be resubmitted.");
        if (!ModelState.IsValid)
        {
            SetEditViewData(request);
            return View("RequestForm", form);
        }

        db.PurchaseRequestItems.RemoveRange(request.PurchaseRequestItems);
        request.PurchaseRequestItems.Clear();
        form.ApplyTo(request);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["ActionSuccess"] = "Updated request resubmitted to the sourcing officer.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public IActionResult Clarifications() => RedirectToAction("Index", "Clarification");

    private IQueryable<PurchaseRequest> DepartmentRequests(ApplicationUser user) =>
        db.PurchaseRequests.AsNoTracking().AsSplitQuery().Include(r => r.Department).Include(r => r.Tenders)
            .Where(r => r.DepartmentId == user.DepartmentId);

    private void SetEditViewData(PurchaseRequest request)
    {
        ViewData["RequestId"] = request.PurchaseRequestId;
        ViewData["ReviewNotes"] = request.ReviewNotes;
    }
}
