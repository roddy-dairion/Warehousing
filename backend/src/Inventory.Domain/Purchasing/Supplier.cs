using System.Text.RegularExpressions;
using Inventory.Domain.Common;

namespace Inventory.Domain.Purchasing;

/// <summary>A company (or the company's own production) that goods are ordered from.</summary>
public sealed partial class Supplier : Entity, IAuditable
{
    private Supplier() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    /// <summary>Usual days between ordering and delivery; proposes the expected date of new orders.</summary>
    public int? LeadTimeDays { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static Supplier Create(string code, string name)
    {
        var s = new Supplier
        {
            Code = Text.Code(code, CodePattern(), "supplier.code_invalid", "Supplier code must be 1–32 letters, digits or hyphens."),
        };
        s.Rename(name);
        return s;
    }

    public void Rename(string name) => Name = Text.Required(name, 200, "supplier.name_invalid", "Supplier name");

    public void SetContact(string? contactName, string? phone, string? email)
    {
        ContactName = Text.Optional(contactName, 120, "supplier.contact_invalid", "Contact name");
        Phone = Text.Optional(phone, 40, "supplier.phone_invalid", "Phone");
        Email = Text.Optional(email, 256, "supplier.email_invalid", "Email");
    }

    public void SetAddress(string? line1, string? line2, string? city, string? country)
    {
        AddressLine1 = Text.Optional(line1, 200, "supplier.address_invalid", "Address line 1");
        AddressLine2 = Text.Optional(line2, 200, "supplier.address_invalid", "Address line 2");
        City = Text.Optional(city, 100, "supplier.address_invalid", "City");
        Country = Text.Optional(country, 100, "supplier.address_invalid", "Country");
    }

    public void SetTerms(int? leadTimeDays, string? notes)
    {
        if (leadTimeDays is < 0 or > 365) throw new DomainRuleException("supplier.lead_time_invalid", "Lead time must be 0–365 days.");
        LeadTimeDays = leadTimeDays;
        Notes = Text.Optional(notes, 2000, "supplier.notes_invalid", "Notes");
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$")]
    private static partial Regex CodePattern();
}
