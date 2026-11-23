using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.Entities;

public sealed class BidGuarantee
{
    public int BidGuaranteeId { get; set; }
    public int BidId { get; set; }
    [MaxLength(10)] public string Kind { get; set; } = "";
    [MaxLength(100)] public string ReferenceNumber { get; set; } = "";
    [MaxLength(150)] public string Issuer { get; set; } = "";
    [MaxLength(10)] public string Coverage { get; set; } = "Product";
    [MaxLength(500)] public string Scope { get; set; } = "";
    public DateOnly StartsOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    [MaxLength(32)] public string VerificationToken { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(255)] public string OriginalFileName { get; set; } = "";
    [MaxLength(80)] public string StoredFileName { get; set; } = "";
    [MaxLength(50)] public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Bid Bid { get; set; } = null!;
    public string Label => Kind == "Bank" ? "Bank guarantee" : "Legal warranty";
    public int DurationDays => ExpiresOn.DayNumber - StartsOn.DayNumber + 1;
}
