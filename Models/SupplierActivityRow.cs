namespace SourcingTenderManagement.Models;

public sealed record SupplierTenderActivity(int TenderId, string Title, int BidId, DateTime SubmittedAt,
    string Outcome, decimal Total, string Currency, string? Compliance);

public sealed record SupplierActivityRow(int SupplierId, string Company, string Status, string RegistrationNumber,
    string? Category, IReadOnlyList<SupplierTenderActivity> History)
{
    public int Awarded => History.Count(row => row.Outcome == "Awarded");
    public int Rejected => History.Count(row => row.Outcome == "Rejected");
}
