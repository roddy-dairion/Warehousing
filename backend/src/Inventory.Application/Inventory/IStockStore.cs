using Inventory.Domain.Inventory;

namespace Inventory.Application.Inventory;

/// <summary>Identifies one balance row: warehouse × bin × product × lot.</summary>
public readonly record struct BalanceKey(Guid WarehouseId, Guid LocationId, Guid ProductId, Guid? LotId);

/// <summary>
/// Database operations that need explicit SQL: row locks, get-or-create of keyed rows, and
/// document numbering. All methods must run inside the caller's transaction.
/// </summary>
public interface IStockStore
{
    /// <summary>
    /// Returns the balances for the keys, locked FOR UPDATE in ascending id order (prevents deadlocks).
    /// Missing balances are created first when <paramref name="createMissing"/> is true; otherwise they are absent from the result.
    /// Returned entities are tracked and reflect the locked (current) row values.
    /// </summary>
    Task<IReadOnlyDictionary<BalanceKey, InventoryBalance>> LockBalancesAsync(IReadOnlyCollection<BalanceKey> keys, bool createMissing, CancellationToken cancellationToken);

    /// <summary>Locks balances by id (ascending id order) and returns them tracked with current values.</summary>
    Task<IReadOnlyList<InventoryBalance>> LockBalancesByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Returns the lot, creating it if needed (safe under concurrency).</summary>
    Task<ProductLot> GetOrCreateLotAsync(Guid productId, string lotNo, DateOnly? expiryDate, CancellationToken cancellationToken);

    /// <summary>Locks a document row (e.g. a receipt) FOR UPDATE so concurrent posts are serialised.</summary>
    Task LockRowAsync(string table, Guid id, CancellationToken cancellationToken);

    /// <summary>Next readable number for a document type, e.g. GR-MAIN-2026-000001 (gap-free within a committed transaction).</summary>
    Task<string> NextNumberAsync(string prefix, Guid warehouseId, string warehouseCode, int year, CancellationToken cancellationToken);
}
