using Inventory.Domain.Common;

namespace Inventory.Domain.Dispatches;

public enum DispatchStatus
{
    Draft,
    Submitted,
    Approved,
    Picking,
    ReadyForDispatch,
    Dispatched,
    Delivered,
    Cancelled,
}

/// <summary>What happens to quantities that were not shipped.</summary>
public enum ShortShipDecision
{
    /// <summary>The rest is released; the dispatch ends with shipped &lt; requested.</summary>
    CloseShort,
    /// <summary>The rest stays reserved on a new child dispatch (status Approved).</summary>
    Backorder,
}

/// <summary>
/// An outbound delivery to a customer from one warehouse. Status changes only through the command methods,
/// which enforce the state machine; every change is recorded in <see cref="History"/>.
/// </summary>
public sealed class Dispatch : Entity, IAuditable
{
    public const int MaxLines = 300;

    private readonly List<DispatchLine> _lines = [];
    private readonly List<DispatchStatusHistory> _history = [];

    private Dispatch() { }

    public string Number { get; private set; } = null!;
    public Guid WarehouseId { get; private set; }
    public Guid CustomerId { get; private set; }
    /// <summary>Chosen delivery address; null = the customer's default address at submission.</summary>
    public Guid? DeliveryAddressId { get; private set; }
    public string? CustomerReference { get; private set; }
    public DateOnly RequestedDate { get; private set; }
    public string? Notes { get; private set; }
    public DispatchStatus Status { get; private set; } = DispatchStatus.Draft;
    public Guid? BackorderOfId { get; private set; }

    /// <summary>Delivery address copied at submission, so later customer edits do not change the document.</summary>
    public AddressSnapshot? ShipTo { get; private set; }

