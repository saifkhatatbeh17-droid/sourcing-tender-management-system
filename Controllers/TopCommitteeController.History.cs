using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
namespace SourcingTenderManagement.Controllers;

// CEO & Chiefs page listing past decisions.
public partial class TopCommitteeController
{
    [HttpGet]
    public async Task<IActionResult> History([FromServices] TopCommitteeHistory history,
        [FromQuery] CommitteeHistoryFilter filter, CancellationToken ct) => await ReadAsync(async () =>
    {
        ViewData["ActivePage"] = nameof(History);
        try
        {
            if (!ModelState.IsValid) throw new ValidationException("Check the history search, filters and page settings.");
            return View(await history.GetAsync(User, filter, ct));
        }
        catch (ValidationException error)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ModelState.AddModelError("", error.Message);
            return View(new CommitteeHistoryPage(filter, [], 0));
        }
    });
}
