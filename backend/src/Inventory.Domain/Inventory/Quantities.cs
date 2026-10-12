using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public static class Quantities
{
    /// <summary>Precision of stock columns (numeric(18,3)).</summary>
    public const int MaxDecimals = 3;

    /// <summary>Validates a positive quantity respecting the unit's allowed decimals.</summary>
    public static decimal Positive(decimal quantity, int unitDecimals, string unitCode, string field = "quantity")
    {
        if (quantity <= 0) throw new DomainRuleException($"{field}.not_positive", "Quantity must be greater than zero.");
        return Precision(quantity, unitDecimals, unitCode, field);
    }

    public static decimal Precision(decimal quantity, int unitDecimals, string unitCode, string field = "quantity")
    {
        var decimals = Math.Min(unitDecimals, MaxDecimals);
        if (decimal.Round(quantity, decimals) != quantity)
        {
            throw new DomainRuleException($"{field}.precision", decimals == 0
                ? $"Quantity must be a whole number of {unitCode}."
                : $"Quantity can have at most {decimals} decimals for {unitCode}.");
        }
        if (Math.Abs(quantity) >= 1_000_000_000_000m) throw new DomainRuleException($"{field}.too_large", "Quantity is too large.");
        return quantity;
    }
}
