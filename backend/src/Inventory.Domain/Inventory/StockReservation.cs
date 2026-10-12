using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public enum ReservationStatus
{
    Open,
    Consumed,
    Released,
}

/// <summary>
/// Stock set aside on a specific balance (bin + lot) for a document line (dispatch line in Phase 5).
/// Outstanding = Reserved − Consumed − Released.
/// </summary>
public sealed class StockReservation : Entity, IAuditable
{
    private StockReservation() { }

    public Guid BalanceId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public Guid SourceId { get; private set; }
    public Guid SourceLineId { get; private set; }
    public decimal QuantityReserved { get; private set; }
    public decimal QuantityConsumed { get; private set; }
    public decimal QuantityReleased { get; private set; }
    public ReservationStatus Status { get; private set; } = ReservationStatus.Open;

    public decimal Outstanding => QuantityReserved - QuantityConsumed - QuantityReleased;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static StockReservation Create(InventoryBalance balance, string sourceType, Guid sourceId, Guid sourceLineId, decimal quantity)
    {
        if (quantity <= 0) throw new DomainRuleException("reservation.quantity", "Reserved quantity must be greater than zero.");
        return new StockReservation
        {
            BalanceId = balance.Id,
            WarehouseId = balance.WarehouseId,
            ProductId = balance.ProductId,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceLineId = sourceLineId,
            QuantityReserved = quantity,
        };
    }

    public void Consume(decimal quantity)
    {
        if (quantity <= 0 || quantity > Outstanding)
        {
            throw new DomainRuleException("reservation.consume", $"Cannot use {quantity:0.###}: only {Outstanding:0.###} is reserved.", isConflict: true);
        }
        QuantityConsumed += quantity;
        UpdateStatus();
    }

    /// <summary>Releases the given quantity, or everything outstanding when null. Returns the quantity released.</summary>
    public decimal Release(decimal? quantity = null)
    {
        var q = quantity ?? Outstanding;
        if (q < 0 || q > Outstanding)
        {
            throw new DomainRuleException("reservation.release", $"Cannot release {q:0.###}: only {Outstanding:0.###} is reserved.", isConflict: true);
        }
        QuantityReleased += q;
        UpdateStatus();
        return q;
    }

    private void UpdateStatus()
    {
        if (Outstanding > 0) return;
        Status = QuantityConsumed > 0 ? ReservationStatus.Consumed : ReservationStatus.Released;
    }
}
