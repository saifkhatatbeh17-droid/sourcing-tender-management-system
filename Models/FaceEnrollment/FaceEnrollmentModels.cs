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
