namespace Inventory.Application.Products;

public sealed record ProductListItemDto(
    Guid Id,
    string Sku,
    string Name,
    string? CategoryName,
    string BaseUnitCode,
    string? PrimaryBarcode,
    decimal? MinStockQty,
    bool IsLotTracked,
    bool HasExpiry,
    bool HasImage,
    bool IsActive);

public sealed record ProductUnitDto(Guid Id, Guid UnitId, string UnitCode, string UnitName, decimal FactorToBase);

public sealed record ProductBarcodeDto(Guid Id, string Value, Guid? ProductUnitId, string UnitCode, decimal FactorToBase, string? Symbology, bool IsPrimary);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    Guid BaseUnitId,
    string BaseUnitCode,
    int BaseUnitDecimals,
    decimal? MinStockQty,
    bool IsLotTracked,
    bool HasExpiry,
    int? ShelfLifeDays,
    bool HasImage,
    bool IsActive,
    IReadOnlyList<ProductUnitDto> Units,
    IReadOnlyList<ProductBarcodeDto> Barcodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    decimal? MaxStockQty,
    Guid? DefaultSupplierId,
    string? DefaultSupplierName,
    Guid? PurchaseUnitId);

public sealed class ProductListQuery : Common.ListQuery
{
    public Guid? CategoryId { get; set; }
}

public sealed record NewUnitRequest(Guid UnitId, decimal FactorToBase);

public sealed record NewBarcodeRequest(string Value, Guid? UnitId, string? Symbology);

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    Guid BaseUnitId,
    decimal? MinStockQty,
    bool IsLotTracked,
    bool HasExpiry,
    int? ShelfLifeDays,
    IReadOnlyList<NewUnitRequest>? Units,
    IReadOnlyList<NewBarcodeRequest>? Barcodes,
    decimal? MaxStockQty = null,
    Guid? DefaultSupplierId = null,
    Guid? PurchaseUnitId = null);

public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    Guid BaseUnitId,
    decimal? MinStockQty,
    bool IsLotTracked,
    bool HasExpiry,
    int? ShelfLifeDays,
    uint Version,
    decimal? MaxStockQty = null,
    Guid? DefaultSupplierId = null,
    Guid? PurchaseUnitId = null);

public sealed record AddUnitRequest(Guid UnitId, decimal FactorToBase, uint Version);

public sealed record ChangeUnitRequest(decimal FactorToBase, uint Version);

/// <param name="UnitId">The pack unit (unit of measure id) the barcode is printed on; null = base unit.</param>
public sealed record AddBarcodeRequest(string Value, Guid? UnitId, string? Symbology, uint Version);

public sealed record VersionRequest(uint Version);

/// <summary>Result of scanning a product barcode.</summary>
public sealed record BarcodeLookupDto(
    Guid ProductId,
    string Sku,
    string Name,
    bool IsActive,
    string Barcode,
    string UnitCode,
    decimal FactorToBase,
    string BaseUnitCode,
    bool IsLotTracked,
    bool HasExpiry);
