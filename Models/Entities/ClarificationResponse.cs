using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: one message inside a clarification thread.
public class ClarificationResponse
{
    public int ClarificationResponseId { get; set; }

    public int ClarificationId { get; set; }

    public string RespondedByUserId { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Clarification Clarification { get; set; } = null!;

    public ApplicationUser RespondedByUser { get; set; } = null!;
}
