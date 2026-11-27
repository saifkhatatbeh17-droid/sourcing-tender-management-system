using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Services.Evaluation;
// Checks that a tender has its four Evaluation Committee members before evaluation starts.
public static class CommitteeSetup
{
    public static async Task<string?> Prepare(ApplicationDbContext db, Tender tender)
    {
        var assigned = await db.CommitteeAssignments.Where(a => a.TenderId == tender.TenderId
            && a.CommitteeType == "Evaluation")
            .ToListAsync();
        if (assigned.Count == 4 && assigned.Select(a => a.UserId).Distinct().Count() == 4) return null;
        if (assigned.Count != 0) return "The committee assignments are incomplete. Ask the administrator to correct them.";
        var members = new List<string>();
        foreach (var role in RoleNames.EvaluationPositions)
        {
            var candidates = await db.Users.Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role))
                && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.EvaluationCommittee)))
                .Select(u => u.Id).ToListAsync();
            if (candidates.Count != 1) return $"The administrator must assign exactly one active committee member for {role}.";
            members.Add(candidates[0]);
        }
        if (members.Distinct().Count() != 4) return "Four different committee members are required.";
        for (var i = 0; i < members.Count; i++)
        {
            tender.CommitteeAssignments.Add(new CommitteeAssignment { UserId = members[i], CommitteeType = "Evaluation", Position = i + 1 });
            db.Notifications.Add(new Notification { UserId = members[i],
                Title = "Tender ready for review",
                Message = $"{tender.Title}: review the offers and choose a supplier.",
                RelativeUrl = $"/EvaluationCommittee/Evaluations/{tender.TenderId}",
                CreatedAt = DateTime.UtcNow });
        }
        if (!await db.EvaluationCriteria.AnyAsync(c => c.TenderId == tender.TenderId))
        {
            tender.EvaluationCriteria.Add(new EvaluationCriterion { Name = "Price", Category = "Financial", Weight = 40 });
            tender.EvaluationCriteria.Add(new EvaluationCriterion { Name = "Technical compliance", Category = "Requirements", Weight = 60 });
        }
        if (!await db.EvaluationReports.AnyAsync(r => r.TenderId == tender.TenderId))
            tender.EvaluationReport = new EvaluationReport { Status = "Draft", PreparedByUserId = members[0] };
        return null;
    }
}