    public Guid? AssignedToId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? PickingStartedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public Guid? ReadyBy { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public Guid? ShippedBy { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }
    public ShortShipDecision? ShortDecision { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public Guid? DeliveredBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyList<DispatchLine> Lines => _lines;
    public IReadOnlyList<DispatchStatusHistory> History => _history;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static Dispatch Create(string number, Guid warehouseId, Guid customerId, Guid? deliveryAddressId, DateOnly requestedDate,
        string? customerReference, string? notes, Guid? userId, DateTimeOffset at)
    {
        var d = new Dispatch { Number = number, WarehouseId = warehouseId };
        d.SetHeader(customerId, deliveryAddressId, requestedDate, customerReference, notes);
        d._history.Add(DispatchStatusHistory.Create(d.Id, null, DispatchStatus.Draft, userId, at, null));
        return d;
    }

    /// <summary>A backorder for the quantities not shipped on <paramref name="parent"/>; it starts Approved (stock stays reserved).</summary>
    public static Dispatch CreateBackorder(string number, Dispatch parent, IEnumerable<DispatchLine> lines, Guid? userId, DateTimeOffset at)
    {
        var d = new Dispatch
        {
            Number = number,
            WarehouseId = parent.WarehouseId,
            CustomerId = parent.CustomerId,
            DeliveryAddressId = parent.DeliveryAddressId,
            CustomerReference = parent.CustomerReference,
            RequestedDate = parent.RequestedDate,
            Notes = parent.Notes,
            BackorderOfId = parent.Id,
            ShipTo = parent.ShipTo is { } a ? a with { } : null, // owned value: each dispatch needs its own instance
            Status = DispatchStatus.Approved,
            SubmittedAt = at,
            SubmittedBy = userId,
            ApprovedAt = at,
            ApprovedBy = userId,
        };
        d.AttachLines(lines);
        foreach (var line in d._lines) line.SetReserved(line.Quantity);
        d._history.Add(DispatchStatusHistory.Create(d.Id, null, DispatchStatus.Approved, userId, at, $"Backorder of {parent.Number}"));
        return d;
    }

    public void SetHeader(Guid customerId, Guid? deliveryAddressId, DateOnly requestedDate, string? customerReference, string? notes)
    {
        EnsureStatus(DispatchStatus.Draft);
        CustomerId = customerId;
        DeliveryAddressId = deliveryAddressId;
        RequestedDate = requestedDate;
        CustomerReference = Text.Optional(customerReference, 60, "dispatch.reference_invalid", "Customer reference");
        Notes = Text.Optional(notes, 2000, "dispatch.notes_invalid", "Notes");
    }

    public void ReplaceLines(IEnumerable<DispatchLine> lines)
    {
        EnsureStatus(DispatchStatus.Draft);
        _lines.Clear();
        AttachLines(lines);
    }

    private void AttachLines(IEnumerable<DispatchLine> lines)
    {
        var list = lines.ToList();
        if (list.Count > MaxLines) throw new DomainRuleException("dispatch.too_many_lines", $"A dispatch can have at most {MaxLines} lines.");
        if (list.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
        {
            throw new DomainRuleException("dispatch.duplicate_product", "Each product can appear only once. Combine the quantities on one line.");
        }
        var n = 1;
        foreach (var line in list)
        {
            line.Attach(Id, n++);
            _lines.Add(line);
        }
    }

    public void Submit(AddressSnapshot shipTo, Guid? userId, DateTimeOffset at)
    {
        EnsureStatus(DispatchStatus.Draft);
        if (_lines.Count == 0) throw new DomainRuleException("dispatch.empty", "Add at least one product before submitting.");
        ShipTo = shipTo;
        SubmittedAt = at;
        SubmittedBy = userId;
        Move(DispatchStatus.Submitted, userId, at, null);
    }

    /// <summary>Sends a submitted dispatch back to draft for changes.</summary>
    public void Return(Guid? userId, DateTimeOffset at, string reason)
    {
        EnsureStatus(DispatchStatus.Submitted);
        var r = Text.Required(reason, 500, "dispatch.reason_required", "Reason");
        SubmittedAt = null;
        SubmittedBy = null;
        Move(DispatchStatus.Draft, userId, at, r);
    }

    /// <summary>Marks the dispatch approved. The caller reserves the stock in the same transaction.</summary>
    public void Approve(Guid? userId, DateTimeOffset at, bool requireDifferentApprover, string? note)
    {
        EnsureStatus(DispatchStatus.Submitted);
        if (requireDifferentApprover && userId is not null && (userId == CreatedBy || userId == SubmittedBy))
        {
            throw new DomainRuleException("dispatch.self_approval", "You cannot approve a dispatch you created or submitted. Ask another manager.", isConflict: true);
        }
        ApprovedAt = at;
        ApprovedBy = userId;
        foreach (var line in _lines) line.SetReserved(line.Quantity);
        Move(DispatchStatus.Approved, userId, at, Text.Optional(note, 500, "dispatch.note_invalid", "Note"));
    }

    public void StartPicking(Guid? assignedToId, Guid? userId, DateTimeOffset at)
    {
        EnsureStatus(DispatchStatus.Approved);
        AssignedToId = assignedToId;
        PickingStartedAt = at;
        Move(DispatchStatus.Picking, userId, at, null);
    }

    public void Assign(Guid? assignedToId)
    {
        if (Status is not (DispatchStatus.Approved or DispatchStatus.Picking))
        {
            throw new DomainRuleException("dispatch.cannot_assign", "Picking can only be assigned before it is complete.", isConflict: true);
        }
        AssignedToId = assignedToId;
    }

    /// <summary>
    /// Picking is complete. Lines picked short are allowed only when <paramref name="allowShort"/> is true
    /// (the shipper later chooses close short or backorder).
    /// </summary>
    public void CompletePicking(bool allowShort, Guid? userId, DateTimeOffset at)
    {
        EnsureStatus(DispatchStatus.Picking);
        if (_lines.All(l => l.QuantityPicked == 0))
        {
            throw new DomainRuleException("dispatch.nothing_picked", "Nothing has been picked. Pick the goods first, or cancel the dispatch.");
        }
        var shortLines = _lines.Where(l => l.QuantityPicked < l.QuantityReserved).ToList();
        if (shortLines.Count > 0 && !allowShort)
        {
            throw new DomainRuleException("dispatch.picking_incomplete",
                $"{shortLines.Count} line(s) are not fully picked. Pick the rest or confirm the dispatch as partial.", isConflict: true);
        }
        ReadyAt = at;
        ReadyBy = userId;
        Move(DispatchStatus.ReadyForDispatch, userId, at, shortLines.Count > 0 ? $"Partial: {shortLines.Count} line(s) short" : null);
    }

    public void ReopenPicking(Guid? userId, DateTimeOffset at, string? reason)
    {
        EnsureStatus(DispatchStatus.ReadyForDispatch);
        ReadyAt = null;
        ReadyBy = null;
        Move(DispatchStatus.Picking, userId, at, Text.Optional(reason, 500, "dispatch.reason_invalid", "Reason"));
    }

    /// <summary>
    /// Ships what was picked. The caller consumes the reservations, and releases or moves the remainder, in the same
    /// transaction. Shipping is allowed exactly once.
    /// </summary>
    public void Ship(string? carrier, string? trackingNumber, ShortShipDecision? decision, Guid? userId, DateTimeOffset at)
    {
        EnsureStatus(DispatchStatus.ReadyForDispatch);
        var isShort = _lines.Any(l => l.QuantityPicked < l.Quantity);
        if (isShort && decision is null)
        {
            throw new DomainRuleException("dispatch.short_decision_required",
                "Some lines are short. Choose whether to close the dispatch short or create a backorder for the rest.");
        }
        foreach (var line in _lines) line.MarkShipped(line.QuantityPicked);
        Carrier = Text.Optional(carrier, 100, "dispatch.carrier_invalid", "Carrier");
        TrackingNumber = Text.Optional(trackingNumber, 100, "dispatch.tracking_invalid", "Tracking number");
        ShortDecision = isShort ? decision : null;
        ShippedAt = at;
        ShippedBy = userId;
        Move(DispatchStatus.Dispatched, userId, at, isShort ? $"Shipped short ({decision})" : null);
    }

    public void MarkDelivered(Guid? userId, DateTimeOffset at)
    {
        EnsureStatus(DispatchStatus.Dispatched);
        DeliveredAt = at;
        DeliveredBy = userId;
        Move(DispatchStatus.Delivered, userId, at, null);
    }

    public void Cancel(Guid? userId, DateTimeOffset at, string reason)
    {
        if (Status is DispatchStatus.Dispatched or DispatchStatus.Delivered)
        {
            throw new DomainRuleException("dispatch.cannot_cancel_shipped",
                "A shipped dispatch cannot be cancelled. Goods that come back must be booked in as a return.", isConflict: true);
        }
        if (Status == DispatchStatus.Cancelled) throw new DomainRuleException("dispatch.already_cancelled", "This dispatch is already cancelled.", isConflict: true);
        CancelReason = Text.Required(reason, 500, "dispatch.reason_required", "Reason");
        CancelledAt = at;
        CancelledBy = userId;
        Move(DispatchStatus.Cancelled, userId, at, CancelReason);
    }

    public bool HoldsReservations => Status is DispatchStatus.Approved or DispatchStatus.Picking or DispatchStatus.ReadyForDispatch;

    public void EnsureStatus(params DispatchStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainRuleException("dispatch.invalid_status",
                $"Dispatch {Number} is {Describe(Status)}; this action needs it to be {string.Join(" or ", allowed.Select(Describe))}.", isConflict: true);
        }
    }

    public static string Describe(DispatchStatus s) => s switch
    {
        DispatchStatus.ReadyForDispatch => "ready for dispatch",
        _ => s.ToString().ToLowerInvariant(),
    };

    private void Move(DispatchStatus to, Guid? userId, DateTimeOffset at, string? note)
    {
        _history.Add(DispatchStatusHistory.Create(Id, Status, to, userId, at, note));
        Status = to;
    }
}

/// <summary>A product line. Quantities are in the product's base unit.</summary>
public sealed class DispatchLine : Entity
{
    private DispatchLine() { }

    public Guid DispatchId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>Pack unit the quantity was entered in; null = base unit.</summary>
    public Guid? ProductUnitId { get; private set; }
    public decimal EnteredQuantity { get; private set; }
    /// <summary>Requested quantity (base unit).</summary>
    public decimal Quantity { get; private set; }
    public decimal QuantityReserved { get; private set; }
    public decimal QuantityPicked { get; private set; }
    public decimal QuantityShipped { get; private set; }
    public string? Notes { get; private set; }

    public decimal Outstanding => Quantity - QuantityShipped;

    public static DispatchLine Create(Guid productId, Guid? productUnitId, decimal enteredQuantity, decimal baseQuantity, string? notes)
    {
        if (baseQuantity <= 0) throw new DomainRuleException("dispatch.quantity", "Quantity must be greater than zero.");
        return new DispatchLine
        {
            ProductId = productId,
            ProductUnitId = productUnitId,
            EnteredQuantity = enteredQuantity,
            Quantity = baseQuantity,
            Notes = Text.Optional(notes, 500, "dispatch.line_notes_invalid", "Line notes"),
        };
    }

    internal void Attach(Guid dispatchId, int lineNo)
    {
        DispatchId = dispatchId;
        LineNo = lineNo;
    }

    internal void SetReserved(decimal quantity) => QuantityReserved = quantity;

    /// <summary>Records picked quantity (negative to undo). Cannot exceed the reserved quantity.</summary>
    public void AddPicked(decimal quantity)
    {
        var picked = QuantityPicked + quantity;
        if (picked < 0) throw new DomainRuleException("pick.negative", "Cannot undo more than was picked.", isConflict: true);
        if (picked > QuantityReserved)
        {
            throw new DomainRuleException("pick.over_pick", $"Only {QuantityReserved - QuantityPicked:0.###} more can be picked on line {LineNo}.");
        }
        QuantityPicked = picked;
    }

    internal void MarkShipped(decimal quantity) => QuantityShipped = quantity;
}

public sealed class DispatchStatusHistory : Entity
{
    private DispatchStatusHistory() { }

    public Guid DispatchId { get; private set; }
    public DispatchStatus? FromStatus { get; private set; }
    public DispatchStatus ToStatus { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Note { get; private set; }

    internal static DispatchStatusHistory Create(Guid dispatchId, DispatchStatus? from, DispatchStatus to, Guid? userId, DateTimeOffset at, string? note) =>
        new() { DispatchId = dispatchId, FromStatus = from, ToStatus = to, UserId = userId, At = at, Note = note };
}

/// <summary>Delivery address copied onto the dispatch (stored as columns of the dispatch row).</summary>
public sealed record AddressSnapshot(string Label, string Line1, string? Line2, string? City, string? Country, string? ContactName, string? Phone);
