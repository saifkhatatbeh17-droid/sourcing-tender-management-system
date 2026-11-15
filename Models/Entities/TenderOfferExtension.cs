namespace SourcingTenderManagement.Models.Entities;

public sealed class TenderOfferExtension
{
    public const int MaximumRound = 4;
    public const int RequiredCompliantOffers = 3;
    public const int MinimumResponseHours = 24;

    public int TenderId { get; set; }
    public int Round { get; set; }
    public DateTime PreviousDeadline { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime Deadline { get; set; }
    public int PreviousCompliantOffers { get; set; }
    public string Outreach { get; set; } = "";
}
