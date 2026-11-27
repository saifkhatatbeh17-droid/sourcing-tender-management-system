namespace SourcingTenderManagement.Models.TopCommittee;
// Data shown on the CEO & Chiefs notifications page.
public sealed record CommitteeNotificationItem(int Id,string Title,string Message,bool IsRead,DateTime CreatedAt,int? TenderId);
public sealed record CommitteeNotificationPage(IReadOnlyList<CommitteeNotificationItem> Items,int Total,int Unread,int Page);
