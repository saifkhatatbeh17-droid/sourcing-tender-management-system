using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.FaceEnrollment;

public sealed class FaceEnrollmentRequest
{
    [Required, StringLength(1024)] public string Password { get; set; } = "";
    public bool Consent { get; set; }
    public bool Replace { get; set; }
    [StringLength(24)] public string? RowVersion { get; set; }
    public float[][]? Descriptors { get; set; }
}

public sealed record FaceEnrollmentPage(bool Enrolled, int SampleCount, DateTime? EnrolledAt, string? RowVersion);

// Face sign-in on the login page (password + face, or the local demo with email + face).
public sealed class FaceSignInInput : LoginViewModel
{
    [Required, MinLength(128), MaxLength(128)] public float[]? Descriptor { get; set; }
    [Required, StringLength(150)] public string ModelIdentifier { get; set; } = "";
}

public sealed class FaceDemoSignInInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(128), MaxLength(128)] public float[]? Descriptor { get; set; }
    [Required, StringLength(150)] public string ModelIdentifier { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed class FaceVerificationRequest
{
    [Required] public float[]? Descriptor { get; set; }
    [Required, StringLength(150)] public string ModelIdentifier { get; set; } = "";
}

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
