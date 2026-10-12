using Inventory.Domain.Inventory;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal static class Qty
{
    /// <summary>Stock quantities: up to 15 integer digits and 3 decimals.</summary>
    public static PropertyBuilder<decimal> Quantity(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 3);
    public static PropertyBuilder<decimal?> Quantity(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 3);
}

internal sealed class ProductLotConfiguration : IEntityTypeConfiguration<ProductLot>
{
    public void Configure(EntityTypeBuilder<ProductLot> b)
    {
        b.ToTable("product_lots", t => t.HasCheckConstraint("ck_product_lots_lot_no_upper", "lot_no = upper(lot_no)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.LotNo).HasMaxLength(ProductLot.LotNoMaxLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => new { x.ProductId, x.LotNo }).IsUnique();
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasXminVersion();
    }
}

internal sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> b)
    {
        b.ToTable("inventory_balances", t =>
        {
            t.HasCheckConstraint("ck_inventory_balances_on_hand", "on_hand >= 0");
            t.HasCheckConstraint("ck_inventory_balances_reserved", "reserved >= 0 AND reserved <= on_hand");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.OnHand).Quantity();
        b.Property(x => x.Reserved).Quantity();
        b.Property(x => x.Available).Quantity().HasComputedColumnSql("on_hand - reserved", stored: true);
        // One row per warehouse × bin × product × lot; a null lot (non-lot products) counts as one value.
        b.HasIndex(x => new { x.WarehouseId, x.LocationId, x.ProductId, x.LotId }).IsUnique().AreNullsDistinct(false);
        b.HasIndex(x => new { x.ProductId, x.WarehouseId });
        b.HasIndex(x => x.LocationId);
        b.HasIndex(x => x.LotId);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.ToTable("inventory_movements", t =>
        {
            t.HasCheckConstraint("ck_inventory_movements_quantity", "quantity <> 0");
            t.HasCheckConstraint("ck_inventory_movements_on_hand_after", "on_hand_after >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        b.HasIndex(x => x.Sequence).IsUnique();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.OnHandAfter).Quantity();
        b.Property(x => x.SourceType).HasMaxLength(24).IsRequired();
        b.Property(x => x.SourceNumber).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(500);
        // A document line posts at most once per movement type (backs up the status checks).
        b.HasIndex(x => new { x.SourceType, x.SourceLineId, x.Type }).IsUnique();
        b.HasIndex(x => new { x.SourceType, x.SourceId });
        b.HasIndex(x => new { x.WarehouseId, x.OccurredAt }).IsDescending(false, true);
        b.HasIndex(x => new { x.ProductId, x.OccurredAt }).IsDescending(false, true);
        b.HasIndex(x => x.BalanceId);
        b.HasOne<InventoryBalance>().WithMany().HasForeignKey(x => x.BalanceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> b)
    {
        b.ToTable("stock_reservations", t =>
        {
            t.HasCheckConstraint("ck_stock_reservations_quantities",
                "quantity_reserved > 0 AND quantity_consumed >= 0 AND quantity_released >= 0 AND quantity_consumed + quantity_released <= quantity_reserved");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceType).HasMaxLength(24).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.QuantityReserved).Quantity();
        b.Property(x => x.QuantityConsumed).Quantity();
        b.Property(x => x.QuantityReleased).Quantity();
        b.Ignore(x => x.Outstanding);
        b.HasIndex(x => new { x.SourceType, x.SourceId });
        b.HasIndex(x => x.BalanceId).HasFilter("status = 'Open'");
        b.HasOne<InventoryBalance>().WithMany().HasForeignKey(x => x.BalanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasXminVersion();
    }
}

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("goods_receipts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.SupplierName).HasMaxLength(200);
        b.Property(x => x.SupplierReference).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ReversalReason).HasMaxLength(500);
        b.HasIndex(x => new { x.WarehouseId, x.CreatedAt });
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasXminVersion();
    }
}

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> b)
    {
        b.ToTable("goods_receipt_lines", t => t.HasCheckConstraint("ck_goods_receipt_lines_quantity", "quantity > 0 AND entered_quantity > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EnteredQuantity).Quantity();
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.ExpectedQuantity).Quantity();
        b.Property(x => x.LotNo).HasMaxLength(ProductLot.LotNoMaxLength);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.ReceiptId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("stock_adjustments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.HasIndex(x => new { x.WarehouseId, x.CreatedAt });
        b.HasIndex(x => x.Status);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.AdjustmentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasXminVersion();
    }
}

internal sealed class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> b)
    {
        b.ToTable("stock_adjustment_lines", t => t.HasCheckConstraint("ck_stock_adjustment_lines_delta", "quantity_delta <> 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.QuantityDelta).Quantity();
        b.Property(x => x.LotNo).HasMaxLength(ProductLot.LotNoMaxLength);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.AdjustmentId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.FromWarehouseId, x.PostedAt });
        b.HasIndex(x => new { x.ToWarehouseId, x.PostedAt });
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasXminVersion();
    }
}

internal sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> b)
    {
        b.ToTable("stock_transfer_lines", t =>
        {
            t.HasCheckConstraint("ck_stock_transfer_lines_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_stock_transfer_lines_bins", "from_location_id <> to_location_id");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Quantity).Quantity();
        b.HasIndex(x => new { x.TransferId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> b)
    {
        b.ToTable("document_sequences", t => t.HasCheckConstraint("ck_document_sequences_last_value", "last_value > 0"));
        b.HasKey(x => new { x.Prefix, x.WarehouseId, x.Year });
        b.Property(x => x.Prefix).HasMaxLength(8);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.Endpoint).HasMaxLength(200).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ResponseContentType).HasMaxLength(100);
        b.Property(x => x.ResponseBody).HasColumnType("text");
        // Keys are per user: two users can never collide or replay each other's responses.
        b.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
        b.HasIndex(x => x.CreatedAt);
    }
}
