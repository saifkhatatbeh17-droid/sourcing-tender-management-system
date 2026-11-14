using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Services.TopCommittee;
namespace SourcingTenderManagement.Controllers;
public partial class TopCommitteeController
{
 [HttpGet]
 public async Task<IActionResult> Notifications([FromServices]TopCommitteeNotifications notifications,int page=1,CancellationToken ct=default)=>await ReadAsync(async()=>{try{if(!ModelState.IsValid)throw new ValidationException("Choose a valid page.");return View(await notifications.GetAsync(User,page,ct));}catch(ValidationException e){return BadRequest(e.Message);}});
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> MarkNotificationRead([FromServices]TopCommitteeNotifications notifications,int id,CancellationToken ct,string? returnUrl=null)=>await ReadAsync(async()=>{if(!await notifications.MarkAsync(User,id,ct))return NotFound();TempData["NotificationSuccess"]="Notification marked as read.";return NotificationReturn(returnUrl);});
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> MarkAllNotificationsRead([FromServices]TopCommitteeNotifications notifications,CancellationToken ct,string? returnUrl=null)=>await ReadAsync(async()=>{await notifications.MarkAllAsync(User,ct);TempData["NotificationSuccess"]="Your notifications are marked as read.";return NotificationReturn(returnUrl);});
 private IActionResult NotificationReturn(string? returnUrl)=>Url.IsLocalUrl(returnUrl)?LocalRedirect(returnUrl!):RedirectToAction(nameof(Notifications));
}
