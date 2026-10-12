using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Inventory;

/// <summary>One change to a balance; on-hand changes must carry a movement type and source.</summary>
public sealed record StockChange(BalanceKey Key, decimal OnHandDelta, decimal ReservedDelta, MovementType? Movement = null, StockSource? Source = null);

/// <summary>
/// The single choke point for stock changes. It locks the affected balances in a fixed order,
/// applies the changes (enforcing on hand ≥ 0 and 0 ≤ reserved ≤ on hand) and appends the ledger
/// movements. Callers own the transaction and commit it together with their document change.
/// </summary>
public sealed class StockLedger(IApplicationDbContext db, IStockStore store, ICurrentUser user, TimeProvider clock)
{
    public async Task<IReadOnlyDictionary<BalanceKey, InventoryBalance>> ApplyAsync(IReadOnlyList<StockChange> changes, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Stock changes must run inside a database transaction.");
        }
        foreach (var c in changes)
        {
            if (c.OnHandDelta != 0 && (c.Movement is null || c.Source is null))
            {
                throw new InvalidOperationException("On-hand changes require a movement type and source.");
            }
        }

        var keys = changes.Select(c => c.Key).Distinct().ToList();
        var anyIncrease = changes.Any(c => c.OnHandDelta > 0);
        var balances = await store.LockBalancesAsync(keys, createMissing: anyIncrease, cancellationToken);
        var now = clock.GetUtcNow();
        var ordinal = 0;

        foreach (var change in changes)
        {
            if (!balances.TryGetValue(change.Key, out var balance))
            {
                throw await DescribeAsync(change.Key, new DomainRuleException("stock.insufficient", "There is no stock here.", isConflict: true), cancellationToken);
            }
            try
            {
                balance.Apply(change.OnHandDelta, change.ReservedDelta, now);
            }
            catch (DomainRuleException e)
            {
                throw await DescribeAsync(change.Key, e, cancellationToken);
            }
            if (change.OnHandDelta != 0)
            {
                // Movements of one call are 1 µs apart so the ledger keeps their order (the database stores microseconds).
                var at = now.AddTicks(TimeSpan.TicksPerMicrosecond * ordinal++);
                db.InventoryMovements.Add(InventoryMovement.Create(balance, change.Movement!.Value, change.OnHandDelta, change.Source!, at, user.IdOrNull));
            }
        }
        return balances;
    }

    /// <summary>Adds product, bin and lot to a stock error so the user knows which line failed.</summary>
    private async Task<DomainRuleException> DescribeAsync(BalanceKey key, DomainRuleException error, CancellationToken cancellationToken)
    {
        var sku = await db.Products.Where(p => p.Id == key.ProductId).Select(p => p.Sku).FirstOrDefaultAsync(cancellationToken);
        var bin = await db.WarehouseLocations.Where(l => l.Id == key.LocationId).Select(l => l.Code).FirstOrDefaultAsync(cancellationToken);
        var lot = key.LotId is null ? null : await db.ProductLots.Where(l => l.Id == key.LotId).Select(l => l.LotNo).FirstOrDefaultAsync(cancellationToken);
        var where = lot is null ? $"{sku} in bin {bin}" : $"{sku} lot {lot} in bin {bin}";
        return new DomainRuleException(error.Code, $"{where}: {error.Message}", error.IsConflict);
    }
}
