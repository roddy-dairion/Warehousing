using Inventory.Application.Common;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Inventory;

/// <summary>
/// Checks the stock invariants that the database cannot enforce row by row:
/// on hand = Σ ledger movements, and reserved = Σ outstanding open reservations, per balance.
/// It never changes data; differences are reported (and logged by the maintenance job).
/// </summary>
public sealed class ReconciliationService(IApplicationDbContext db, IWarehouseAccess access, TimeProvider clock)
{
    /// <summary>Checks one warehouse the current user can access.</summary>
    public async Task<ReconciliationResult> CheckWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(warehouseId, cancellationToken);
        return await CheckAsync(warehouseId, cancellationToken);
    }

    /// <summary>Checks one warehouse, or all when null. No access check: for background jobs and tests.</summary>
    public async Task<ReconciliationResult> CheckAsync(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var balances = db.InventoryBalances.AsNoTracking().Where(b => warehouseId == null || b.WarehouseId == warehouseId);
        var count = await balances.CountAsync(cancellationToken);

        var issues = await (
            from b in balances
            let ledger = db.InventoryMovements.Where(m => m.BalanceId == b.Id).Sum(m => (decimal?)m.Quantity) ?? 0
            let reserved = db.StockReservations.Where(r => r.BalanceId == b.Id && r.Status == ReservationStatus.Open)
                .Sum(r => (decimal?)(r.QuantityReserved - r.QuantityConsumed - r.QuantityReleased)) ?? 0
            where ledger != b.OnHand || reserved != b.Reserved
            join p in db.Products on b.ProductId equals p.Id
            join loc in db.WarehouseLocations on b.LocationId equals loc.Id
            from lot in db.ProductLots.Where(l => l.Id == b.LotId).DefaultIfEmpty()
            orderby p.Sku, loc.Code
            select new ReconciliationIssue(b.Id, b.WarehouseId, p.Sku, loc.Code, lot == null ? null : lot.LotNo, b.OnHand, ledger, b.Reserved, reserved))
            .Take(1000)
            .ToListAsync(cancellationToken);

        return new ReconciliationResult(clock.GetUtcNow(), count, issues);
    }
}
