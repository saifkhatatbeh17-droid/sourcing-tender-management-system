using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
namespace SourcingTenderManagement.Checks;
// Part of the --top-committee-foundation-check self-test (called by TopCommitteeFoundationChecks).
public static class TopCommitteePhase6Checks
{
 public static async Task RunAsync(IServiceProvider services)
 {
  int passed=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;}
  static ClaimsPrincipal Actor(string id,string role=RoleNames.TopCommittee)=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id),new Claim(ClaimTypes.Role,role)},"NotificationChecks"));
  foreach(var name in new[]{"MarkNotificationRead","MarkAllNotificationsRead"}){var method=typeof(TopCommitteeController).GetMethod(name)!;Check(method.GetCustomAttribute<HttpPostAttribute>()!=null&&method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>()!=null,"CSRF POST notification action");}
  var fixture=new CommitteeNotificationPage([new(1,"Test unread","<script>private test</script>",false,DateTime.UtcNow,1),new(2,"Test read","Recorded",true,DateTime.UtcNow,null)],2,1,1);
  var html=await TopCommitteePhase2Checks.RenderAsync(services,Actor("test"),"Notifications",fixture);Check(html.Contains("Unread")&&html.Contains("Mark all as read")&&html.Contains("Mark as read"),"Notification controls");Check(html.Contains("__RequestVerificationToken")&&!html.Contains("<script>private test</script>"),"CSRF tokens and encoding");Check(html.Contains("View tender #1")&&html.Contains("OpenNotification"),"Authorized link");var empty=await TopCommitteePhase2Checks.RenderAsync(services,Actor("test"),"Notifications",new CommitteeNotificationPage([],0,0,1));Check(empty.Contains("No notifications"),"Empty notifications");
  try{await services.GetRequiredService<TopCommitteeNotifications>().GetAsync(new ClaimsPrincipal());throw new Exception("Anonymous notifications accepted");}catch(CommitteeAccessDeniedException){passed++;}
  var output=Environment.GetEnvironmentVariable("TOP_COMMITTEE_PREVIEW_DIR");if(!string.IsNullOrWhiteSpace(output)){await File.WriteAllTextAsync(Path.Combine(output,"notifications.html"),html);await File.WriteAllTextAsync(Path.Combine(output,"notifications-empty.html"),empty);}
  var connection=Environment.GetEnvironmentVariable("TOP_COMMITTEE_NOTIFICATION_TEST_CONNECTION");
  if(!string.IsNullOrWhiteSpace(connection)){
   var cs=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);if(!cs.InitialCatalog.StartsWith("SourcingTender_Phase46_"))throw new Exception("Refuse non-isolated notification writes");
   var setup=new ServiceCollection();setup.AddLogging();setup.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection));setup.AddTopCommitteeFoundation();using var provider=setup.BuildServiceProvider();
   async Task<CommitteeNotificationPage> Read(string member){using var scope=provider.CreateScope();return await scope.ServiceProvider.GetRequiredService<TopCommitteeNotifications>().GetAsync(Actor(member));}
   async Task Vote(int tender,int bid,int member,string decision="Approved"){using var scope=provider.CreateScope();await scope.ServiceProvider.GetRequiredService<ITopCommitteeVoting>().SubmitAsync(Actor("p46-member"+member),new(){TenderId=tender,ExpectedNominatedBidId=bid,Decision=decision,Notes=decision=="Rejected"?"Notification integration test rejection":null,Confirmed=true});}
   var manager=Actor("p46-manager",RoleNames.SourcingManager);
   using(var scope=provider.CreateScope()){var tx=scope.ServiceProvider.GetRequiredService<ITopCommitteeTenderTransaction>();await TopCommitteeNotificationEvents.PublishAssignmentsAsync(tx,manager,1);await TopCommitteeNotificationEvents.PublishAssignmentsAsync(tx,manager,1);}
   Check((await Read("p46-member1")).Items.Count==1,"Assignment hook deduplicated");
   for(int i=1;i<=4;i++)await Vote(1,1,i);
   Check((await Read("p46-member1")).Items.Count==2&&(await Read("p46-member2")).Items.Count==2,"Award notifications to each assigned member");
   using(var scope=provider.CreateScope()){try{await scope.ServiceProvider.GetRequiredService<ITopCommitteeVoting>().SubmitAsync(Actor("p46-member4"),new(){TenderId=1,ExpectedNominatedBidId=1,Decision="Approved",Confirmed=true});throw new Exception("Duplicate accepted");}catch(CommitteeStateConflictException){passed++;}}
   Check((await Read("p46-member1")).Items.Count==2,"Duplicate decision did not duplicate notices");
   await Vote(2,3,1,"Rejected");Check((await Read("p46-member1")).Items.Count==3,"Rejection notification");
   int qid;using(var scope=provider.CreateScope()){var result=await scope.ServiceProvider.GetRequiredService<ITopCommitteeClarifications>().CreateAsync(Actor("p46-member1"),new(){TenderId=3,Subject="Private notification test subject",Description="Private content must not appear in notifications"});qid=result.ClarificationId;await scope.ServiceProvider.GetRequiredService<ITopCommitteeManagerAnswers>().AnswerAsync(manager,qid,"Private manager answer");}
   Check((await Read("p46-member1")).Items.Count==4&&(await Read("p46-member2")).Items.Count==3,"Answer notification private to creator");
   using(var scope=provider.CreateScope())await scope.ServiceProvider.GetRequiredService<ITopCommitteeClarifications>().ResolveAsync(Actor("p46-member1"),qid);
   using(var scope=provider.CreateScope())await scope.ServiceProvider.GetRequiredService<ITopCommitteeManagerAnswers>().ConfirmResolutionAsync(manager,qid);
   var one=await Read("p46-member1");Check(one.Items.Count==5&&(await Read("p46-member2")).Items.Count==4,"Resolution notification for all assigned members");Check(one.Items.All(n=>!n.Message.Contains("Private")&&!n.Title.Contains("Private")),"No clarification contents in notices");
   using(var scope=provider.CreateScope()){var notifications=scope.ServiceProvider.GetRequiredService<TopCommitteeNotifications>();Check(!await notifications.MarkAsync(Actor("p46-member2"),one.Items[0].Id),"Cannot mark another member's notice");Check(await notifications.MarkAsync(Actor("p46-member1"),one.Items[0].Id),"Own mark read");Check(await notifications.MarkAsync(Actor("p46-member1"),one.Items[0].Id),"Repeated mark idempotent");await notifications.MarkAllAsync(Actor("p46-member1"));}
   Check((await Read("p46-member1")).Unread==0,"Read state persisted across scopes");Check((await Read("p46-member2")).Unread==4,"Other recipient read status unchanged");Check((await Read("p46-outsider")).Total==0,"Unassigned user receives no event notices");
   using(var scope=provider.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(!db.Database.HasPendingModelChanges(),"No schema model change");Check(await db.TenderApprovals.CountAsync(v=>v.Stage=="Top")==5,"Vote history preserved");Check(await db.Tenders.AnyAsync(t=>t.TenderId==1&&t.Status=="Awarded"&&t.AwardedBidId==1),"Award behavior preserved");}
  }
  Console.WriteLine($"PASS: {passed} Phase 6 notification checks; SQL writes run only when an isolated test connection is explicitly supplied.");
 }
}
