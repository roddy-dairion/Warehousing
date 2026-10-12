using Inventory.Domain.Auditing;
using Inventory.Domain.Customers;
using Inventory.Domain.Files;
using Inventory.Domain.Inventory;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Inventory.Application.Common;

/// <summary>Data access used by application services.</summary>
public interface IApplicationDbContext
{
    DbSet<Warehouse> Warehouses { get; }
    DbSet<WarehouseLocation> WarehouseLocations { get; }
    DbSet<Customer> Customers { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductBarcode> ProductBarcodes { get; }
    DbSet<StoredFile> StoredFiles { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ProductLot> ProductLots { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<StockReservation> StockReservations { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<StockTransfer> StockTransfers { get; }

    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
