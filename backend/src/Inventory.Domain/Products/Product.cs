using System.Text.RegularExpressions;
using Inventory.Domain.Common;

namespace Inventory.Domain.Products;

/// <summary>
/// A stock item. Quantities are always stored in the base unit; <see cref="Units"/> are pack sizes
/// (e.g. carton = 12 base units). <see cref="Barcodes"/> may identify the base unit or a pack.
/// </summary>
public sealed partial class Product : Entity, IAuditable
{
    public const int SkuMaxLength = 64;

    private readonly List<ProductUnit> _units = [];
    private readonly List<ProductBarcode> _barcodes = [];

    private Product() { }

    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public decimal? MinStockQty { get; private set; }
    /// <summary>Reordering: order up to this level (null = up to <see cref="MinStockQty"/>).</summary>
    public decimal? MaxStockQty { get; private set; }
    /// <summary>Reordering: the supplier normally ordered from.</summary>
    public Guid? DefaultSupplierId { get; private set; }
    /// <summary>Reordering: unit of measure ordered in (the base unit or one of <see cref="Units"/>); suggestions round up to whole units.</summary>
    public Guid? PurchaseUnitId { get; private set; }
    public bool IsLotTracked { get; private set; }
    public bool HasExpiry { get; private set; }
    public int? ShelfLifeDays { get; private set; }
    public Guid? ImageId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<ProductUnit> Units => _units;
    public IReadOnlyList<ProductBarcode> Barcodes => _barcodes;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>SKU is set once: it identifies the product on labels, imports and history.</summary>
    public static Product Create(string sku, string name, Guid baseUnitId)
    {
        var p = new Product
        {
            Sku = Text.Code(sku, SkuPattern(), "product.sku_invalid", $"SKU must be 1–{SkuMaxLength} letters, digits, dots, slashes, hyphens or underscores."),
            BaseUnitId = baseUnitId,
        };
        p.Rename(name);
        return p;
    }

    public void Rename(string name) => Name = Text.Required(name, 200, "product.name_invalid", "Product name");

    public void Describe(string? description, Guid? categoryId)
    {
        Description = Text.Optional(description, 2000, "product.description_invalid", "Description");
        CategoryId = categoryId;
    }

    public void ChangeBaseUnit(Guid baseUnitId)
    {
        if (_units.Any(u => u.UnitId == baseUnitId))
        {
            throw new DomainRuleException("product.base_unit_is_pack", "The base unit cannot also be a pack unit of this product.");
        }
        BaseUnitId = baseUnitId;
    }

    public void SetStockRules(decimal? minStockQty, bool isLotTracked, bool hasExpiry, int? shelfLifeDays)
    {
        if (minStockQty < 0) throw new DomainRuleException("product.min_stock_invalid", "Minimum stock cannot be negative.");
        if (hasExpiry && !isLotTracked)
        {
            throw new DomainRuleException("product.expiry_needs_lot", "Products with expiry dates must be lot-tracked (expiry is recorded per lot).");
        }
        if (shelfLifeDays is not null && (!hasExpiry || shelfLifeDays <= 0 || shelfLifeDays > 36500))
        {
            throw new DomainRuleException("product.shelf_life_invalid", "Shelf life applies only to products with expiry and must be 1–36500 days.");
        }
        MinStockQty = minStockQty;
        IsLotTracked = isLotTracked;
        HasExpiry = hasExpiry;
        ShelfLifeDays = shelfLifeDays;
    }

    public void SetImage(Guid? imageId) => ImageId = imageId;

