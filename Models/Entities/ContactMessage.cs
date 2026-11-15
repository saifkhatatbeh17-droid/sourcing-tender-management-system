namespace SourcingTenderManagement.Models.Entities;

public sealed class ContactMessage
{
    public int ContactMessageId { get; set; }
    public string? UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = "Unread";
    public Guid? ReplyAttemptId { get; set; }
    public string? ReplyText { get; set; }
    public DateTime? RepliedAt { get; set; }
    public string? RepliedByUserId { get; set; }

    public string AdminDetailsUrl => FormattableString.Invariant($"/Admin/ContactMessageDetails/{ContactMessageId}");
}
