using Inventory.Application.Common;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Inventory;

/// <param name="ProductUnitId">Pack unit of <paramref name="Quantity"/>; null = base unit.</param>
public sealed record MoveLineRequest(Guid ProductId, string? LotNo, Guid FromLocationId, Guid ToLocationId, decimal Quantity, Guid? ProductUnitId);

public sealed record CreateBinMoveRequest(Guid WarehouseId, string? Notes, IReadOnlyList<MoveLineRequest> Lines);

public sealed record TransferLineDto(int LineNo, Guid ProductId, string Sku, string ProductName, string? LotNo, Guid FromLocationId, string FromLocationCode,
    Guid ToLocationId, string ToLocationCode, decimal Quantity, string BaseUnitCode);

public sealed record TransferDto(Guid Id, string Number, TransferType Type, Guid WarehouseId, string WarehouseCode, string? Notes,
    DateTimeOffset PostedAt, string? PostedByName, IReadOnlyList<TransferLineDto> Lines);

public sealed record TransferListItemDto(Guid Id, string Number, TransferType Type, string WarehouseCode, int LineCount, DateTimeOffset PostedAt, string? Notes);

public sealed class TransferListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
}
