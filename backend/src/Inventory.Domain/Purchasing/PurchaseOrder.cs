using Inventory.Domain.Common;

namespace Inventory.Domain.Purchasing;

public enum PurchaseOrderStatus
{
    Draft,
    /// <summary>Sent to the supplier; counts as "on order".</summary>
    Ordered,
    PartiallyReceived,
    Received,
    /// <summary>Closed before everything arrived: the rest is no longer expected.</summary>
    Closed,
    Cancelled,
}

/// <summary>
/// Goods ordered from a supplier for one warehouse. Draft → Ordered → (Partially) Received, or Closed / Cancelled.
/// Ordering never changes stock: stock only goes up when a goods receipt linked to the order is posted, which also
/// records the received quantity on the order line (and a receipt reversal takes it off again).
/// </summary>
public sealed class PurchaseOrder : Entity, IAuditable
{
    public const int MaxLines = 500;

    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder() { }

    public string Number { get; private set; } = null!;
    public Guid WarehouseId { get; private set; }
    public Guid SupplierId { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public DateOnly? ExpectedDate { get; private set; }
    public string? SupplierReference { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public DateTimeOffset? OrderedAt { get; private set; }
    public Guid? OrderedBy { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    /// <summary>Reason given when the order was closed short or cancelled.</summary>
    public string? CloseReason { get; private set; }
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Open orders are expected to deliver (they count as "on order" and can be received against).</summary>
    public bool IsOpen => Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived;

    /// <summary>A goods receipt can be posted against the order (an over-delivery after "Received" is still recorded).</summary>
    public bool CanReceive => Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received;

    public static PurchaseOrder Create(string number, Guid warehouseId, Guid supplierId, DateOnly orderDate) =>
        new() { Number = number, WarehouseId = warehouseId, SupplierId = supplierId, OrderDate = orderDate };

    /// <summary>Supplier and lines can only change while the order is a draft.</summary>
    public void ChangeSupplier(Guid supplierId)
    {
        EnsureDraft();
        SupplierId = supplierId;
    }

    /// <summary>Dates, supplier reference and notes can change until the order is finished (e.g. the supplier confirms a date).</summary>
    public void SetDetails(DateOnly orderDate, DateOnly? expectedDate, string? supplierReference, string? notes)
    {
        if (Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled)
        {
            throw NotChangeable();
        }
        if (Status != PurchaseOrderStatus.Draft && orderDate != OrderDate)
        {
            throw new DomainRuleException("purchase_order.order_date_fixed", "The order date cannot change once the order is placed.");
        }
        if (expectedDate is { } e && e < orderDate)
        {
            throw new DomainRuleException("purchase_order.expected_before_order", "The expected date cannot be before the order date.");
        }
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        SupplierReference = Text.Optional(supplierReference, 60, "purchase_order.reference_invalid", "Supplier reference");
        Notes = Text.Optional(notes, 2000, "purchase_order.notes_invalid", "Notes");
    }

    /// <summary>Replaces all lines (the draft editor sends the full list). A product appears at most once.</summary>
    public void ReplaceLines(IEnumerable<PurchaseOrderLine> lines)
    {
        EnsureDraft();
        var list = lines.ToList();
        if (list.Count > MaxLines) throw new DomainRuleException("purchase_order.too_many_lines", $"An order can have at most {MaxLines} lines.");
        if (list.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
        {
            throw new DomainRuleException("purchase_order.duplicate_product", "Each product can appear only once on an order: change the quantity instead.");
        }
        _lines.Clear();
        var n = 1;
        foreach (var line in list)
        {
            line.Attach(Id, n++);
            _lines.Add(line);
        }
    }

    public void Place(Guid? userId, DateTimeOffset at)
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new DomainRuleException("purchase_order.empty", "Add at least one line before placing the order.");
        Status = PurchaseOrderStatus.Ordered;
        OrderedAt = at;
        OrderedBy = userId;
    }

    /// <summary>A draft, or a placed order nothing has been received against yet.</summary>
    public void Cancel(Guid? userId, DateTimeOffset at, string? reason)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered))
        {
            throw new DomainRuleException("purchase_order.cannot_cancel",
                Status == PurchaseOrderStatus.PartiallyReceived
                    ? "Goods were already received against this order: close it instead."
                    : $"This order is {Label(Status)} and can no longer be cancelled.", isConflict: true);
        }
        if (_lines.Any(l => l.ReceivedQuantity > 0))
        {
            throw new DomainRuleException("purchase_order.cannot_cancel", "Goods were already received against this order: close it instead.", isConflict: true);
        }
        CloseReason = Text.Optional(reason, 500, "purchase_order.reason_invalid", "Reason");
        Status = PurchaseOrderStatus.Cancelled;
        ClosedAt = at;
        ClosedBy = userId;
    }

