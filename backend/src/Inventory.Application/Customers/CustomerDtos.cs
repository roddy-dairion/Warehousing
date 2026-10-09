namespace Inventory.Application.Customers;

public sealed record CustomerListItemDto(
    Guid Id,
    string Reference,
    string CompanyName,
    string? ContactName,
    string? Phone,
    string? Email,
    string? City,
    int AddressCount,
    bool IsActive);

public sealed record CustomerAddressDto(
    Guid Id,
    string Label,
    string Line1,
    string? Line2,
    string? City,
    string? Country,
    string? ContactName,
    string? Phone,
    bool IsDefault,
    bool IsActive);

public sealed record CustomerDto(
    Guid Id,
    string Reference,
    string CompanyName,
    string? ContactName,
    string? Phone,
    string? Email,
    string? BillingLine1,
    string? BillingLine2,
    string? BillingCity,
    string? BillingCountry,
    string? Notes,
    bool IsActive,
    IReadOnlyList<CustomerAddressDto> Addresses,
    uint Version);

public sealed record AddressRequest(string Label, string Line1, string? Line2, string? City, string? Country, string? ContactName, string? Phone, bool IsDefault);

public sealed record CreateCustomerRequest(
    string Reference,
    string CompanyName,
    string? ContactName,
    string? Phone,
    string? Email,
    string? BillingLine1,
    string? BillingLine2,
    string? BillingCity,
    string? BillingCountry,
    string? Notes,
    IReadOnlyList<AddressRequest>? Addresses);

public sealed record UpdateCustomerRequest(
    string CompanyName,
    string? ContactName,
    string? Phone,
    string? Email,
    string? BillingLine1,
    string? BillingLine2,
    string? BillingCity,
    string? BillingCountry,
    string? Notes,
    uint Version);

/// <summary>Address changes carry the customer's version.</summary>
public sealed record AddressChangeRequest(AddressRequest Address, uint Version);
