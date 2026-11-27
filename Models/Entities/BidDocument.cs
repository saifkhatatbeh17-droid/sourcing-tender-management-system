namespace SourcingTenderManagement.Models.Entities;

// Database table: a file a supplier attached to their offer.
public class BidDocument
{
    public int BidDocumentId { get; set; }

    public int BidId { get; set; }

    public string DocumentType { get; set; } = null!;

    public string OriginalFileName { get; set; } = null!;

    public string StoredFileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long FileSizeBytes { get; set; }

    public DateTime UploadedAt { get; set; }

    public Bid Bid { get; set; } = null!;
}
