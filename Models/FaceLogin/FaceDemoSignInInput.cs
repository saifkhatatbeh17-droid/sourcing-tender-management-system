using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.FaceLogin;

// Data posted by the demo face sign-in button.
public sealed class FaceDemoSignInInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(128), MaxLength(128)] public float[]? Descriptor { get; set; }
    [Required, StringLength(150)] public string ModelIdentifier { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
