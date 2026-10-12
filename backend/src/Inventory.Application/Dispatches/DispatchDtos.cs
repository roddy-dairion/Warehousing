using Inventory.Application.Common;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;

namespace Inventory.Application.Dispatches;

public sealed class DispatchOptions
{
    public const string Section = "Dispatch";

    /// <summary>The creator or submitter of a dispatch cannot approve it (Q6). Default on.</summary>
    public bool RequireDifferentApprover { get; set; } = true;

    /// <summary>Allow completing picking with short lines (the shipper then chooses close short or backorder). Default on (Q5).</summary>
    public bool AllowPartialShipment { get; set; } = true;

    /// <summary>Photos required for proof of delivery (Q13).</summary>
    public int MinDeliveryPhotos { get; set; } = 1;

    public int MaxDeliveryPhotos { get; set; } = 10;

    /// <summary>Maximum size of one delivery photo, in MB.</summary>
    public int MaxPhotoMegabytes { get; set; } = 10;
}

public sealed record DispatchListItemDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, Guid CustomerId, string CustomerReference, string CustomerName,
    string? CustomerOrderReference, DateOnly RequestedDate, DispatchStatus Status, int LineCount, decimal QuantityRequested, decimal QuantityShipped,
    string? BackorderOfNumber, string? AssignedToName, DateTimeOffset CreatedAt, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt);

public sealed record DispatchLineDto(
    Guid Id, int LineNo, Guid ProductId, string Sku, string ProductName, bool IsLotTracked, string BaseUnitCode,
    Guid? ProductUnitId, string UnitCode, decimal EnteredQuantity,
    decimal Quantity, decimal QuantityReserved, decimal QuantityPicked, decimal QuantityShipped, decimal Outstanding, string? Notes);

public sealed record AddressDto(string Label, string Line1, string? Line2, string? City, string? Country, string? ContactName, string? Phone);

public sealed record StatusHistoryDto(DispatchStatus? From, DispatchStatus To, DateTimeOffset At, string? UserName, string? Note);

public sealed record DiscrepancyDto(Guid Id, DiscrepancyType Type, Guid? LineId, string? Sku, string? LocationCode, decimal? Quantity, string? Barcode, string? Note, DateTimeOffset At, string? UserName);

public sealed record DispatchDocumentDto(Guid Id, DocumentType Type, DocumentStatus Status, string? FileName, int? TemplateVersion, DateTimeOffset RequestedAt, DateTimeOffset? GeneratedAt, string? LastError);

public sealed record DispatchEmailDto(Guid Id, string To, string Subject, EmailStatus Status, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

public sealed record ProofOfDeliveryDto(string ReceiverName, DateTimeOffset DeliveredAt, string? Notes, Guid SignatureFileId, IReadOnlyList<Guid> PhotoFileIds, DateTimeOffset RecordedAt, string? RecordedByName);

/// <summary>What the current user may do next (UI hints; the API still enforces everything).</summary>
public sealed record DispatchActionsDto(
    bool CanEdit, bool CanSubmit, bool CanReturn, bool CanApprove, bool CanStartPicking, bool CanPick, bool CanCompletePicking,
    bool CanReopenPicking, bool CanShip, bool CanDeliver, bool CanCancel);

public sealed record DispatchDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, Guid CustomerId, string CustomerReference, string CustomerName, string? CustomerEmail,
    Guid? DeliveryAddressId, AddressDto? ShipTo, string? CustomerOrderReference, DateOnly RequestedDate, string? Notes, DispatchStatus Status,
    Guid? BackorderOfId, string? BackorderOfNumber, IReadOnlyList<BackorderRefDto> Backorders,
    Guid? AssignedToId, string? AssignedToName,
    IReadOnlyList<DispatchLineDto> Lines, IReadOnlyList<StatusHistoryDto> History, IReadOnlyList<DiscrepancyDto> Discrepancies,
    IReadOnlyList<DispatchDocumentDto> Documents, IReadOnlyList<DispatchEmailDto> Emails, ProofOfDeliveryDto? ProofOfDelivery,
    string? CreatedByName, DateTimeOffset CreatedAt, string? SubmittedByName, DateTimeOffset? SubmittedAt, string? ApprovedByName, DateTimeOffset? ApprovedAt,
    string? ShippedByName, DateTimeOffset? ShippedAt, string? Carrier, string? TrackingNumber, ShortShipDecision? ShortDecision,
    DateTimeOffset? DeliveredAt, DateTimeOffset? CancelledAt, string? CancelledByName, string? CancelReason,
    DispatchActionsDto Actions, uint Version);

