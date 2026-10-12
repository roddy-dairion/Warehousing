using Inventory.Application.Common;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Inventory;

public sealed record AdjustmentListItemDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, AdjustmentReason Reason, AdjustmentStatus Status, int LineCount,
    DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt);

public sealed record AdjustmentLineDto(
    Guid Id, int LineNo, Guid ProductId, string Sku, string ProductName, string BaseUnitCode, Guid LocationId, string LocationCode,
    string? LotNo, DateOnly? ExpiryDate, decimal QuantityDelta, decimal? CurrentOnHand, string? Notes);

public sealed record AdjustmentDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, AdjustmentReason Reason, string? Notes, AdjustmentStatus Status,
    IReadOnlyList<AdjustmentLineDto> Lines,
    DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset? SubmittedAt, string? SubmittedByName,
    DateTimeOffset? DecidedAt, string? DecidedByName, string? DecisionNote,
    /// <summary>True when the caller may approve or reject this adjustment now.</summary>
    bool CanDecide,
    uint Version);

/// <param name="QuantityDelta">Signed change in base units: positive adds stock, negative removes it.</param>
public sealed record AdjustmentLineRequest(Guid ProductId, Guid LocationId, string? LotNo, DateOnly? ExpiryDate, decimal QuantityDelta, string? Notes);

public sealed record CreateAdjustmentRequest(Guid WarehouseId, AdjustmentReason Reason, string? Notes, IReadOnlyList<AdjustmentLineRequest>? Lines);

public sealed record UpdateAdjustmentRequest(AdjustmentReason Reason, string? Notes, IReadOnlyList<AdjustmentLineRequest> Lines, uint Version);

public sealed record ApproveRequest(string? Note);

public sealed class AdjustmentListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public AdjustmentStatus? Status { get; set; }
}
