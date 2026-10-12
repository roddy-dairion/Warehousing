using Inventory.Application.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;

namespace Inventory.Application.Inventory;

/// <summary>One balance row: product × bin × lot in a warehouse.</summary>
/// <remarks>Init-only properties (not a positional record) so EF Core can filter and sort on the projection.</remarks>
public sealed record BalanceDto
{
    public Guid Id { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string BaseUnitCode { get; init; } = "";
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = "";
    public LocationType LocationType { get; init; }
    public bool IsPickable { get; init; }
    public Guid? LotId { get; init; }
    public string? LotNo { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public LotStatus? LotStatus { get; init; }
    public decimal OnHand { get; init; }
    public decimal Reserved { get; init; }
    public decimal Available { get; init; }
    public DateTimeOffset? LastMovementAt { get; init; }
}

public sealed class BalanceListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? LotId { get; set; }
    /// <summary>Hide rows with nothing on hand (default true).</summary>
    public bool OnlyWithStock { get; set; } = true;
}

/// <summary>Stock of one product in one warehouse, totalled over bins and lots.</summary>
public sealed record StockSummaryDto
{
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? CategoryName { get; init; }
    public string BaseUnitCode { get; init; } = "";
    public bool IsActive { get; init; }
    public decimal OnHand { get; init; }
    public decimal Reserved { get; init; }
    public decimal Available { get; init; }
    /// <summary>Available stock in pickable bins and usable lots (what a dispatch could reserve today).</summary>
    public decimal Sellable { get; init; }
    public decimal? MinStockQty { get; init; }
    /// <summary>Still expected on open purchase orders for this warehouse.</summary>
    public decimal OnOrder { get; init; }
    /// <summary>Available is below the product's minimum stock.</summary>
    public bool IsLow { get; init; }
}

public sealed class StockSummaryQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool LowStockOnly { get; set; }
    /// <summary>Only products with stock on hand (default false: products with a minimum stock or on order also show at zero).</summary>
    public bool OnlyWithStock { get; set; }
}

public sealed record ProductStockWarehouseDto(Guid WarehouseId, string WarehouseCode, string WarehouseName, decimal OnHand, decimal Reserved, decimal Available, IReadOnlyList<BalanceDto> Balances);

/// <summary>Everything about one product's stock, across the warehouses the user may see (mobile scan lookup).</summary>
public sealed record ProductStockDto(
    Guid ProductId, string Sku, string ProductName, string BaseUnitCode, bool IsActive, bool IsLotTracked, bool HasExpiry, decimal? MinStockQty,
    decimal OnHand, decimal Reserved, decimal Available, IReadOnlyList<ProductStockWarehouseDto> Warehouses);

public sealed record MovementDto(
    Guid Id, DateTimeOffset OccurredAt, MovementType Type, Guid ProductId, string Sku, string ProductName, string BaseUnitCode,
    Guid LocationId, string LocationCode, Guid? LotId, string? LotNo, decimal Quantity, decimal OnHandAfter,
    string SourceType, Guid SourceId, string? SourceNumber, string? Reason, string? CreatedByName);

public sealed class MovementListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? LotId { get; set; }
    public MovementType? Type { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

/// <summary>A lot with stock that expires soon (or has expired).</summary>
public sealed record ExpiringLotDto(
    Guid LotId, string LotNo, DateOnly ExpiryDate, int DaysLeft, LotStatus Status, Guid ProductId, string Sku, string ProductName, string BaseUnitCode,
    decimal OnHand, decimal Reserved, IReadOnlyList<string> Locations);

public sealed class ExpiringQuery
{
    public Guid WarehouseId { get; set; }
    /// <summary>Window in days; defaults to the configured alert window.</summary>
    public int? Days { get; set; }
}

public sealed record LotDto(Guid Id, Guid ProductId, string LotNo, DateOnly? ExpiryDate, LotStatus Status, decimal OnHand, decimal Reserved, DateTimeOffset CreatedAt, uint Version);

public sealed record SetLotStatusRequest(LotStatus Status, string? Reason, uint Version);

/// <summary>A balance whose stored totals disagree with its ledger or reservations.</summary>
public sealed record ReconciliationIssue(
    Guid BalanceId, Guid WarehouseId, string Sku, string LocationCode, string? LotNo,
    decimal OnHand, decimal LedgerOnHand, decimal Reserved, decimal OpenReservations);

public sealed record ReconciliationResult(DateTimeOffset CheckedAt, int BalancesChecked, IReadOnlyList<ReconciliationIssue> Issues);
