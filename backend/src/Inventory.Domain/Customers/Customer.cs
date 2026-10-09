using System.Text.RegularExpressions;
using Inventory.Domain.Common;

namespace Inventory.Domain.Customers;

/// <summary>A company that receives dispatches. Not a tenant; customers do not sign in (v1).</summary>
public sealed partial class Customer : Entity, IAuditable
{
    private readonly List<CustomerAddress> _addresses = [];

    private Customer() { }

    public string Reference { get; private set; } = null!;
    public string CompanyName { get; private set; } = null!;
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? BillingLine1 { get; private set; }
    public string? BillingLine2 { get; private set; }
    public string? BillingCity { get; private set; }
    public string? BillingCountry { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<CustomerAddress> Addresses => _addresses;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static Customer Create(string reference, string companyName)
    {
        var c = new Customer
        {
            Reference = Text.Code(reference, ReferencePattern(), "customer.reference_invalid", "Customer reference must be 1–32 letters, digits or hyphens."),
        };
        c.Rename(companyName);
        return c;
    }

    public void Rename(string companyName) =>
        CompanyName = Text.Required(companyName, 200, "customer.name_invalid", "Company name");

    public void SetContact(string? contactName, string? phone, string? email)
    {
        ContactName = Text.Optional(contactName, 120, "customer.contact_invalid", "Contact name");
        Phone = Text.Optional(phone, 40, "customer.phone_invalid", "Phone");
        Email = Text.Optional(email, 256, "customer.email_invalid", "Email");
    }

    public void SetBilling(string? line1, string? line2, string? city, string? country)
    {
        BillingLine1 = Text.Optional(line1, 200, "customer.address_invalid", "Billing address line 1");
        BillingLine2 = Text.Optional(line2, 200, "customer.address_invalid", "Billing address line 2");
        BillingCity = Text.Optional(city, 100, "customer.address_invalid", "Billing city");
        BillingCountry = Text.Optional(country, 100, "customer.address_invalid", "Billing country");
    }

    public void SetNotes(string? notes) => Notes = Text.Optional(notes, 2000, "customer.notes_invalid", "Notes");

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public CustomerAddress AddAddress(string label, string line1, string? line2, string? city, string? country, string? contactName, string? phone, bool isDefault)
    {
        var normalized = Text.Required(label, 60, "address.label_invalid", "Address label");
        if (_addresses.Any(a => string.Equals(a.Label, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException("address.label_duplicate", $"This customer already has an address called “{normalized}”.", isConflict: true);
        }
        var address = new CustomerAddress(Id);
        address.Update(normalized, line1, line2, city, country, contactName, phone);
        _addresses.Add(address);
        if (isDefault || !_addresses.Any(a => a.IsDefault && a.IsActive))
        {
            MakeDefault(address);
        }
        return address;
    }

    public CustomerAddress UpdateAddress(Guid addressId, string label, string line1, string? line2, string? city, string? country, string? contactName, string? phone, bool isDefault)
    {
        var address = FindAddress(addressId);
        var normalized = Text.Required(label, 60, "address.label_invalid", "Address label");
        if (_addresses.Any(a => a.Id != addressId && string.Equals(a.Label, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException("address.label_duplicate", $"This customer already has an address called “{normalized}”.", isConflict: true);
        }
        address.Update(normalized, line1, line2, city, country, contactName, phone);
        if (isDefault) MakeDefault(address);
        return address;
    }

    /// <summary>Deactivated addresses stay attached to past dispatches but cannot be chosen for new ones.</summary>
    public void DeactivateAddress(Guid addressId)
    {
        var address = FindAddress(addressId);
        address.SetActive(false);
        if (address.IsDefault)
        {
            address.SetDefault(false);
            var next = _addresses.FirstOrDefault(a => a.IsActive);
            if (next is not null) next.SetDefault(true);
        }
    }

    public void ActivateAddress(Guid addressId)
    {
        var address = FindAddress(addressId);
        address.SetActive(true);
        if (!_addresses.Any(a => a.IsDefault && a.IsActive)) address.SetDefault(true);
    }

    private void MakeDefault(CustomerAddress address)
    {
        if (!address.IsActive) throw new DomainRuleException("address.inactive_default", "An inactive address cannot be the default.");
        foreach (var a in _addresses) a.SetDefault(a == address);
    }

    private CustomerAddress FindAddress(Guid id) =>
        _addresses.SingleOrDefault(a => a.Id == id) ?? throw new DomainRuleException("address.not_found", "Address not found.");

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$")]
    private static partial Regex ReferencePattern();
}

public sealed class CustomerAddress : Entity
{
    private CustomerAddress() { }

    internal CustomerAddress(Guid customerId) => CustomerId = customerId;

    public Guid CustomerId { get; private set; }
    public string Label { get; private set; } = null!;
    public string Line1 { get; private set; } = null!;
    public string? Line2 { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;

    internal void Update(string label, string line1, string? line2, string? city, string? country, string? contactName, string? phone)
    {
        Label = label;
        Line1 = Text.Required(line1, 200, "address.line1_invalid", "Address line 1");
        Line2 = Text.Optional(line2, 200, "address.line2_invalid", "Address line 2");
        City = Text.Optional(city, 100, "address.city_invalid", "City");
        Country = Text.Optional(country, 100, "address.country_invalid", "Country");
        ContactName = Text.Optional(contactName, 120, "address.contact_invalid", "Contact name");
        Phone = Text.Optional(phone, 40, "address.phone_invalid", "Phone");
    }

    internal void SetDefault(bool value) => IsDefault = value;

    internal void SetActive(bool value) => IsActive = value;
}
