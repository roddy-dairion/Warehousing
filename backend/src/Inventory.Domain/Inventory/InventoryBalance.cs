using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

/// <summary>
/// Current stock of one product (and lot) in one bin. Changed only through the stock ledger,
/// always together with a movement or reservation. Quantities are in the product's base unit.
/// </summary>
public sealed class InventoryBalance : Entity
{
    private InventoryBalance() { }

    public Guid WarehouseId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? LotId { get; private set; }
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }
    /// <summary>Generated column: OnHand − Reserved.</summary>
    public decimal Available { get; private set; }
    public DateTimeOffset? LastMovementAt { get; private set; }

    /// <summary>
    /// Applies a change and enforces the invariants: on hand ≥ 0, 0 ≤ reserved ≤ on hand.
    /// </summary>
    public void Apply(decimal onHandDelta, decimal reservedDelta, DateTimeOffset at)
    {
        var onHand = OnHand + onHandDelta;
        var reserved = Reserved + reservedDelta;
        if (onHand < 0)
        {
            throw new DomainRuleException("stock.insufficient", $"Not enough stock: {OnHand:0.###} on hand, {(-onHandDelta):0.###} needed.", isConflict: true);
        }
        if (reserved < 0)
        {
            throw new DomainRuleException("stock.reservation_negative", "Cannot release more than is reserved.", isConflict: true);
        }
        if (reserved > onHand)
        {
            throw new DomainRuleException("stock.reserved_exceeds_on_hand",
                $"Not enough free stock: {onHand:0.###} would remain on hand but {reserved:0.###} is reserved for dispatches.", isConflict: true);
        }
        OnHand = onHand;
        Reserved = reserved;
        Available = onHand - reserved;
        if (onHandDelta != 0) LastMovementAt = at;
    }
}
