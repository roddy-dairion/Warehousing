using Inventory.Domain.Common;

namespace Inventory.Domain.Documents;

public enum EmailStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// Outgoing email (outbox). Written in the same transaction as the business change, sent by a background worker with
/// retries, so a mail-server outage never blocks or rolls back a delivery.
/// </summary>
public sealed class EmailMessage : Entity
{
    public const int MaxAttempts = 8;

    private readonly List<Guid> _attachmentFileIds = [];

    private EmailMessage() { }

    public string To { get; private set; } = null!;
    public string? Bcc { get; private set; }
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public IReadOnlyList<Guid> AttachmentFileIds => _attachmentFileIds;
    public EmailStatus Status { get; private set; } = EmailStatus.Pending;
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? RelatedType { get; private set; }
    public Guid? RelatedId { get; private set; }

    public static EmailMessage Create(string to, string? bcc, string subject, string body, IEnumerable<Guid> attachmentFileIds,
        string? relatedType, Guid? relatedId, DateTimeOffset at)
    {
        var m = new EmailMessage
        {
            To = Text.Required(to, 500, "email.to_required", "Recipient"),
            Bcc = Text.Optional(bcc, 500, "email.bcc_invalid", "Copy to"),
            Subject = Text.Required(subject, 300, "email.subject_required", "Subject"),
            Body = Text.Required(body, 20_000, "email.body_required", "Body"),
            RelatedType = relatedType,
            RelatedId = relatedId,
            CreatedAt = at,
            NextAttemptAt = at,
        };
        m._attachmentFileIds.AddRange(attachmentFileIds);
        return m;
    }

    public void MarkSent(DateTimeOffset at)
    {
        Status = EmailStatus.Sent;
        SentAt = at;
        Attempts++;
        LastError = null;
    }

    /// <summary>Records a failed attempt and schedules the next one (1, 2, 4 … minutes, capped at 2 hours).</summary>
    public void MarkFailed(string error, DateTimeOffset at)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
            return;
        }
        NextAttemptAt = at.AddMinutes(Math.Min(120, Math.Pow(2, Attempts - 1)));
    }
}
