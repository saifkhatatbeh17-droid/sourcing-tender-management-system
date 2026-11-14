using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SourcingTenderManagement.Contracts.TopCommittee;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
using static SourcingTenderManagement.Services.TopCommittee.TopCommitteeNotificationEvents;

namespace SourcingTenderManagement.Checks;

// Uses production voting and recipient planning with an isolated, rollback-capable memory store.
// It never creates a database context or connects to SQL Server.
public static class TopCommitteeAwardNotificationChecks
{
    public static async Task RunAsync()
    {
        int passed=0;
        void Check(bool ok,string name) { if(!ok)throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS AWARD NOTICE: "+name); }
        var store=new MemoryStore();
        await Approve(store,1,2,3);
        Check(store.Notices.Count==0 && store.Tender.Status=="AwaitingCommitteeDecision","No new notifications before fourth approval");
        await Approve(store,4);
        Check(store.Notices.Select(n=>n.UserId).Order().SequenceEqual(new[]{"manager","officer","requester"}),"Fourth approval notifies actual manager, creator and requester");
        Check(store.Tender.Status=="Awarded" && store.Tender.AwardedBidId==10 && store.Votes.Count==4,"Four-approval award rule unchanged");
        Check(store.Notices.Single(n=>n.UserId=="manager").RelativeUrl=="/SourcingManager/ChosenOffers","Manager destination supports awarded tenders");
        Check(store.Notices.Single(n=>n.UserId=="officer").RelativeUrl=="/SourcingOfficer/Tenders?id=1","Officer destination selects owned tender");
        Check(store.Notices.Single(n=>n.UserId=="requester").RelativeUrl=="/Department/Details/20","Requester destination selects originating request");
        Check(store.Notices.All(n=>!n.IsRead && n.Title=="Tender #1: awarded" && n.CreatedAt!=default),"New notices have unread status, clear title and timestamp");
        Check(store.Notices.All(n=>n.RelativeUrl!.StartsWith('/')&&!n.RelativeUrl.StartsWith("//")&&!n.Message.Contains("secret")),"Local links and no private bid/committee details");
        Check(store.Notices.All(n=>n.UserId!="unrelated"),"Unrelated user with all roles never receives a notice");
        try { await Approve(store,4); throw new Exception("Duplicate vote accepted"); }
        catch(CommitteeStateConflictException) { Check(store.Notices.Count==3,"Duplicate fourth approval adds no notices"); }

        var candidates=RelatedAwardCandidates(store.Tender,store.Managers,store.Request);
        var all=RelatedAwardNotices(store.Tender,candidates,store.Accounts,store.Roles,[]);
        var staged=new List<Notification>(all);
        foreach(var n in all)if(!staged.Any(old=>SameNotice(old,n)))staged.Add(n);
        Check(staged.Count==3,"Repeated plans deduplicate using existing notice identity");
        Check(RelatedAwardNotices(store.Tender,candidates,store.Accounts,store.Roles,["manager","officer"]).Select(n=>n.UserId).SequenceEqual(["requester"]),
            "Existing committee/supplier recipients are not notified twice");
        var dual=store.Request;
        dual.RequestedByUserId="officer";
        var dualAccounts=new[]{new AwardRecipientAccount("manager",true,null),new AwardRecipientAccount("officer",true,5)};
        var dualRoles=new[]{("manager",RoleNames.SourcingManager),("officer",RoleNames.SourcingOfficer),("officer",RoleNames.Department)};
        var dualNotices=RelatedAwardNotices(store.Tender,RelatedAwardCandidates(store.Tender,store.Managers,dual),dualAccounts,dualRoles,[]);
        Check(dualNotices.Count==2 && dualNotices.Count(n=>n.UserId=="officer")==1,"Multi-relationship user receives one new notice");
        Check(dualNotices.Single(n=>n.UserId=="officer").RelativeUrl=="/SourcingOfficer/Tenders?id=1","Deduplicated multi-role user retains an authorized destination");
        dual.RequestedByUserId="requester";
        Check(RelatedAwardNotices(store.Tender,candidates,store.Accounts.Select(a=>a with{IsActive=false}),store.Roles,[]).Count==0,"Inactive recipients excluded");
        Check(RelatedAwardNotices(store.Tender,candidates,store.Accounts,[],[]).Count==0,"Revoked or missing destination roles excluded");
        Check(RelatedAwardNotices(store.Tender,candidates,store.Accounts.Where(a=>a.UserId!="officer"),store.Roles,[]).Count==2,"Missing account does not fall back to broadcast");
        Check(RelatedAwardNotices(store.Tender,candidates,store.Accounts.Select(a=>a with{DepartmentId=99}),store.Roles,[]).All(n=>n.UserId!="requester"),"Requester must still belong to originating department");
        Check(RelatedAwardCandidates(store.Tender,store.Managers,store.Request.WithRequestId(999)).All(c=>c.UserId!="requester"),"Unrelated request is never used");
        Check(RelatedAwardCandidates(store.Tender,[..store.Managers,new("other-manager","Other",true,true,DateTime.UtcNow)],store.Request).All(c=>c.Role!=RoleNames.SourcingManager),"Ambiguous manager records are not guessed");
        Check(RelatedAwardCandidates(store.Tender,store.Managers,null).All(c=>c.Role!=RoleNames.Department),"Missing request does not notify whole department");
        foreach(var state in new[]{"Draft","AwaitingCommitteeDecision","AwaitingManagerReview","Cancelled"})
        {
            var unawarded=new MemoryStore(); unawarded.Tender.Status=state;
            Check(RelatedAwardCandidates(unawarded.Tender,unawarded.Managers,unawarded.Request).Count==0,"No award recipients in "+state);
        }
        var incomplete=new MemoryStore(); incomplete.Tender.Status="Awarded";
        Check(RelatedAwardCandidates(incomplete.Tender,incomplete.Managers,incomplete.Request).Count==0,"Incomplete award does not create new notices");
        foreach(var failCommit in new[]{false,true})
        {
            var failed=new MemoryStore(); await Approve(failed,1,2,3);
            failed.FailSave=!failCommit; failed.FailCommit=failCommit;
            try { await Approve(failed,4); throw new Exception("Failure not simulated"); }
            catch(IOException) { Check(failed.Notices.Count==0&&failed.Votes.Count==3&&failed.Tender.Status=="AwaitingCommitteeDecision"&&failed.Tender.AwardedBidId==null,
                failCommit?"Rolled-back commit publishes no notices":"Failed save publishes no notices"); }
        }
        foreach(var (controller,action,role) in new[]{
            (typeof(SourcingManagerController),"ChosenOffers",RoleNames.SourcingManager),
            (typeof(SourcingOfficerController),"Tenders",RoleNames.SourcingOfficer),
            (typeof(DepartmentController),"Details",RoleNames.Department)})
            Check(controller.GetMethod(action)!=null && controller.GetCustomAttributes<AuthorizeAttribute>().Any(a=>a.Roles==role),
                controller.Name+" notification destination exists with matching role");
        Console.WriteLine($"PASS: {passed} database-free award notification checks.");
    }

    private static PurchaseRequest WithRequestId(this PurchaseRequest request,int id) => new()
    { PurchaseRequestId=id,DepartmentId=request.DepartmentId,RequestedByUserId=request.RequestedByUserId };

    private static async Task Approve(MemoryStore store,params int[] members)
    {
        foreach(var i in members)await new TopCommitteeVotingService(store).SubmitAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,$"member-{i}"),new(ClaimTypes.Role,RoleNames.TopCommittee)],"MemoryQA")),
            new(){TenderId=1,ExpectedNominatedBidId=10,Decision="Approved",Confirmed=true});
    }

    private sealed class MemoryStore : ITopCommitteeVotingStore
    {
        public Tender Tender {get;}=new(){TenderId=1,PurchaseRequestId=20,CreatedByUserId="officer",Status="AwaitingCommitteeDecision",
            Title="secret title should not be copied",EvaluationReport=new(){RecommendedBidId=10,ManagerSummary="secret committee recommendation"}};
        public PurchaseRequest Request {get;}=new(){PurchaseRequestId=20,DepartmentId=5,RequestedByUserId="requester"};
        public List<CommitteeManager> Managers {get;}=[new("manager","Manager",true,true,DateTime.UtcNow)];
        private readonly List<CommitteeMember> members=Enumerable.Range(1,4).Select(i=>new CommitteeMember(i,$"member-{i}","Member",i,true,true)).ToList();
        private readonly Bid nomination=new(){BidId=10,TenderId=1,SubmittedAt=DateTime.UtcNow,Status="Compliant"};
        public List<TenderApproval> Votes=>Tender.TenderApprovals.ToList();
        public List<Notification> Notices {get;}=[];
        public AwardRecipientAccount[] Accounts {get;}=[new("manager",true,null),new("officer",true,null),new("requester",true,5),new("unrelated",true,5)];
        public (string UserId,string Role)[] Roles {get;}=[("manager",RoleNames.SourcingManager),("officer",RoleNames.SourcingOfficer),("requester",RoleNames.Department),
            ("unrelated",RoleNames.SourcingManager),("unrelated",RoleNames.SourcingOfficer),("unrelated",RoleNames.Department)];
        public bool FailSave {get;set;}
        public bool FailCommit {get;set;}

        public async Task<T> ExecuteAsync<T>(string memberId,int tenderId,Func<ICommitteeVotingSession,CancellationToken,Task<T>> action,CancellationToken ct)
        {
            var oldVotes=Tender.TenderApprovals.ToList(); var oldStatus=Tender.Status; var oldId=Tender.AwardedBidId; var oldAt=Tender.AwardedAt; var oldNotes=Tender.AwardNotes;
            var session=new Session(this,new(new(Tender,true,[RoleNames.TopCommittee],members,Managers),nomination));
            try
            {
                var result=await action(session,ct);
                if(FailCommit)throw new IOException("Simulated commit failure");
                foreach(var notice in session.Staged)if(!Notices.Any(n=>SameNotice(n,notice)))Notices.Add(notice);
                return result;
            }
            catch
            {
                Tender.TenderApprovals.Clear(); foreach(var vote in oldVotes)Tender.TenderApprovals.Add(vote);
                Tender.Status=oldStatus; Tender.AwardedBidId=oldId; Tender.AwardedAt=oldAt; Tender.AwardNotes=oldNotes;
                throw;
            }
        }

        private sealed class Session(MemoryStore owner,CommitteeVotingContext context) : ICommitteeVotingSession
        {
            public CommitteeVotingContext Context=>context;
            public List<Notification> Staged {get;}=[];
            public Task AssertNotPausedAsync(CancellationToken ct)=>Task.CompletedTask;
            public Task<bool> IsDatabaseReadyAsync(CancellationToken ct)=>Task.FromResult(true);
            public void Add(TenderApproval vote)=>owner.Tender.TenderApprovals.Add(vote);
            public Task SaveAsync(CancellationToken ct)
            {
                Staged.AddRange(RelatedAwardNotices(owner.Tender,RelatedAwardCandidates(owner.Tender,owner.Managers,owner.Request),owner.Accounts,owner.Roles,[]));
                if(owner.FailSave)throw new IOException("Simulated save failure");
                return Task.CompletedTask;
            }
        }
    }
}
