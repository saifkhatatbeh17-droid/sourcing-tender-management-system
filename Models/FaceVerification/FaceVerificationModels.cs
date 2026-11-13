using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.FaceVerification;

public sealed class FaceVerificationRequest
{
    [Required] public float[]? Descriptor { get; set; }
    [Required, StringLength(150)] public string ModelIdentifier { get; set; } = "";
}

public sealed record FaceVerificationPage(bool Enrolled);

public sealed record FaceVerificationResult(string Status, string? Message = null)
{
    public static FaceVerificationResult Failed() => new("Verification Failed",
        "Face verification is unavailable. Check your enrollment or capture a new sample.");
}

public sealed class FaceVerificationOptions
{
    // Informational similarity threshold only, not a calibrated identity/security guarantee.
    public double MatchThreshold { get; set; } = 0.5;
}
