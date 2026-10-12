using System.Linq.Expressions;
using System.Security.Cryptography;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Files;
using Inventory.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Products;

public sealed class ProductService(IApplicationDbContext db, BarcodeRegistry barcodes, IFileStorage files, ICurrentUser user, TimeProvider clock, IAuditWriter audit)
{
    public const long MaxImageBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, Expression<Func<Product, object?>>> Sorts = new()
    {
        ["sku"] = p => p.Sku,
        ["name"] = p => p.Name,
        ["minStockQty"] = p => p.MinStockQty,
        ["isActive"] = p => p.IsActive,
        ["updatedAt"] = p => p.UpdatedAt,
    };

    public Task<PagedResult<ProductListItemDto>> ListAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        var q = db.Products.AsNoTracking();
        if (query.Term is { } term)
        {
            // Exact barcode match, or SKU/name contains.
            q = q.Where(p => p.Sku.ToLower().Contains(term) || p.Name.ToLower().Contains(term) || p.Barcodes.Any(b => b.Value.ToLower() == term));
        }
        if (query.IsActive is { } active) q = q.Where(p => p.IsActive == active);
        if (query.CategoryId is { } categoryId) q = q.Where(p => p.CategoryId == categoryId);

        return q.SortBy(query, Sorts, "sku", p => p.Id)
            .Select(p => new ProductListItemDto(
                p.Id,
                p.Sku,
                p.Name,
                db.ProductCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
                p.Barcodes.Where(b => b.IsPrimary && b.ProductUnitId == null).Select(b => b.Value).FirstOrDefault(),
                p.MinStockQty,
                p.IsLotTracked,
                p.HasExpiry,
                p.ImageId != null,
                p.IsActive))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var p = await db.Products.AsNoTracking().Include(x => x.Units).Include(x => x.Barcodes).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product not found.");
        var version = await db.Products.Where(x => x.Id == id).Select(x => EF.Property<uint>(x, Versioning.Column)).SingleAsync(cancellationToken);
        var units = await db.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Id, cancellationToken);
        var category = p.CategoryId is null ? null : await db.ProductCategories.AsNoTracking().Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        var baseUnit = units[p.BaseUnitId];

        var unitDtos = p.Units.Select(u => new ProductUnitDto(u.Id, u.UnitId, units[u.UnitId].Code, units[u.UnitId].Name, u.FactorToBase))
            .OrderBy(u => u.FactorToBase).ToList();
        var barcodeDtos = p.Barcodes.Select(b =>
        {
            var pu = b.ProductUnitId is null ? null : p.Units.Single(u => u.Id == b.ProductUnitId);
            return new ProductBarcodeDto(b.Id, b.Value, b.ProductUnitId, pu is null ? baseUnit.Code : units[pu.UnitId].Code, pu?.FactorToBase ?? 1, b.Symbology, b.IsPrimary);
        }).OrderBy(b => b.FactorToBase).ThenByDescending(b => b.IsPrimary).ThenBy(b => b.Value).ToList();

        var supplier = p.DefaultSupplierId is null ? null : await db.Suppliers.AsNoTracking().Where(s => s.Id == p.DefaultSupplierId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);
        return new ProductDto(p.Id, p.Sku, p.Name, p.Description, p.CategoryId, category, p.BaseUnitId, baseUnit.Code, baseUnit.Decimals,
            p.MinStockQty, p.IsLotTracked, p.HasExpiry, p.ShelfLifeDays, p.ImageId is not null, p.IsActive, unitDtos, barcodeDtos, p.CreatedAt, p.UpdatedAt, version,
            p.MaxStockQty, p.DefaultSupplierId, supplier, p.PurchaseUnitId);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest r, CancellationToken cancellationToken)
    {
        await EnsureUnitUsableAsync(r.BaseUnitId, cancellationToken);
        await EnsureCategoryUsableAsync(r.CategoryId, cancellationToken);
        var product = Product.Create(r.Sku, r.Name, r.BaseUnitId);
        await EnsureSkuFreeAsync(product.Sku, cancellationToken);
        product.Describe(r.Description, r.CategoryId);
        product.SetStockRules(await ValidMinStockAsync(r.BaseUnitId, r.MinStockQty, cancellationToken), r.IsLotTracked, r.HasExpiry, r.ShelfLifeDays);
        foreach (var u in r.Units ?? [])
        {
            await EnsureUnitUsableAsync(u.UnitId, cancellationToken);
            product.AddUnit(u.UnitId, u.FactorToBase);
        }
        foreach (var b in r.Barcodes ?? [])
        {
            await AddBarcodeInternalAsync(product, b.Value, b.UnitId, b.Symbology, cancellationToken);
        }
        await SetReorderAsync(product, r.MaxStockQty, r.DefaultSupplierId, r.PurchaseUnitId, cancellationToken);
        db.Products.Add(product);
        audit.Add("product.created", "product", product.Id.ToString(), data: new { product.Sku, product.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest r, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(product, r.Version);
        await EnsureCategoryUsableAsync(r.CategoryId, cancellationToken);
        var stockRulesChanged = r.BaseUnitId != product.BaseUnitId || r.IsLotTracked != product.IsLotTracked || r.HasExpiry != product.HasExpiry;
        if (stockRulesChanged && await db.InventoryMovements.AnyAsync(m => m.ProductId == id, cancellationToken))
        {
            // Stock is recorded in the base unit and keyed by lot: changing either would make existing stock and history wrong.
            throw new DomainRuleException("product.has_stock_history",
                "The base unit, lot tracking and expiry tracking cannot be changed once the product has stock movements. Create a new product instead.", isConflict: true);
        }
        if (r.BaseUnitId != product.BaseUnitId)
        {
            await EnsureUnitUsableAsync(r.BaseUnitId, cancellationToken);
            product.ChangeBaseUnit(r.BaseUnitId);
        }
        product.Rename(r.Name);
        product.Describe(r.Description, r.CategoryId);
        product.SetStockRules(await ValidMinStockAsync(r.BaseUnitId, r.MinStockQty, cancellationToken), r.IsLotTracked, r.HasExpiry, r.ShelfLifeDays);
        await SetReorderAsync(product, r.MaxStockQty, r.DefaultSupplierId, r.PurchaseUnitId, cancellationToken);
        audit.Add("product.updated", "product", id.ToString(), data: new { product.Sku, product.Name, product.IsLotTracked, product.HasExpiry });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task SetReorderAsync(Product product, decimal? maxStock, Guid? supplierId, Guid? purchaseUnitId, CancellationToken cancellationToken)
    {
        if (supplierId is { } sid && sid != product.DefaultSupplierId && !await db.Suppliers.AnyAsync(s => s.Id == sid && s.IsActive, cancellationToken))
        {
            throw new DomainRuleException("supplier.unknown", "Choose an active supplier.");
        }
        product.SetReorder(await ValidMinStockAsync(product.BaseUnitId, maxStock, cancellationToken), supplierId, purchaseUnitId);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken);
        if (product.IsActive == active) return;
        if (active) product.Activate(); else product.Deactivate();
        audit.Add(active ? "product.activated" : "product.deactivated", "product", id.ToString(), data: new { product.Sku });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<ProductDto> AddUnitAsync(Guid id, AddUnitRequest r, CancellationToken cancellationToken) =>
        ChangeAsync(id, r.Version, "product.unit_added", async p =>
        {
            await EnsureUnitUsableAsync(r.UnitId, cancellationToken);
            p.AddUnit(r.UnitId, r.FactorToBase);
        }, cancellationToken);

    public Task<ProductDto> ChangeUnitAsync(Guid id, Guid productUnitId, ChangeUnitRequest r, CancellationToken cancellationToken) =>
        ChangeAsync(id, r.Version, "product.unit_changed", p =>
        {
            p.ChangeUnitFactor(productUnitId, r.FactorToBase);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<ProductDto> RemoveUnitAsync(Guid id, Guid productUnitId, uint version, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, "product.unit_removed", p =>
        {
            p.RemoveUnit(productUnitId);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<ProductDto> AddBarcodeAsync(Guid id, AddBarcodeRequest r, CancellationToken cancellationToken) =>
        ChangeAsync(id, r.Version, "product.barcode_added", p => AddBarcodeInternalAsync(p, r.Value, r.UnitId, r.Symbology, cancellationToken), cancellationToken);

    public Task<ProductDto> RemoveBarcodeAsync(Guid id, Guid barcodeId, uint version, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, "product.barcode_removed", p =>
        {
            p.RemoveBarcode(barcodeId);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<ProductDto> SetPrimaryBarcodeAsync(Guid id, Guid barcodeId, uint version, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, "product.barcode_primary", p =>
        {
            p.SetPrimaryBarcode(barcodeId);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Barcode scan. Inactive products are returned (flagged) so the scanner can warn; unknown codes are 404.</summary>
    public async Task<BarcodeLookupDto> LookupBarcodeAsync(string value, CancellationToken cancellationToken)
    {
        var code = (value ?? string.Empty).Trim();
        var hit = await (
            from b in db.ProductBarcodes.AsNoTracking()
            where b.Value == code
            join p in db.Products on b.ProductId equals p.Id
            join bu in db.UnitsOfMeasure on p.BaseUnitId equals bu.Id
            select new
            {
                p.Id, p.Sku, p.Name, p.IsActive, b.Value, b.ProductUnitId, BaseCode = bu.Code, p.IsLotTracked, p.HasExpiry,
                Pack = p.Units.Where(u => u.Id == b.ProductUnitId).Select(u => new
                {
                    u.FactorToBase,
                    Code = db.UnitsOfMeasure.Where(x => x.Id == u.UnitId).Select(x => x.Code).First(),
                }).FirstOrDefault(),
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Barcode {code} is not recognised.");

        return new BarcodeLookupDto(hit.Id, hit.Sku, hit.Name, hit.IsActive, hit.Value,
            hit.Pack?.Code ?? hit.BaseCode, hit.Pack?.FactorToBase ?? 1, hit.BaseCode, hit.IsLotTracked, hit.HasExpiry);
    }

    // ---- Image ---------------------------------------------------------------------------

    public async Task SetImageAsync(Guid id, string fileName, Stream content, long length, CancellationToken cancellationToken)
    {
        if (length is <= 0 or > MaxImageBytes)
        {
            throw new DomainRuleException("image.size", "The image must be smaller than 2 MB.");
        }
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImageBytes) throw new DomainRuleException("image.size", "The image must be smaller than 2 MB.");
        var contentType = ImageSniffer.Detect(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
            ?? throw new DomainRuleException("image.type", "Only JPEG, PNG or WebP images are accepted.");

        var product = await LoadAsync(id, cancellationToken);
        buffer.Position = 0;
        var key = await files.SaveAsync(buffer, cancellationToken);
        var sha = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
        var stored = StoredFile.Create(fileName, contentType, buffer.Length, key, sha, "product", id, clock.GetUtcNow(), user.IdOrNull);
        db.StoredFiles.Add(stored);

        var previous = product.ImageId;
        product.SetImage(stored.Id);
        audit.Add("product.image_set", "product", id.ToString(), data: new { stored.FileName, stored.SizeBytes });
        await db.SaveChangesAsync(cancellationToken);
        if (previous is not null) await DeleteStoredAsync(previous.Value, cancellationToken);
    }

    public async Task RemoveImageAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken);
        if (product.ImageId is not { } imageId) return;
        product.SetImage(null);
        audit.Add("product.image_removed", "product", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await DeleteStoredAsync(imageId, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType, string Sha256)> GetImageAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await (from p in db.Products.AsNoTracking()
                          where p.Id == id
                          join f in db.StoredFiles on p.ImageId equals f.Id
                          select f).SingleOrDefaultAsync(cancellationToken)
                   ?? throw new NotFoundException("This product has no image.");
        var stream = await files.OpenReadAsync(file.StorageKey, cancellationToken) ?? throw new NotFoundException("Image file is missing.");
        return (stream, file.ContentType, file.Sha256);
    }

    // ---- Internals -----------------------------------------------------------------------

    internal async Task EnsureSkuFreeAsync(string sku, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            throw new DomainRuleException("product.sku_taken", $"SKU {sku} already exists.", isConflict: true);
        }
    }

    internal async Task AddBarcodeInternalAsync(Product product, string value, Guid? unitId, string? symbology, CancellationToken cancellationToken)
    {
        Guid? productUnitId = null;
        if (unitId is not null && unitId != product.BaseUnitId)
        {
            productUnitId = product.Units.SingleOrDefault(u => u.UnitId == unitId)?.Id
                ?? throw new DomainRuleException("barcode.unit_missing", "Add the pack unit to the product before giving it a barcode.");
        }
        var normalized = Domain.Common.Barcodes.Normalize(value);
        await barcodes.EnsureFreeAsync(normalized, product.Id, null, cancellationToken);
        product.AddBarcode(normalized, productUnitId, symbology);
    }

    internal async Task EnsureUnitUsableAsync(Guid unitId, CancellationToken cancellationToken)
    {
        if (!await db.UnitsOfMeasure.AnyAsync(u => u.Id == unitId && u.IsActive, cancellationToken))
        {
            throw new DomainRuleException("unit.unknown", "The unit does not exist or is inactive.");
        }
    }

    internal async Task EnsureCategoryUsableAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null) return;
        if (!await db.ProductCategories.AnyAsync(c => c.Id == categoryId && c.IsActive, cancellationToken))
        {
            throw new DomainRuleException("category.unknown", "The category does not exist or is inactive.");
        }
    }

    /// <summary>Minimum stock must respect the base unit's decimals (e.g. no 2.5 "each").</summary>
    internal async Task<decimal?> ValidMinStockAsync(Guid baseUnitId, decimal? value, CancellationToken cancellationToken)
    {
        if (value is null) return null;
        var decimals = await db.UnitsOfMeasure.Where(u => u.Id == baseUnitId).Select(u => u.Decimals).SingleAsync(cancellationToken);
        if (decimal.Round(value.Value, decimals) != value.Value)
        {
            throw new DomainRuleException("product.min_stock_precision", $"Minimum stock can have at most {decimals} decimals for this unit.");
        }
        return value;
    }

    private async Task<ProductDto> ChangeAsync(Guid id, uint version, string action, Func<Product, Task> change, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(product, version);
        await change(product);
        audit.Add(action, "product", id.ToString(), data: new { product.Sku });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Product> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products.Include(p => p.Units).Include(p => p.Barcodes).SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException("Product not found.");

    private async Task DeleteStoredAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) return;
        db.StoredFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(file.StorageKey, cancellationToken);
    }
}

/// <summary>Detects the real image type from its first bytes (the client-supplied content type is ignored).</summary>
public static class ImageSniffer
{
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
