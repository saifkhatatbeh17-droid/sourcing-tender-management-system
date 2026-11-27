using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

// Form data for the public Contact page and the Admin list of contact messages.
public sealed class ContactMessageInput
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = "";

    private string email = "";

    [Required, StringLength(254), EmailAddress]
    public string Email
    {
        get => email;
        set => email = value?.Trim() ?? "";
    }

    [Required, StringLength(150)]
    public string Subject { get; set; } = "";

    [Required, StringLength(2000)]
    public string Message { get; set; } = "";

    public static bool IsValidAddress(string address) =>
        !address.Any(char.IsControl) && MailAddress.TryCreate(address, out var parsed)
        && string.Equals(parsed.Address, address, StringComparison.Ordinal);
}

public sealed record ContactMessagesPage(
    IReadOnlyList<ContactMessage> Messages, string? Search, string? Status, int Page, int TotalPages);
