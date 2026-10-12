using Inventory.Application.Documents;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Inventory.Infrastructure.Documents;

public enum EmailMode
{
    /// <summary>Nothing is sent; queued emails stay pending.</summary>
    Disabled,
    /// <summary>Writes .eml files to <see cref="EmailOptions.PickupDirectory"/> (development).</summary>
    Pickup,
    Smtp,
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    public EmailMode Mode { get; set; } = EmailMode.Disabled;
    public string From { get; set; } = "dispatch@hellocherie.ch";
    public string FromName { get; set; } = "HelloCherie Dispatch";
    public string PickupDirectory { get; set; } = "App_Data/mail";
    public string SmtpHost { get; set; } = "smtp.office365.com";
    public int SmtpPort { get; set; } = 587;
    /// <summary>StartTls (port 587), SslOnConnect (465), Auto or None.</summary>
    public string SmtpSecurity { get; set; } = "StartTls";
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public int PollSeconds { get; set; } = 15;
    /// <summary>Run the background worker that sends queued emails and retries pending documents.</summary>
    public bool WorkerEnabled { get; set; } = true;
}

internal sealed class MailKitEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        foreach (var to in email.To) message.To.Add(MailboxAddress.Parse(to));
        foreach (var bcc in email.Bcc) message.Bcc.Add(MailboxAddress.Parse(bcc));
        message.Subject = email.Subject;
        var body = new BodyBuilder { TextBody = email.TextBody };
        foreach (var a in email.Attachments) body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        message.Body = body.ToMessageBody();

        switch (o.Mode)
        {
            case EmailMode.Pickup:
                Directory.CreateDirectory(o.PickupDirectory);
                var path = Path.Combine(o.PickupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
                await message.WriteToAsync(path, cancellationToken);
                break;
            case EmailMode.Smtp:
                using (var client = new SmtpClient())
                {
                    var security = Enum.TryParse<SecureSocketOptions>(o.SmtpSecurity, true, out var s) ? s : SecureSocketOptions.StartTls;
                    await client.ConnectAsync(o.SmtpHost, o.SmtpPort, security, cancellationToken);
                    if (!string.IsNullOrEmpty(o.SmtpUsername)) await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? string.Empty, cancellationToken);
                    await client.SendAsync(message, cancellationToken);
                    await client.DisconnectAsync(true, cancellationToken);
                }
                break;
            default:
                throw new InvalidOperationException("Email sending is disabled (Email:Mode).");
        }
    }
}
