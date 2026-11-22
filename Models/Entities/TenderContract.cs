namespace SourcingTenderManagement.Models.Entities;

public sealed class TenderContract
{
    public int TenderContractId { get; set; }
    public int TenderId { get; set; }
    public int BidId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public required string GeneratedByUserId { get; set; }
    public required byte[] GeneratedPdf { get; set; }
    public string? SignedFileName { get; set; }
    public string? SignedStoredFileName { get; set; }
    public string? SignedContentType { get; set; }
    public long? SignedSizeBytes { get; set; }
    public string? SignedUploadedByUserId { get; set; }
    public DateTime? SignedUploadedAt { get; set; }
    public string? Notes { get; set; }
}
