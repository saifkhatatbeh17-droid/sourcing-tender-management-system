using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models.FaceLogin;

// Data posted when a user signs in with their face.
public sealed class FaceSignInInput : LoginViewModel
{
    [Required, MinLength(128), MaxLength(128)] public float[]? Descriptor { get; set; }
    [Required, StringLength(150)] public string ModelIdentifier { get; set; } = "";
}
