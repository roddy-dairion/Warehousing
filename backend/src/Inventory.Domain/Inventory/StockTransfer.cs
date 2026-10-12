using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public enum TransferType
{
    /// <summary>Between bins of one warehouse (put-away, re-slotting). Posted immediately.</summary>
    BinMove,
    /// <summary>Between warehouses. Designed, not enabled in v1.</summary>
    InterWarehouse,
}

/// <summary>
/// Moves stock between bins (v1) or warehouses (future). A bin move is created and posted in one
/// atomic step: source decreases and destination increases in the same transaction.
/// </summary>
public sealed class StockTransfer : Entity, IAuditable
{
    public const int MaxLines = 200;

    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer() { }

    public string Number { get; private set; } = null!;
    public TransferType Type { get; private set; }
    public Guid FromWarehouseId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset PostedAt { get; private set; }
    public Guid? PostedBy { get; private set; }
    public IReadOnlyList<StockTransferLine> Lines => _lines;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static StockTransfer CreateBinMove(string number, Guid warehouseId, string? notes, IEnumerable<StockTransferLine> lines, Guid? userId, DateTimeOffset at)
    {
        var t = new StockTransfer
        {
            Number = number,
            Type = TransferType.BinMove,
            FromWarehouseId = warehouseId,
            ToWarehouseId = warehouseId,
            Notes = Text.Optional(notes, 2000, "transfer.notes_invalid", "Notes"),
            PostedAt = at,
            PostedBy = userId,
        };
        var n = 1;
        foreach (var line in lines)
        {
            line.Attach(t.Id, n++);
            t._lines.Add(line);
        }
        if (t._lines.Count == 0) throw new DomainRuleException("transfer.empty", "Add at least one line.");
        if (t._lines.Count > MaxLines) throw new DomainRuleException("transfer.too_many_lines", $"At most {MaxLines} lines per move.");
        return t;
    }
}

public sealed class StockTransferLine : Entity
{
    private StockTransferLine() { }

    public Guid TransferId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? LotId { get; private set; }
    public Guid FromLocationId { get; private set; }
    public Guid ToLocationId { get; private set; }
    public decimal Quantity { get; private set; }

    public static StockTransferLine Create(Guid productId, Guid? lotId, Guid fromLocationId, Guid toLocationId, decimal quantity)
    {
        if (fromLocationId == toLocationId) throw new DomainRuleException("transfer.same_bin", "The source and destination bins must be different.");
        if (quantity <= 0) throw new DomainRuleException("transfer.quantity", "Quantity must be greater than zero.");
        return new StockTransferLine { ProductId = productId, LotId = lotId, FromLocationId = fromLocationId, ToLocationId = toLocationId, Quantity = quantity };
    }

    internal void Attach(Guid transferId, int lineNo)
    {
        TransferId = transferId;
        LineNo = lineNo;
    }
}
