using Inventory.Domain.Warehouses;

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
    bool IsActive,
    uint Version);

public sealed record WarehouseContactDto(string? AddressLine1, string? AddressLine2, string? City, string? Country, string? Phone, string? Email);

public sealed record CreateWarehouseRequest(string Code, string Name, string? AddressLine1, string? AddressLine2, string? City, string? Country, string? Phone, string? Email);

public sealed record UpdateWarehouseRequest(string Name, string? AddressLine1, string? AddressLine2, string? City, string? Country, string? Phone, string? Email, uint Version);

public sealed record LocationDto(
    Guid Id,
    Guid WarehouseId,
    string Code,
    string? Zone,
    LocationType Type,
    string? Barcode,
    bool IsPickable,
    bool IsSystem,
    bool IsActive,
    uint Version);

public sealed class LocationListQuery : Common.ListQuery
{
    public LocationType? Type { get; set; }
}

public sealed record CreateLocationRequest(string Code, LocationType Type, string? Zone, string? Barcode, bool? IsPickable);

public sealed record UpdateLocationRequest(LocationType Type, string? Zone, string? Barcode, bool IsPickable, uint Version);
