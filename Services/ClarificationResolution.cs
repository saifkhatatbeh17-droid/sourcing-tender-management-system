using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services;

internal static class ClarificationResolution
{
    internal const string Confirmation = "[Portal: resolution confirmed]";

    internal static HashSet<string> ConfirmedUsers(IEnumerable<ClarificationResponse> responses) =>
        responses.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ClarificationResponseId)
            .TakeWhile(r => r.Message == Confirmation)
            .Select(r => r.RespondedByUserId).ToHashSet();

    internal static ClarificationResponse CreateConfirmation(int id, string actorId) => new()
    {
        ClarificationId = id,
        RespondedByUserId = actorId,
        Message = Confirmation,
        CreatedAt = DateTime.UtcNow
    };
}
