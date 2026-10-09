using Inventory.Domain.Auditing;
using Inventory.Domain.Customers;
using Inventory.Domain.Files;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
