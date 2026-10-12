using Inventory.Domain.Common;

namespace Inventory.Domain.Documents;

public enum DocumentType
{
    /// <summary>Unsigned list of the goods, printed when the dispatch ships.</summary>
    PackingSlip,
    /// <summary>Signed delivery note, produced after proof of delivery and emailed to the customer.</summary>
    DeliveryNote,
}

public enum DocumentStatus
{
    Pending,
    Generated,
    Failed,
}

/// <summary>
/// A version of an HTML (Liquid) template. Saving a template creates a new version; old versions are kept so every
/// generated document can be traced to the template that produced it.
/// </summary>
public sealed class DocumentTemplate : Entity
{
    public const int MaxHtmlLength = 200_000;

    private DocumentTemplate() { }

    public DocumentType Type { get; private set; }
    public int Version { get; private set; }
    public string Html { get; private set; } = null!;
    public bool IsCurrent { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    public static DocumentTemplate Create(DocumentType type, int version, string html, string? comment, Guid? userId, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(html)) throw new DomainRuleException("template.empty", "The template cannot be empty.");
        if (html.Length > MaxHtmlLength) throw new DomainRuleException("template.too_large", "The template is too large (200 000 characters maximum).");
        return new DocumentTemplate
        {
            Type = type, Version = version, Html = html, IsCurrent = true,
            Comment = Text.Optional(comment, 200, "template.comment_invalid", "Comment"),
            CreatedAt = at, CreatedBy = userId,
        };
    }

    public void Retire() => IsCurrent = false;
}

/// <summary>A PDF produced for a dispatch. Generated after the transaction that triggered it; failures are retried.</summary>
public sealed class DispatchDocument : Entity
{
    private DispatchDocument() { }

    public Guid DispatchId { get; private set; }
    public DocumentType Type { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Pending;
    public Guid? FileId { get; private set; }
    public Guid? TemplateId { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Email the PDF to the customer once generated (delivery notes).</summary>
    public bool EmailToCustomer { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public DateTimeOffset? GeneratedAt { get; private set; }

    public static DispatchDocument Request(Guid dispatchId, DocumentType type, bool emailToCustomer, Guid? userId, DateTimeOffset at) =>
        new() { DispatchId = dispatchId, Type = type, EmailToCustomer = emailToCustomer, RequestedAt = at, RequestedBy = userId };

    public void MarkGenerated(Guid fileId, Guid templateId, DateTimeOffset at)
    {
        Status = DocumentStatus.Generated;
        FileId = fileId;
        TemplateId = templateId;
        GeneratedAt = at;
        Attempts++;
        LastError = null;
    }

    public void MarkFailed(string error, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        Status = Attempts >= maxAttempts ? DocumentStatus.Failed : DocumentStatus.Pending;
    }
}

/// <summary>Company details printed on documents and used in emails (one row).</summary>
public sealed class CompanyProfile : Entity, IAuditable
{
    public const string DefaultEmailSubject = "Delivery note {{ dispatch.number }}";
    public const string DefaultEmailBody =
        "Dear customer,\n\nPlease find attached the signed delivery note {{ dispatch.number }}" +
        "{% if dispatch.customer_reference %} for your reference {{ dispatch.customer_reference }}{% endif %}.\n\n" +
        "Received by {{ delivery.receiver_name }} on {{ delivery.delivered_at }}.\n\nKind regards,\n{{ company.name }}";

    private CompanyProfile() { }

    public string Name { get; private set; } = null!;
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? RegistrationNumber { get; private set; }
    public string? VatNumber { get; private set; }
    public string? FooterText { get; private set; }
    public Guid? LogoFileId { get; private set; }
    /// <summary>IANA time zone used to print dates and times on documents.</summary>
    public string TimeZone { get; private set; } = "Indian/Mauritius";
    public string DeliveryEmailSubject { get; private set; } = DefaultEmailSubject;
    public string DeliveryEmailBody { get; private set; } = DefaultEmailBody;
    /// <summary>Extra internal recipients (copy) of delivery-note emails, comma separated.</summary>
    public string? DeliveryEmailBcc { get; private set; }
    /// <summary>Recipients (comma separated) of the daily low-stock email; null = no email.</summary>
    public string? LowStockAlertEmails { get; private set; }
    /// <summary>Company-local date the last low-stock email was considered (sent at most once a day).</summary>
    public DateOnly? LowStockAlertCheckedOn { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static CompanyProfile Create(string name) => new() { Name = Text.Required(name, 200, "company.name_required", "Company name") };

    public void Update(string name, string? line1, string? line2, string? city, string? country, string? phone, string? email, string? website,
        string? registration, string? vat, string? footer, string timeZone)
    {
        Name = Text.Required(name, 200, "company.name_required", "Company name");
        AddressLine1 = Text.Optional(line1, 200, "company.address_invalid", "Address line 1");
        AddressLine2 = Text.Optional(line2, 200, "company.address_invalid", "Address line 2");
        City = Text.Optional(city, 100, "company.address_invalid", "City");
        Country = Text.Optional(country, 100, "company.address_invalid", "Country");
        Phone = Text.Optional(phone, 40, "company.phone_invalid", "Phone");
        Email = Text.Optional(email, 256, "company.email_invalid", "Email");
        Website = Text.Optional(website, 200, "company.website_invalid", "Website");
        RegistrationNumber = Text.Optional(registration, 60, "company.registration_invalid", "Registration number");
        VatNumber = Text.Optional(vat, 60, "company.vat_invalid", "VAT number");
        FooterText = Text.Optional(footer, 1000, "company.footer_invalid", "Footer");
        if (!TimeZones.TryFind(timeZone, out _))
        {
            throw new DomainRuleException("company.time_zone_invalid", "Unknown time zone. Use an IANA name such as Indian/Mauritius or Europe/Zurich.");
        }
        TimeZone = timeZone.Trim();
    }

    public void SetLowStockAlert(string? recipients) =>
        LowStockAlertEmails = Text.Optional(recipients, 500, "company.alert_emails_invalid", "Low-stock email recipients");

    public void MarkLowStockAlertChecked(DateOnly day) => LowStockAlertCheckedOn = day;

    public void SetEmail(string subject, string body, string? bcc)
    {
        DeliveryEmailSubject = Text.Required(subject, 200, "company.email_subject_required", "Email subject");
        DeliveryEmailBody = Text.Required(body, 5000, "company.email_body_required", "Email text");
        DeliveryEmailBcc = Text.Optional(bcc, 500, "company.bcc_invalid", "Copy to");
    }

    public void SetLogo(Guid? fileId) => LogoFileId = fileId;
}
