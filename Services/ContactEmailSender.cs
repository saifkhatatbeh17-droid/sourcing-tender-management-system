using System.Net;
using System.Net.Mail;
using System.Text;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services;

public enum ContactEmailResult { Accepted, NotConfigured, Rejected, Uncertain }

public sealed class ContactEmailSender(IConfiguration configuration, ILogger<ContactEmailSender> logger)
{
    public const string DemoModeKey = "ContactEmail:DemoMode";
    public bool IsDemoMode => configuration.GetValue<bool>(DemoModeKey);

    public async Task<bool> SendConfirmationAsync(ContactMessage message, CancellationToken cancellationToken)
        => await SendAsync(message, "PharmaCare received your message",
            $"Hello {message.FullName},\n\nThank you for contacting PharmaCare. We received your message.\n\nSubject: {message.Subject}\n\nYour message:\n{message.Message}\n\nKind regards,\nThe PharmaCare Team",
            null, cancellationToken) == ContactEmailResult.Accepted;

    public Task<ContactEmailResult> SendReplyAsync(ContactMessage message, string reply, Guid attemptId)
        => SendAsync(message, "PharmaCare reply to your contact message",
            $"Thank you for contacting PharmaCare.\n\nOriginal subject: {message.Subject}\n\n{reply}\n\nKind regards,\nThe PharmaCare Team",
            ReplyMessageId(attemptId), CancellationToken.None);

    public static string ReplyMessageId(Guid attemptId) => $"<{attemptId:N}@pharmacare.invalid>";

    private async Task<ContactEmailResult> SendAsync(ContactMessage message, string subject, string body,
        string? messageId, CancellationToken cancellationToken)
    {
        if (IsDemoMode) return ContactEmailResult.NotConfigured;

        var settings = configuration.GetSection("ContactEmail");
        var host = settings["Host"];
        var from = settings["From"];
        var user = settings["Username"];
        var password = settings["Password"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(from, out var sender) ||
            !int.TryParse(settings["Port"] ?? "587", out var port) || port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(user) != string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Contact email skipped for message {ContactId}. Check SMTP configuration.", message.ContactMessageId);
            return ContactEmailResult.NotConfigured;
        }

        var sending = false;
        try
        {
            using var email = new MailMessage
            {
                From = new MailAddress(sender.Address, "PharmaCare"),
                Subject = subject,
                Body = body,
                BodyEncoding = Encoding.UTF8
            };
            var recipient = new MailAddress(message.Email);
            if (messageId != null && !string.Equals(recipient.Address, message.Email.Trim(), StringComparison.Ordinal))
                return ContactEmailResult.Rejected;
            email.To.Add(recipient);
            if (messageId != null) email.Headers.Add("Message-ID", messageId);
            using var client = new SmtpClient(host, port) { EnableSsl = true };
            if (!string.IsNullOrWhiteSpace(user))
                client.Credentials = new NetworkCredential(user, password);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            sending = true;
            await client.SendMailAsync(email, timeout.Token);
            return ContactEmailResult.Accepted;
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning("Contact email failed for message {ContactId}, SMTP message ID {SmtpMessageId} ({ErrorType}).",
                message.ContactMessageId, messageId, ex.GetType().Name);
            if (!sending ||
                ex is SmtpFailedRecipientException { StatusCode: >= (SmtpStatusCode)400 and <= (SmtpStatusCode)599 } ||
                ex is SmtpException { StatusCode: (SmtpStatusCode)530 or (SmtpStatusCode)535 })
                return ContactEmailResult.Rejected;

            // A timeout or lost connection can occur after SMTP has accepted the message.
            return ContactEmailResult.Uncertain;
        }
    }
}
