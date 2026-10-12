using Inventory.Application.Common;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Inventory;

public sealed record ReceiptListItemDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, DateOnly ReceiptDate, string? SupplierName, string? SupplierReference,
    ReceiptStatus Status, int LineCount, DateTimeOffset CreatedAt, DateTimeOffset? PostedAt);

public sealed record ReceiptLineDto(
    Guid Id, int LineNo, Guid ProductId, string Sku, string ProductName, Guid? ProductUnitId, string UnitCode, decimal EnteredQuantity,
    decimal Quantity, string BaseUnitCode, decimal? ExpectedQuantity, Guid LocationId, string LocationCode, string? LotNo, DateOnly? ExpiryDate, string? Notes);

public sealed record ReceiptDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, DateOnly ReceiptDate, string? SupplierName, string? SupplierReference, string? Notes,
    ReceiptStatus Status, IReadOnlyList<ReceiptLineDto> Lines,
    DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset? PostedAt, string? PostedByName,
    DateTimeOffset? ReversedAt, string? ReversedByName, string? ReversalReason, uint Version);

/// <param name="LocationId">Bin to receive into; null = the warehouse's RECEIVING area.</param>
/// <param name="ProductUnitId">Pack unit the quantity is entered in; null = base unit.</param>
public sealed record ReceiptLineRequest(Guid ProductId, Guid? ProductUnitId, decimal Quantity, decimal? ExpectedQuantity, Guid? LocationId, string? LotNo, DateOnly? ExpiryDate, string? Notes);

public sealed record CreateReceiptRequest(Guid WarehouseId, DateOnly? ReceiptDate, string? SupplierName, string? SupplierReference, string? Notes, IReadOnlyList<ReceiptLineRequest>? Lines);

public sealed record UpdateReceiptRequest(DateOnly ReceiptDate, string? SupplierName, string? SupplierReference, string? Notes, IReadOnlyList<ReceiptLineRequest> Lines, uint Version);

public sealed record ReasonRequest(string Reason);

public sealed class ReceiptListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public ReceiptStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
