using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a user's registered face data (encrypted) and consent.
public sealed class UserFaceCredential
{
    public string UserId { get; set; } = null!;
    public byte[]? ProtectedTemplates { get; set; }
    public short? TemplateFormatVersion { get; set; }
    public string? ModelIdentifier { get; set; }
    public short SampleCount { get; set; }
    public DateTime EnrollmentPromptedAt { get; set; }
    public string? ConsentVersion { get; set; }
    public DateTime? ConsentGrantedAt { get; set; }
    public DateTime? EnrolledAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
