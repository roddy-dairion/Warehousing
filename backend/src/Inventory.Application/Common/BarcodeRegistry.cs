using Inventory.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Common;

/// <summary>
/// A scanned code must identify exactly one thing, so barcodes are unique across products
/// and bin labels together (the database enforces uniqueness within each table).
/// </summary>
public sealed class BarcodeRegistry(IApplicationDbContext db)
{
    public async Task EnsureFreeAsync(string value, Guid? exceptProductId, Guid? exceptLocationId, CancellationToken cancellationToken)
    {
        var product = await db.ProductBarcodes.AsNoTracking()
            .Where(b => b.Value == value && b.ProductId != exceptProductId)
            .Select(b => db.Products.Where(p => p.Id == b.ProductId).Select(p => p.Sku).First())
            .FirstOrDefaultAsync(cancellationToken);
        if (product is not null)
        {
            throw new DomainRuleException("barcode.in_use", $"Barcode {value} is already used by product {product}.", isConflict: true);
        }

        var location = await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.Barcode == value && l.Id != exceptLocationId)
            .Select(l => l.Code)
            .FirstOrDefaultAsync(cancellationToken);
        if (location is not null)
        {
            throw new DomainRuleException("barcode.in_use", $"Barcode {value} is already used by bin {location}.", isConflict: true);
        }
    }
}
