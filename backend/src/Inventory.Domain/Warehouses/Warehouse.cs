using System.Text.RegularExpressions;
using Inventory.Domain.Common;

namespace Inventory.Domain.Warehouses;

/// <summary>A physical storage and dispatch location (branch).</summary>
public sealed partial class Warehouse : Entity, IAuditable
{
    public const int CodeMaxLength = 16;
    public const int NameMaxLength = 120;

    private Warehouse() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static Warehouse Create(string code, string name)
    {
        var warehouse = new Warehouse();
        warehouse.SetCode(code);
        warehouse.Rename(name);
        return warehouse;
    }

    public void Rename(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is 0 or > NameMaxLength)
        {
            throw new DomainRuleException("warehouse.name_invalid", $"Warehouse name is required and must be at most {NameMaxLength} characters.");
        }
        Name = name;
    }

    public void SetContact(string? addressLine1, string? addressLine2, string? city, string? country, string? phone, string? email)
    {
        AddressLine1 = Clean(addressLine1);
        AddressLine2 = Clean(addressLine2);
        City = Clean(city);
        Country = Clean(country);
        Phone = Clean(phone);
        Email = Clean(email);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private void SetCode(string code)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
        {
            throw new DomainRuleException("warehouse.code_invalid", $"Warehouse code must be 2–{CodeMaxLength} letters, digits or hyphens.");
        }
        Code = code;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,15}$")]
    private static partial Regex CodePattern();
}
