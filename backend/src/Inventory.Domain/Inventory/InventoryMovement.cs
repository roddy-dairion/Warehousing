using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public enum MovementType
{
    Receipt,
    ReceiptReversal,
    AdjustmentIn,
    AdjustmentOut,
    MoveOut,
    MoveIn,
    TransferOut,
    TransferIn,
    Dispatch,
    ReturnIn,
}

/// <summary>Where a movement came from (document type).</summary>
public static class SourceTypes
{
    public const string Receipt = "receipt";
    public const string Adjustment = "adjustment";
    public const string BinMove = "bin_move";
    public const string Dispatch = "dispatch";
}

/// <summary>
/// Immutable ledger row: one change of on-hand stock. A database trigger rejects UPDATE and DELETE;
/// a unique key on (source type, source line, movement type) makes each document line post once.
/// </summary>
public sealed class InventoryMovement : Entity
{
    private InventoryMovement() { }

    /// <summary>Database-assigned, strictly increasing ledger sequence (orders movements written in the same instant).</summary>
    public long Sequence { get; private init; }
    public Guid BalanceId { get; private init; }
    public Guid WarehouseId { get; private init; }
    public Guid LocationId { get; private init; }
    public Guid ProductId { get; private init; }
    public Guid? LotId { get; private init; }
    public MovementType Type { get; private init; }
    /// <summary>Signed change of on-hand stock (base unit).</summary>
    public decimal Quantity { get; private init; }
    public decimal OnHandAfter { get; private init; }
    public string SourceType { get; private init; } = null!;
    public Guid SourceId { get; private init; }
    public Guid SourceLineId { get; private init; }
    public string? SourceNumber { get; private init; }
    public string? Reason { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }
    public Guid? CreatedBy { get; private init; }

    public static InventoryMovement Create(
        InventoryBalance balance, MovementType type, decimal quantity, StockSource source, DateTimeOffset at, Guid? userId) => new()
    {
        BalanceId = balance.Id,
        WarehouseId = balance.WarehouseId,
        LocationId = balance.LocationId,
        ProductId = balance.ProductId,
        LotId = balance.LotId,
        Type = type,
        Quantity = quantity,
        OnHandAfter = balance.OnHand,
        SourceType = source.Type,
        SourceId = source.Id,
        SourceLineId = source.LineId,
        SourceNumber = source.Number,
        Reason = source.Reason,
        OccurredAt = at,
        CreatedBy = userId,
    };
}

/// <summary>The document line that caused a stock change.</summary>
public sealed record StockSource(string Type, Guid Id, Guid LineId, string? Number = null, string? Reason = null);
