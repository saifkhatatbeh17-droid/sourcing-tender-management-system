namespace SourcingTenderManagement.Models.TopCommittee;

public sealed class CommitteeNotificationBellViewModel : NotificationBellViewModel
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int Pages { get; init; }
    public string ReturnUrl { get; init; } = "";
    public string? PreviousUrl { get; init; }
    public string? NextUrl { get; init; }
    public bool OpenOnLoad { get; init; }
}
