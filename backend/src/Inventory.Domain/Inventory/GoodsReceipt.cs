using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public enum ReceiptStatus
{
    Draft,
    Posted,
    Reversed,
    Cancelled,
}

/// <summary>
/// Goods received into a warehouse. Draft receipts do not affect stock; posting adds stock exactly
/// once; a posted receipt can only be corrected by reversing it (fully) and receiving again.
/// </summary>
public sealed class GoodsReceipt : Entity, IAuditable
{
    public const int MaxLines = 500;

    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt() { }

    public string Number { get; private set; } = null!;
    public Guid WarehouseId { get; private set; }
    public DateOnly ReceiptDate { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierReference { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>The purchase order this delivery is for (set when the receipt is created; null = no order).</summary>
    public Guid? PurchaseOrderId { get; private set; }
    public ReceiptStatus Status { get; private set; } = ReceiptStatus.Draft;
    public DateTimeOffset? PostedAt { get; private set; }
    public Guid? PostedBy { get; private set; }
    public DateTimeOffset? ReversedAt { get; private set; }
    public Guid? ReversedBy { get; private set; }
    public string? ReversalReason { get; private set; }
    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static GoodsReceipt Create(string number, Guid warehouseId, DateOnly receiptDate, Guid? purchaseOrderId = null)
    {
        var r = new GoodsReceipt { Number = number, WarehouseId = warehouseId, PurchaseOrderId = purchaseOrderId };
        r.SetHeader(receiptDate, null, null, null);
        return r;
    }

    public void SetHeader(DateOnly receiptDate, string? supplierName, string? supplierReference, string? notes)
    {
        EnsureDraft();
        ReceiptDate = receiptDate;
        SupplierName = Text.Optional(supplierName, 200, "receipt.supplier_invalid", "Supplier");
        SupplierReference = Text.Optional(supplierReference, 60, "receipt.reference_invalid", "Supplier reference");
        Notes = Text.Optional(notes, 2000, "receipt.notes_invalid", "Notes");
    }

    /// <summary>Replaces all lines (the draft editor sends the full list).</summary>
    public void ReplaceLines(IEnumerable<GoodsReceiptLine> lines)
    {
        EnsureDraft();
        var list = lines.ToList();
        if (list.Count > MaxLines) throw new DomainRuleException("receipt.too_many_lines", $"A receipt can have at most {MaxLines} lines.");
        _lines.Clear();
        var n = 1;
        foreach (var line in list)
        {
            line.Attach(Id, n++);
            _lines.Add(line);
        }
    }

    public void MarkPosted(Guid? userId, DateTimeOffset at)
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new DomainRuleException("receipt.empty", "Add at least one line before posting.");
        Status = ReceiptStatus.Posted;
        PostedAt = at;
        PostedBy = userId;
    }

    public void MarkReversed(Guid? userId, DateTimeOffset at, string reason)
    {
        if (Status != ReceiptStatus.Posted)
        {
            throw new DomainRuleException("receipt.not_posted", "Only a posted receipt can be reversed.", isConflict: true);
        }
        ReversalReason = Text.Required(reason, 500, "receipt.reason_required", "Reason");
        Status = ReceiptStatus.Reversed;
        ReversedAt = at;
        ReversedBy = userId;
    }

    public void Cancel()
    {
        EnsureDraft();
        Status = ReceiptStatus.Cancelled;
    }

    private void EnsureDraft()
    {
        if (Status != ReceiptStatus.Draft)
        {
            throw new DomainRuleException("receipt.not_draft", $"This receipt is {Status.ToString().ToLowerInvariant()} and can no longer be changed.", isConflict: true);
        }
    }
}

public sealed class GoodsReceiptLine : Entity
{
    private GoodsReceiptLine() { }

    public Guid ReceiptId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>The pack unit the quantity was entered in; null = base unit.</summary>
    public Guid? ProductUnitId { get; private set; }
    public decimal EnteredQuantity { get; private set; }
    /// <summary>Received quantity in base units.</summary>
    public decimal Quantity { get; private set; }
    public decimal? ExpectedQuantity { get; private set; }
    public Guid LocationId { get; private set; }
    public string? LotNo { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>Set when posted.</summary>
    public Guid? LotId { get; private set; }
    /// <summary>The purchase order line for the same product (receipts linked to an order); received quantities are recorded on it when posted.</summary>
    public Guid? PurchaseOrderLineId { get; private set; }
    public string? Notes { get; private set; }

    public static GoodsReceiptLine Create(Guid productId, Guid? productUnitId, decimal enteredQuantity, decimal baseQuantity,
        decimal? expectedQuantity, Guid locationId, string? lotNo, DateOnly? expiryDate, string? notes) => new()
    {
        ProductId = productId,
        ProductUnitId = productUnitId,
        EnteredQuantity = enteredQuantity,
        Quantity = baseQuantity,
        ExpectedQuantity = expectedQuantity,
        LocationId = locationId,
        LotNo = lotNo is null ? null : ProductLot.NormalizeLotNo(lotNo),
        ExpiryDate = expiryDate,
        Notes = Text.Optional(notes, 500, "receipt.line_notes_invalid", "Line notes"),
    };

    internal void Attach(Guid receiptId, int lineNo)
    {
        ReceiptId = receiptId;
        LineNo = lineNo;
    }

    public void SetLot(Guid lotId) => LotId = lotId;

    public void LinkOrderLine(Guid? purchaseOrderLineId) => PurchaseOrderLineId = purchaseOrderLineId;
}
