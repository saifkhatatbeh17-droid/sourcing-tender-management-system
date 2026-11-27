using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a notification shown in a user's bell.
public class Notification
{
    public int NotificationId { get; set; }

    public string UserId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? RelativeUrl { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
}