    /// <summary>Reorder settings. <paramref name="purchaseUnitId"/> is a unit of measure: the base unit or a pack unit of this product.</summary>
    public void SetReorder(decimal? maxStockQty, Guid? defaultSupplierId, Guid? purchaseUnitId)
    {
        if (maxStockQty < 0) throw new DomainRuleException("product.max_stock_invalid", "Order-up-to level cannot be negative.");
        if (maxStockQty is not null && MinStockQty is not null && maxStockQty < MinStockQty)
        {
            throw new DomainRuleException("product.max_below_min", "The order-up-to level must be at least the minimum stock.");
        }
        if (purchaseUnitId is { } unitId && unitId != BaseUnitId && _units.All(u => u.UnitId != unitId))
        {
            throw new DomainRuleException("product.purchase_unit_invalid", "The purchase unit must be the base unit or one of the product's pack units.");
        }
        MaxStockQty = maxStockQty;
        DefaultSupplierId = defaultSupplierId;
        PurchaseUnitId = purchaseUnitId == BaseUnitId ? null : purchaseUnitId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    // ---- Pack units -----------------------------------------------------------------------

    public ProductUnit AddUnit(Guid unitId, decimal factorToBase)
    {
        if (unitId == BaseUnitId) throw new DomainRuleException("product.unit_is_base", "This is already the base unit.");
        if (_units.Any(u => u.UnitId == unitId))
        {
            throw new DomainRuleException("product.unit_duplicate", "This pack unit is already defined for the product.", isConflict: true);
        }
        var unit = new ProductUnit(Id, unitId, ValidFactor(factorToBase));
        _units.Add(unit);
        return unit;
    }

    public void ChangeUnitFactor(Guid productUnitId, decimal factorToBase) => FindUnit(productUnitId).SetFactor(ValidFactor(factorToBase));

    public void RemoveUnit(Guid productUnitId)
    {
        var unit = FindUnit(productUnitId);
        if (_barcodes.Any(b => b.ProductUnitId == productUnitId))
        {
            throw new DomainRuleException("product.unit_has_barcodes", "Remove the barcodes of this pack unit first.", isConflict: true);
        }
        _units.Remove(unit);
        if (PurchaseUnitId == unit.UnitId) PurchaseUnitId = null;
    }

    // ---- Barcodes -------------------------------------------------------------------------

    /// <param name="productUnitId">Null = the barcode identifies one base unit.</param>
    public ProductBarcode AddBarcode(string value, Guid? productUnitId, string? symbology = null)
    {
        var normalized = Common.Barcodes.Normalize(value);
        if (_barcodes.Any(b => b.Value == normalized))
        {
            throw new DomainRuleException("barcode.duplicate", "This barcode is already on the product.", isConflict: true);
        }
        if (productUnitId is not null) FindUnit(productUnitId.Value);
        var barcode = new ProductBarcode(Id, normalized, productUnitId, Text.Optional(symbology, 20, "barcode.symbology_invalid", "Symbology"));
        _barcodes.Add(barcode);
        if (_barcodes.Count(b => b.ProductUnitId == productUnitId) == 1) barcode.SetPrimary(true);
        return barcode;
    }

    public void RemoveBarcode(Guid barcodeId)
    {
        var barcode = _barcodes.SingleOrDefault(b => b.Id == barcodeId) ?? throw new DomainRuleException("barcode.not_found", "Barcode not found.");
        _barcodes.Remove(barcode);
        if (barcode.IsPrimary)
        {
            _barcodes.FirstOrDefault(b => b.ProductUnitId == barcode.ProductUnitId)?.SetPrimary(true);
        }
    }

    /// <summary>One primary barcode per unit (printed on labels and shown first).</summary>
    public void SetPrimaryBarcode(Guid barcodeId)
    {
        var barcode = _barcodes.SingleOrDefault(b => b.Id == barcodeId) ?? throw new DomainRuleException("barcode.not_found", "Barcode not found.");
        foreach (var b in _barcodes.Where(b => b.ProductUnitId == barcode.ProductUnitId)) b.SetPrimary(b == barcode);
    }

    /// <summary>Converts a quantity in a pack unit (or the base unit when null) to base units.</summary>
    public decimal ToBaseQuantity(decimal quantity, Guid? productUnitId) =>
        productUnitId is null ? quantity : quantity * FindUnit(productUnitId.Value).FactorToBase;

    private ProductUnit FindUnit(Guid productUnitId) =>
        _units.SingleOrDefault(u => u.Id == productUnitId) ?? throw new DomainRuleException("product.unit_not_found", "Pack unit not found on this product.");

    private static decimal ValidFactor(decimal factor)
    {
        if (factor <= 0 || factor == 1 || factor > 1_000_000 || decimal.Round(factor, 6) != factor)
        {
            throw new DomainRuleException("product.unit_factor_invalid", "Pack size must be greater than 0, not 1, and have at most 6 decimals (e.g. 12 for a carton of 12).");
        }
        return factor;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9._/-]{0,63}$")]
    private static partial Regex SkuPattern();
}

/// <summary>A pack size of a product, e.g. CTN = 12 base units.</summary>
public sealed class ProductUnit : Entity
{
    private ProductUnit() { }

    internal ProductUnit(Guid productId, Guid unitId, decimal factorToBase)
    {
        ProductId = productId;
        UnitId = unitId;
        FactorToBase = factorToBase;
    }

    public Guid ProductId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal FactorToBase { get; private set; }

    internal void SetFactor(decimal factor) => FactorToBase = factor;
}

public sealed class ProductBarcode : Entity
{
    private ProductBarcode() { }

    internal ProductBarcode(Guid productId, string value, Guid? productUnitId, string? symbology)
    {
        ProductId = productId;
        Value = value;
        ProductUnitId = productUnitId;
        Symbology = symbology;
    }

    public Guid ProductId { get; private set; }
    public string Value { get; private set; } = null!;
    /// <summary>Null = one base unit; otherwise the pack this barcode is printed on.</summary>
    public Guid? ProductUnitId { get; private set; }
    public string? Symbology { get; private set; }
    public bool IsPrimary { get; private set; }

    internal void SetPrimary(bool value) => IsPrimary = value;
}
