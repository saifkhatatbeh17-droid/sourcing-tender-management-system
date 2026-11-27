namespace SourcingTenderManagement.Models.Entities;

// Database table: a message sent from the public Contact form.
public sealed class ContactMessage
{
    public const string Unread = "Unread";
    public const string Read = "Read";

    public int ContactMessageId { get; set; }
    public string? UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = Unread;

    public bool IsUnread => Status == Unread;
}
