using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services;

// Query filters that decide which clarifications a user may see (their own, or all for the officer).
internal static class ClarificationVisibility
{
    internal static IQueryable<Clarification> ForRecord(this IQueryable<Clarification> threads, string? kind, int id) =>
        kind switch
        {
            "request" => threads.Where(c => c.PurchaseRequestId == id),
            "tender" => threads.Where(c => c.TenderId == id || (c.Bid != null && c.Bid.TenderId == id)),
            "bid" => threads.Where(c => c.BidId == id),
            _ => threads.Where(c => false)
        };

    internal static IQueryable<Clarification> VisibleTo(this IQueryable<Clarification> threads, string userId, bool officer) =>
        threads.Where(c => c.SenderUserId == userId || c.RecipientUserId == userId ||
            (officer && c.PurchaseRequest != null &&
                c.SenderUserId == c.PurchaseRequest.RequestedByUserId &&
                (c.PurchaseRequest.ReviewedByUserId == userId ||
                 (c.PurchaseRequest.ReviewedByUserId == null && c.PurchaseRequest.Status == "Submitted"))));
}
