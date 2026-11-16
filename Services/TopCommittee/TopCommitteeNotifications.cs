using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
namespace SourcingTenderManagement.Services.TopCommittee;
public sealed class TopCommitteeNotifications(ApplicationDbContext db,TopCommitteeAccessService access)
{
 public async Task<CommitteeNotificationPage> GetAsync(ClaimsPrincipal actor,int page=1,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);if(page<1||page>1000000)throw new ValidationException("Choose a valid notification page.");var q=db.Notifications.AsNoTracking().Where(n=>n.UserId==id);var total=await q.CountAsync(ct);var unread=await q.CountAsync(n=>!n.IsRead,ct);var rows=await q.OrderByDescending(n=>n.CreatedAt).ThenByDescending(n=>n.NotificationId).Skip((page-1)*7).Take(7).ToListAsync(ct);
  var assigned=await db.CommitteeAssignments.AsNoTracking().Where(a=>a.UserId==id&&a.CommitteeType=="Top").Select(a=>a.TenderId).ToListAsync(ct);
  return new(rows.Select(n=>{int? tender=null;var match=Regex.Match(n.RelativeUrl??"","^/TopCommittee/TenderDetails/([1-9][0-9]*)$",RegexOptions.CultureInvariant);if(match.Success&&int.TryParse(match.Groups[1].Value,out var t)&&assigned.Contains(t))tender=t;return new CommitteeNotificationItem(n.NotificationId,n.Title,n.Message,n.IsRead,n.CreatedAt,tender);}).ToList(),total,unread,page);
 }
 public async Task<bool> MarkAsync(ClaimsPrincipal actor,int notificationId,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);
  return await db.Notifications.Where(n=>n.NotificationId==notificationId&&n.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.IsRead,true),ct)>0;
 }
 public async Task MarkAllAsync(ClaimsPrincipal actor,CancellationToken ct=default)
 {
  var id=await access.RequireRoleAsync(actor,RoleNames.TopCommittee,ct);await db.Notifications.Where(n=>n.UserId==id&&!n.IsRead).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.IsRead,true),ct);
 }
}