public sealed record BackorderRefDto(Guid Id, string Number, DispatchStatus Status);

/// <param name="ProductUnitId">Pack unit the quantity is entered in; null = base unit.</param>
public sealed record DispatchLineRequest(Guid ProductId, Guid? ProductUnitId, decimal Quantity, string? Notes);

/// <param name="DeliveryAddressId">Null = the customer's default address when submitted.</param>
public sealed record CreateDispatchRequest(Guid WarehouseId, Guid CustomerId, Guid? DeliveryAddressId, DateOnly? RequestedDate, string? CustomerOrderReference,
    string? Notes, IReadOnlyList<DispatchLineRequest>? Lines);

public sealed record UpdateDispatchRequest(Guid CustomerId, Guid? DeliveryAddressId, DateOnly RequestedDate, string? CustomerOrderReference, string? Notes,
    IReadOnlyList<DispatchLineRequest> Lines, uint Version);

public sealed record DispatchReasonRequest(string Reason);

public sealed record DispatchNoteRequest(string? Note);

public sealed record StartPickingRequest(Guid? AssignedToId);

public sealed record AssignRequest(Guid? AssignedToId);

public sealed record CompletePickingRequest(bool AllowShort);

public sealed record ShipRequest(string? Carrier, string? TrackingNumber, ShortShipDecision? ShortDecision);

public sealed class DispatchListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public DispatchStatus? Status { get; set; }
    /// <summary>Open = not delivered or cancelled (shortcut for work lists).</summary>
    public bool? Open { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? AssignedToId { get; set; }
    public bool AssignedToMe { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

// ---- Picking ----------------------------------------------------------------------------------

/// <summary>One allocation to pick: a reservation on a bin (and lot).</summary>
public sealed record PickTaskDto(
    Guid ReservationId, Guid LineId, int LineNo, Guid ProductId, string Sku, string ProductName, string BaseUnitCode,
    Guid LocationId, string LocationCode, string? LocationBarcode, Guid? LotId, string? LotNo, DateOnly? ExpiryDate,
    decimal Quantity, decimal Picked, decimal Remaining);

public sealed record PickListDto(Guid DispatchId, string Number, DispatchStatus Status, string? AssignedToName, IReadOnlyList<PickTaskDto> Tasks, IReadOnlyList<DispatchLineDto> Lines);

/// <param name="ScanId">Unique id generated by the device for this scan; resending the same id does not count twice.</param>
/// <param name="Location">Bin code or bin label barcode.</param>
/// <param name="Barcode">Product or pack barcode. Alternatively send <paramref name="ProductId"/> (manual entry).</param>
/// <param name="Quantity">Quantity in the scanned unit (a pack barcode counts packs). Default 1.</param>
public sealed record PickRequest(string ScanId, string Location, string? Barcode, Guid? ProductId, Guid? ProductUnitId, string? LotNo, decimal? Quantity);

public sealed record PickResultDto(Guid PickId, bool Replayed, Guid LineId, int LineNo, string Sku, string LocationCode, string? LotNo,
    decimal Quantity, decimal LinePicked, decimal LineReserved, decimal TaskRemaining, DispatchStatus Status);

public sealed record UndoPickRequest(string ScanId);

/// <param name="Location">Bin code or label barcode to take the goods from instead.</param>
public sealed record ReallocateRequest(Guid ReservationId, string Location, string? LotNo, decimal Quantity);

public sealed record DiscrepancyRequest(DiscrepancyType Type, Guid? LineId, string? Location, string? Barcode, decimal? Quantity, string? Note);

public sealed record SendDeliveryNoteRequest(string? To);
