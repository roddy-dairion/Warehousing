using Inventory.Domain.Products;
using Inventory.Domain.Purchasing;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers", t =>
        {
            t.HasCheckConstraint("ck_suppliers_code_upper", "code = upper(code)");
            t.HasCheckConstraint("ck_suppliers_lead_time", "lead_time_days IS NULL OR lead_time_days BETWEEN 0 AND 365");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Name);
        b.Property(x => x.ContactName).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.AddressLine1).HasMaxLength(200);
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasXminVersion();
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.SupplierReference).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.CloseReason).HasMaxLength(500);
        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => new { x.WarehouseId, x.CreatedAt });
        b.HasIndex(x => x.SupplierId);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.CanReceive);
        b.HasXminVersion();
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_lines", t =>
        {
            t.HasCheckConstraint("ck_purchase_order_lines_quantity", "quantity > 0 AND entered_quantity > 0");
            t.HasCheckConstraint("ck_purchase_order_lines_received", "received_quantity >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EnteredQuantity).Quantity();
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.ReceivedQuantity).Quantity();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Ignore(x => x.Outstanding);
        b.HasIndex(x => new { x.PurchaseOrderId, x.LineNo }).IsUnique();
        b.HasIndex(x => new { x.PurchaseOrderId, x.ProductId }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
