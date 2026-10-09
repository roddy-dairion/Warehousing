using Inventory.Domain.Customers;
using Inventory.Domain.Files;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal static class ConfigExtensions
{
    /// <summary>Optimistic concurrency via PostgreSQL xmin.</summary>
    public static void HasXminVersion<T>(this EntityTypeBuilder<T> b) where T : class => b.Property<uint>("xmin").IsRowVersion();
}

internal sealed class WarehouseLocationConfiguration : IEntityTypeConfiguration<WarehouseLocation>
{
    public void Configure(EntityTypeBuilder<WarehouseLocation> b)
    {
        b.ToTable("warehouse_locations", t =>
        {
            t.HasCheckConstraint("ck_warehouse_locations_code_upper", "code = upper(code)");
            t.HasCheckConstraint("ck_warehouse_locations_pickable_storage", "NOT is_pickable OR type = 'Storage'");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(WarehouseLocation.CodeMaxLength).IsRequired();
        b.Property(x => x.Zone).HasMaxLength(32);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Barcode).HasMaxLength(64);
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasIndex(x => x.Barcode).IsUnique().HasFilter("barcode IS NOT NULL");
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasXminVersion();
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers", t => t.HasCheckConstraint("ck_customers_reference_upper", "reference = upper(reference)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.CompanyName);
        b.Property(x => x.ContactName).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.BillingLine1).HasMaxLength(200);
        b.Property(x => x.BillingLine2).HasMaxLength(200);
        b.Property(x => x.BillingCity).HasMaxLength(100);
        b.Property(x => x.BillingCountry).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasMany(x => x.Addresses).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasXminVersion();
    }
}

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> b)
    {
        b.ToTable("customer_addresses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Label).HasMaxLength(60).IsRequired();
        b.Property(x => x.Line1).HasMaxLength(200).IsRequired();
        b.Property(x => x.Line2).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.ContactName).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.HasIndex(x => x.CustomerId);
    }
}

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("product_categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasXminVersion();
    }
}

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.ToTable("units_of_measure", t => t.HasCheckConstraint("ck_units_decimals", "decimals BETWEEN 0 AND 3"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(10).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.HasXminVersion();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_sku_upper", "sku = upper(sku)");
            t.HasCheckConstraint("ck_products_min_stock", "min_stock_qty IS NULL OR min_stock_qty >= 0");
            t.HasCheckConstraint("ck_products_expiry_needs_lot", "NOT has_expiry OR is_lot_tracked");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(Product.SkuMaxLength).IsRequired();
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Name);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.MinStockQty).HasPrecision(18, 3);
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Units).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Barcodes).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Barcodes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasXminVersion();
    }
}

internal sealed class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> b)
    {
        b.ToTable("product_units", t => t.HasCheckConstraint("ck_product_units_factor", "factor_to_base > 0 AND factor_to_base <> 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FactorToBase).HasPrecision(18, 6);
        b.HasIndex(x => new { x.ProductId, x.UnitId }).IsUnique();
        b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> b)
    {
        b.ToTable("product_barcodes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Value).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Value).IsUnique();
        b.Property(x => x.Symbology).HasMaxLength(20);
        b.HasOne<ProductUnit>().WithMany().HasForeignKey(x => x.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("stored_files");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.OwnerType).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.OwnerType, x.OwnerId });
    }
}
