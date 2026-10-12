using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Inventory;

/// <summary>Product facts needed to validate stock lines.</summary>
public sealed record StockProduct(Product Product, string BaseUnitCode, int BaseUnitDecimals, IReadOnlyDictionary<Guid, (string Code, int Decimals)> PackUnits);

/// <summary>Shared validation for stock documents (receipts, adjustments, moves).</summary>
public sealed class StockRules(IApplicationDbContext db)
{
    private readonly Dictionary<Guid, StockProduct> _products = [];
    private readonly Dictionary<Guid, WarehouseLocation> _locations = [];

    public async Task<StockProduct> ProductAsync(Guid productId, CancellationToken cancellationToken, bool requireActive = true)
    {
        if (!_products.TryGetValue(productId, out var sp))
        {
            var product = await db.Products.AsNoTracking().Include(p => p.Units).SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
                ?? throw new DomainRuleException("product.unknown", "Product not found.");
            var unitIds = product.Units.Select(u => u.UnitId).Append(product.BaseUnitId).ToList();
            var units = await db.UnitsOfMeasure.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
            var baseUnit = units[product.BaseUnitId];
            sp = new StockProduct(product, baseUnit.Code, baseUnit.Decimals,
                product.Units.ToDictionary(u => u.Id, u => (units[u.UnitId].Code, units[u.UnitId].Decimals)));
            _products[productId] = sp;
        }
        if (requireActive && !sp.Product.IsActive)
        {
            throw new DomainRuleException("product.inactive", $"Product {sp.Product.Sku} is inactive.");
        }
        return sp;
    }

    /// <summary>The bin must belong to the warehouse and be active.</summary>
    public async Task<WarehouseLocation> LocationAsync(Guid warehouseId, Guid locationId, CancellationToken cancellationToken)
    {
        if (!_locations.TryGetValue(locationId, out var location))
        {
            location = await db.WarehouseLocations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == locationId, cancellationToken)
                ?? throw new DomainRuleException("location.unknown", "Bin not found.");
            _locations[locationId] = location;
        }
        if (location.WarehouseId != warehouseId) throw new DomainRuleException("location.other_warehouse", $"Bin {location.Code} belongs to another warehouse.");
        if (!location.IsActive) throw new DomainRuleException("location.inactive", $"Bin {location.Code} is inactive.");
        return location;
    }

    /// <summary>Converts an entered quantity (in a pack unit or the base unit) to base units, checking precision of both.</summary>
    public static decimal ToBase(StockProduct sp, decimal entered, Guid? productUnitId)
    {
        if (productUnitId is null)
        {
            return Quantities.Positive(entered, sp.BaseUnitDecimals, sp.BaseUnitCode);
        }
        if (!sp.PackUnits.TryGetValue(productUnitId.Value, out var pack))
        {
            throw new DomainRuleException("product.unit_not_found", $"That pack unit is not defined for {sp.Product.Sku}.");
        }
        Quantities.Positive(entered, pack.Decimals, pack.Code);
        var baseQty = sp.Product.ToBaseQuantity(entered, productUnitId);
        return Quantities.Precision(baseQty, sp.BaseUnitDecimals, sp.BaseUnitCode);
    }

    /// <summary>Lot-tracked products need a lot number; others must not have one. Products with expiry need an expiry date for new lots.</summary>
    public static void CheckLot(StockProduct sp, string? lotNo, DateOnly? expiryDate, bool requireExpiry)
    {
        var p = sp.Product;
        if (p.IsLotTracked && string.IsNullOrWhiteSpace(lotNo))
        {
            throw new DomainRuleException("lot.required", $"{p.Sku} is lot-tracked: enter the lot number.");
        }
        if (!p.IsLotTracked && !string.IsNullOrWhiteSpace(lotNo))
        {
            throw new DomainRuleException("lot.not_tracked", $"{p.Sku} is not lot-tracked: leave the lot number empty.");
        }
        if (!p.HasExpiry && expiryDate is not null)
        {
            throw new DomainRuleException("lot.expiry_not_tracked", $"{p.Sku} does not track expiry dates.");
        }
        if (requireExpiry && p.HasExpiry && expiryDate is null)
        {
            throw new DomainRuleException("lot.expiry_required", $"{p.Sku} has expiry dates: enter the expiry date.");
        }
    }

    /// <summary>Lot id for an existing lot (null for non-lot products). Throws when the lot does not exist.</summary>
    public async Task<Guid?> ExistingLotIdAsync(StockProduct sp, string? lotNo, CancellationToken cancellationToken)
    {
        if (!sp.Product.IsLotTracked) return null;
        var normalized = ProductLot.NormalizeLotNo(lotNo);
        return await db.ProductLots.Where(l => l.ProductId == sp.Product.Id && l.LotNo == normalized).Select(l => (Guid?)l.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleException("lot.unknown", $"Lot {normalized} of {sp.Product.Sku} does not exist.");
    }
}