    /// <summary>Stop expecting the rest of a placed order (short delivery). What was received stays received.</summary>
    public void Close(Guid? userId, DateTimeOffset at, string reason)
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("purchase_order.cannot_close", $"Only an open order can be closed; this one is {Label(Status)}.", isConflict: true);
        }
        CloseReason = Text.Required(reason, 500, "purchase_order.reason_required", "Reason");
        Status = PurchaseOrderStatus.Closed;
        ClosedAt = at;
        ClosedBy = userId;
    }

    /// <summary>Adds (or, for a receipt reversal, removes) received base quantity on a line and updates the status.</summary>
    public void RecordReceived(Guid lineId, decimal baseQuantity)
    {
        if (baseQuantity > 0 && !CanReceive)
        {
            throw new DomainRuleException("purchase_order.not_receivable",
                $"Purchase order {Number} is {Label(Status)}: goods can no longer be received against it.", isConflict: true);
        }
        var line = _lines.SingleOrDefault(l => l.Id == lineId)
            ?? throw new DomainRuleException("purchase_order.line_not_found", "Purchase order line not found.");
        line.AddReceived(baseQuantity);
        if (Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received)
        {
            Status = _lines.All(l => l.ReceivedQuantity >= l.Quantity) ? PurchaseOrderStatus.Received
                : _lines.Any(l => l.ReceivedQuantity > 0) ? PurchaseOrderStatus.PartiallyReceived
                : PurchaseOrderStatus.Ordered;
        }
    }

    public static string Label(PurchaseOrderStatus s) => s switch
    {
        PurchaseOrderStatus.PartiallyReceived => "partially received",
        _ => s.ToString().ToLowerInvariant(),
    };

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatus.Draft) throw NotChangeable();
    }

    private DomainRuleException NotChangeable() =>
        new("purchase_order.not_draft", $"This order is {Label(Status)}: its supplier and lines can no longer be changed.", isConflict: true);
}

public sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine() { }

    public Guid PurchaseOrderId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>The pack unit the quantity was entered in; null = base unit.</summary>
    public Guid? ProductUnitId { get; private set; }
    public decimal EnteredQuantity { get; private set; }
    /// <summary>Ordered quantity in base units.</summary>
    public decimal Quantity { get; private set; }
    /// <summary>Received so far, in base units (from posted goods receipts; may exceed the ordered quantity).</summary>
    public decimal ReceivedQuantity { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Still expected, in base units.</summary>
    public decimal Outstanding => Quantity > ReceivedQuantity ? Quantity - ReceivedQuantity : 0;

    public static PurchaseOrderLine Create(Guid productId, Guid? productUnitId, decimal enteredQuantity, decimal baseQuantity, string? notes)
    {
        if (enteredQuantity <= 0 || baseQuantity <= 0) throw new DomainRuleException("quantity.positive", "Quantity must be greater than zero.");
        return new()
        {
            ProductId = productId,
            ProductUnitId = productUnitId,
            EnteredQuantity = enteredQuantity,
            Quantity = baseQuantity,
            Notes = Text.Optional(notes, 500, "purchase_order.line_notes_invalid", "Line notes"),
        };
    }

    internal void Attach(Guid orderId, int lineNo)
    {
        PurchaseOrderId = orderId;
        LineNo = lineNo;
    }

    internal void AddReceived(decimal baseQuantity)
    {
        var next = ReceivedQuantity + baseQuantity;
        if (next < 0) throw new DomainRuleException("purchase_order.received_negative", "Received quantity cannot go below zero.");
        ReceivedQuantity = next;
    }
}
