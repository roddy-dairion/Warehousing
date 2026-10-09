namespace Inventory.Application.Warehouses;

public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    bool IsActive);
