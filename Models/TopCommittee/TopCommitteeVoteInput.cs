using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.TopCommittee;

public sealed class CommitteeVoteInput : IValidatableObject
{
    [Range(1, int.MaxValue)] public int TenderId { get; set; }
    // A stale-page guard only. The bid to award always comes from the database nomination.
    [Range(1, int.MaxValue)] public int ExpectedNominatedBidId { get; set; }
    [Required, RegularExpression("^(Approved|Rejected)$", ErrorMessage = "Choose Approve or Reject.")]
    public string Decision { get; set; } = "";
    [StringLength(4000)] public string? Notes { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm your final vote before submitting.")]
    public bool Confirmed { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Decision == "Rejected" && string.IsNullOrWhiteSpace(Notes))
            yield return new ValidationResult("A rejection justification is required.", [nameof(Notes)]);
    }
}
