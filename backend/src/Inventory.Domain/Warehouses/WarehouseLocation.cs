using System.Text.RegularExpressions;
using Inventory.Domain.Common;

namespace Inventory.Domain.Warehouses;

public enum LocationType
{
    /// <summary>Normal shelf/bin. The only type that can be pickable.</summary>
    Storage,
    /// <summary>Goods-in area. Stock here is not available for dispatch until put away.</summary>
    Receiving,
    /// <summary>Dispatch dock / staging.</summary>
    Dispatch,
    /// <summary>Damaged, expired or blocked stock.</summary>
    Quarantine,
}

/// <summary>A bin, shelf or area inside a warehouse. Part of the stock key.</summary>
public sealed partial class WarehouseLocation : Entity, IAuditable
{
    public const int CodeMaxLength = 32;

    /// <summary>Areas every warehouse gets automatically.</summary>
    public static readonly IReadOnlyList<(string Code, LocationType Type)> SystemLocations =
    [
        ("RECEIVING", LocationType.Receiving),
        ("DOCK", LocationType.Dispatch),
        ("QUARANTINE", LocationType.Quarantine),
    ];

    private WarehouseLocation() { }

    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public string? Zone { get; private set; }
    public LocationType Type { get; private set; }
    public string? Barcode { get; private set; }
    public bool IsPickable { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WarehouseLocation Create(Guid warehouseId, string code, LocationType type, string? zone = null, string? barcode = null, bool? isPickable = null)
    {
        var location = new WarehouseLocation
        {
            WarehouseId = warehouseId,
            Code = Text.Code(code, CodePattern(), "location.code_invalid", $"Bin code must be 1–{CodeMaxLength} letters, digits, dots, hyphens or underscores."),
        };
        location.Update(zone, type, barcode, isPickable ?? type == LocationType.Storage);
        return location;
    }

    public static WarehouseLocation CreateSystem(Guid warehouseId, string code, LocationType type)
    {
        var location = Create(warehouseId, code, type);
        location.IsSystem = true;
        return location;
    }

    public void Update(string? zone, LocationType type, string? barcode, bool isPickable)
    {
        if (IsSystem && type != Type)
        {
            throw new DomainRuleException("location.system_type", "The type of a built-in area cannot be changed.");
        }
        if (isPickable && type != LocationType.Storage)
        {
            throw new DomainRuleException("location.pickable_type", "Only storage bins can be pickable.");
        }
        Zone = Text.Optional(zone, 32, "location.zone_invalid", "Zone");
        Type = type;
        Barcode = Barcodes.NormalizeOptional(barcode);
        IsPickable = isPickable;
    }

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (IsSystem) throw new DomainRuleException("location.system_deactivate", "Built-in areas cannot be deactivated.");
        IsActive = false;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{0,31}$")]
    private static partial Regex CodePattern();
}
