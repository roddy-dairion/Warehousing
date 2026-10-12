using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Inventory;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Inventory.Infrastructure.Services;

/// <summary>PostgreSQL implementation of row locks, get-or-create and document numbering.</summary>
internal sealed class StockStore(AppDbContext db, ICurrentUser user, TimeProvider clock) : IStockStore
{
    /// <summary>Tables whose rows may be locked through <see cref="LockRowAsync"/> (table names cannot be parameters).</summary>
    private static readonly HashSet<string> LockableTables = ["goods_receipts", "stock_adjustments", "stock_transfers", "product_lots", "dispatches"];

    public async Task<IReadOnlyDictionary<BalanceKey, InventoryBalance>> LockBalancesAsync(IReadOnlyCollection<BalanceKey> keys, bool createMissing, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (keys.Count == 0) return new Dictionary<BalanceKey, InventoryBalance>();

        var wh = keys.Select(k => k.WarehouseId).ToArray();
        var loc = keys.Select(k => k.LocationId).ToArray();
        var prod = keys.Select(k => k.ProductId).ToArray();
        var lot = keys.Select(k => k.LotId).ToArray();

        if (createMissing)
        {
            // Concurrent inserts of the same key are resolved by the unique index; the loser simply finds the row below.
            var ids = keys.Select(_ => Guid.CreateVersion7()).ToArray();
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO inventory_balances (id, warehouse_id, location_id, product_id, lot_id, on_hand, reserved)
                SELECT k.id, k.w, k.l, k.p, k.lot, 0, 0
                FROM unnest(@ids, @wh, @loc, @prod, @lot) AS k(id, w, l, p, lot)
                ON CONFLICT DO NOTHING
                """,
                [UuidArray("ids", ids.Cast<Guid?>().ToArray()), UuidArray("wh", wh.Cast<Guid?>().ToArray()), UuidArray("loc", loc.Cast<Guid?>().ToArray()),
                 UuidArray("prod", prod.Cast<Guid?>().ToArray()), UuidArray("lot", lot)],
                cancellationToken);
        }

        DetachUnchangedBalances();
        var rows = await db.InventoryBalances
            .FromSqlRaw(
                """
                SELECT b.* FROM inventory_balances b
                JOIN unnest(@wh, @loc, @prod, @lot) AS k(w, l, p, lot)
                  ON b.warehouse_id = k.w AND b.location_id = k.l AND b.product_id = k.p AND b.lot_id IS NOT DISTINCT FROM k.lot
                ORDER BY b.id
                FOR UPDATE OF b
                """,
                UuidArray("wh", wh.Cast<Guid?>().ToArray()), UuidArray("loc", loc.Cast<Guid?>().ToArray()),
                UuidArray("prod", prod.Cast<Guid?>().ToArray()), UuidArray("lot", lot))
            .AsTracking()
            .ToListAsync(cancellationToken);

        var result = new Dictionary<BalanceKey, InventoryBalance>();
        foreach (var b in rows) result[new BalanceKey(b.WarehouseId, b.LocationId, b.ProductId, b.LotId)] = b;
        return result;
    }

    public async Task<IReadOnlyList<InventoryBalance>> LockBalancesByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (ids.Count == 0) return [];
        DetachUnchangedBalances();
        return await db.InventoryBalances
            .FromSqlRaw("SELECT * FROM inventory_balances WHERE id = ANY(@ids) ORDER BY id FOR UPDATE", UuidArray("ids", ids.Cast<Guid?>().ToArray()))
            .AsTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductLot> GetOrCreateLotAsync(Guid productId, string lotNo, DateOnly? expiryDate, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var normalized = ProductLot.NormalizeLotNo(lotNo);
        var tracked = db.ChangeTracker.Entries<ProductLot>().Select(e => e.Entity).FirstOrDefault(l => l.ProductId == productId && l.LotNo == normalized);
        if (tracked is not null) return tracked;

        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO product_lots (id, product_id, lot_no, expiry_date, status, created_at, created_by, updated_at, updated_by)
             VALUES ({Guid.CreateVersion7()}, {productId}, {normalized}, {expiryDate}, {nameof(LotStatus.Released)}, {now}, {user.IdOrNull}, {now}, {user.IdOrNull})
             ON CONFLICT (product_id, lot_no) DO NOTHING
             """,
            cancellationToken);
        return await db.ProductLots.AsTracking().SingleAsync(l => l.ProductId == productId && l.LotNo == normalized, cancellationToken);
    }

    public async Task LockRowAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!LockableTables.Contains(table)) throw new ArgumentException($"Table {table} cannot be locked.", nameof(table));
#pragma warning disable EF1002 // The table name comes from the whitelist above; the id is a parameter.
        await db.Database.ExecuteSqlRawAsync($"SELECT 1 FROM {table} WHERE id = @id FOR UPDATE", [new NpgsqlParameter("id", id)], cancellationToken);
#pragma warning restore EF1002
    }

    public async Task<string> NextNumberAsync(string prefix, Guid warehouseId, string warehouseCode, int year, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var next = await db.Database.SqlQuery<long>(
            $"""
             INSERT INTO document_sequences (prefix, warehouse_id, year, last_value) VALUES ({prefix}, {warehouseId}, {year}, 1)
             ON CONFLICT (prefix, warehouse_id, year) DO UPDATE SET last_value = document_sequences.last_value + 1
             RETURNING last_value AS "Value"
             """).ToListAsync(cancellationToken);
        return $"{prefix}-{warehouseCode}-{year}-{next.Single():000000}";
    }

    /// <summary>
    /// A balance already tracked by this context would be returned with its old values by a query, so unchanged
    /// ones are detached first; modified ones are kept (this transaction already holds their lock).
    /// </summary>
    private void DetachUnchangedBalances()
    {
        foreach (var entry in db.ChangeTracker.Entries<InventoryBalance>().Where(e => e.State == EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("This operation must run inside a database transaction.");
    }

    private static NpgsqlParameter UuidArray(string name, Guid?[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = values };
}
