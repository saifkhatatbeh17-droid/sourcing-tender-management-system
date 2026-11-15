using System.ComponentModel.DataAnnotations;

namespace SourcingTenderManagement.Models;

public sealed class ContactMessageInputModel
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(150)]
    public string Subject { get; set; } = "";

    [Required, StringLength(2000)]
    public string Message { get; set; } = "";
}
