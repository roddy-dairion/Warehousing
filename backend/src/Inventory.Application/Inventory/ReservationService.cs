using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Application.Inventory;

/// <summary>One document line that needs stock (quantity in base units).</summary>
public sealed record ReservationRequest(Guid LineId, Guid ProductId, decimal Quantity);

/// <summary>A line that could not be fully reserved.</summary>
public sealed record ReservationShortage(Guid LineId, Guid ProductId, string Sku, decimal Requested, decimal Available);

/// <summary>Quantity to take from one reservation when goods leave the building.</summary>
public sealed record ReservationConsumption(Guid ReservationId, decimal Quantity);

/// <summary>Thrown when stock cannot be reserved for every line; nothing is reserved.</summary>
public sealed class StockShortageException(IReadOnlyList<ReservationShortage> shortages)
    : DomainRuleException("stock.shortage",
        "Not enough available stock: " + string.Join("; ", shortages.Select(s => $"{s.Sku} needs {s.Requested:0.###}, {s.Available:0.###} available")) + ".",
        isConflict: true)
{
    public IReadOnlyList<ReservationShortage> Shortages { get; } = shortages;
}

/// <summary>
/// Reserves stock for document lines (dispatches in Phase 5), releases it and consumes it at shipment.
/// Callers own the transaction and must have locked their document row first.
/// </summary>
/// <remarks>
/// Allocation is FEFO: earliest expiry first, then the oldest lot, then the oldest balance. Only stock that can be
/// dispatched is eligible: active pickable storage bins, lots that are released and do not expire before the
/// dispatch date plus the minimum shelf life.
/// </remarks>
public sealed class ReservationService(IApplicationDbContext db, IStockStore store, StockLedger ledger, IOptions<InventoryOptions> options)
{
    /// <summary>
    /// Reserves every line or none (throws <see cref="StockShortageException"/>). Idempotent per document: when the
    /// document already holds open or consumed reservations they are returned unchanged and nothing new is reserved.
    /// </summary>
    public async Task<IReadOnlyList<StockReservation>> ReserveAsync(
        string sourceType, Guid sourceId, Guid warehouseId, IReadOnlyList<ReservationRequest> lines, DateOnly dispatchDate, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var existing = await db.StockReservations
            .Where(r => r.SourceType == sourceType && r.SourceId == sourceId && r.Status != ReservationStatus.Released)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0) return existing;

        if (lines.Count == 0) return [];
        foreach (var line in lines)
        {
            if (line.Quantity <= 0) throw new DomainRuleException("reservation.quantity", "Reserved quantity must be greater than zero.");
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var candidateIds = await EligibleBalancesQuery(warehouseId, productIds, dispatchDate).Select(c => c.Id).ToListAsync(cancellationToken);

        // Lock, then allocate against the locked (current) values in FEFO order.
        var locked = (await store.LockBalancesByIdAsync(candidateIds, cancellationToken)).ToDictionary(b => b.Id);
        var free = candidateIds.Where(locked.ContainsKey).ToDictionary(id => id, id => locked[id].Available);

        var plan = new List<(ReservationRequest Line, InventoryBalance Balance, decimal Quantity)>();
        var shortages = new List<ReservationShortage>();
        foreach (var line in lines)
        {
            var remaining = line.Quantity;
            foreach (var id in candidateIds)
            {
                if (remaining == 0) break;
                if (!locked.TryGetValue(id, out var balance) || balance.ProductId != line.ProductId) continue;
                var take = Math.Min(remaining, free[id]);
                if (take <= 0) continue;
                plan.Add((line, balance, take));
                free[id] -= take;
                remaining -= take;
            }
            if (remaining > 0)
            {
                var sku = await db.Products.Where(p => p.Id == line.ProductId).Select(p => p.Sku).SingleAsync(cancellationToken);
                shortages.Add(new ReservationShortage(line.LineId, line.ProductId, sku, line.Quantity, line.Quantity - remaining));
            }
        }
        if (shortages.Count > 0) throw new StockShortageException(shortages);

        await ledger.ApplyAsync(plan.Select(p => new StockChange(KeyOf(p.Balance), 0, p.Quantity)).ToList(), cancellationToken);
        var reservations = plan.Select(p => StockReservation.Create(p.Balance, sourceType, sourceId, p.Line.LineId, p.Quantity)).ToList();
        db.StockReservations.AddRange(reservations);
        return reservations;
    }

    /// <summary>Total eligible (reservable) quantity per product, for availability checks before approval.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> EligibleAvailableAsync(Guid warehouseId, IReadOnlyCollection<Guid> productIds, DateOnly dispatchDate, CancellationToken cancellationToken) =>
        await EligibleBalancesQuery(warehouseId, productIds, dispatchDate)
            .GroupBy(c => c.ProductId)
            .Select(g => new { g.Key, Sum = g.Sum(c => c.Available) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);

    /// <summary>Releases outstanding reservations of a document (or one line). Returns the quantity released.</summary>
    public async Task<decimal> ReleaseAsync(string sourceType, Guid sourceId, Guid? lineId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var open = await db.StockReservations
            .Where(r => r.SourceType == sourceType && r.SourceId == sourceId && r.Status == ReservationStatus.Open && (lineId == null || r.SourceLineId == lineId))
            .ToListAsync(cancellationToken);
        if (open.Count == 0) return 0;

        var balances = (await store.LockBalancesByIdAsync(open.Select(r => r.BalanceId).Distinct().ToList(), cancellationToken)).ToDictionary(b => b.Id);
        var changes = new List<StockChange>();
        decimal total = 0;
        foreach (var r in open)
        {
            var q = r.Release();
            total += q;
            changes.Add(new StockChange(KeyOf(balances[r.BalanceId]), 0, -q));
        }
        await ledger.ApplyAsync(changes, cancellationToken);
        return total;
    }

    /// <summary>
    /// Takes goods out of stock against their reservations: on hand and reserved both go down, and a DISPATCH movement
    /// is written per reservation (its source line is the reservation id, so a reservation is consumed at most once).
    /// </summary>
    public async Task ConsumeAsync(string sourceType, Guid sourceId, string? sourceNumber, IReadOnlyList<ReservationConsumption> items, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (items.Count == 0) return;
        if (items.Select(i => i.ReservationId).Distinct().Count() != items.Count)
        {
            throw new InvalidOperationException("Each reservation can be consumed once per call.");
        }
        var ids = items.Select(i => i.ReservationId).ToList();
        var reservations = await db.StockReservations.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var balances = (await store.LockBalancesByIdAsync(reservations.Values.Select(r => r.BalanceId).Distinct().ToList(), cancellationToken)).ToDictionary(b => b.Id);

        var changes = new List<StockChange>();
        foreach (var item in items)
        {
            if (!reservations.TryGetValue(item.ReservationId, out var r) || r.SourceType != sourceType || r.SourceId != sourceId)
            {
                throw new DomainRuleException("reservation.unknown", "That reservation does not belong to this document.", isConflict: true);
            }
            r.Consume(item.Quantity);
            changes.Add(new StockChange(KeyOf(balances[r.BalanceId]), -item.Quantity, -item.Quantity, MovementType.Dispatch,
                new StockSource(sourceType, sourceId, r.Id, sourceNumber)));
        }
        await ledger.ApplyAsync(changes, cancellationToken);
    }

    private sealed record Candidate(Guid Id, Guid ProductId, decimal Available);

    /// <summary>Eligible balances in FEFO order.</summary>
    private IQueryable<Candidate> EligibleBalancesQuery(Guid warehouseId, IReadOnlyCollection<Guid> productIds, DateOnly dispatchDate)
    {
        var minExpiry = dispatchDate.AddDays(options.Value.MinShelfLifeDays);
        return from b in db.InventoryBalances.AsNoTracking()
               join loc in db.WarehouseLocations on b.LocationId equals loc.Id
               from lot in db.ProductLots.Where(l => l.Id == b.LotId).DefaultIfEmpty()
               where b.WarehouseId == warehouseId && productIds.Contains(b.ProductId) && b.Available > 0
                     && loc.IsActive && loc.IsPickable && loc.Type == LocationType.Storage
                     && (b.LotId == null || (lot!.Status == LotStatus.Released && (lot.ExpiryDate == null || lot.ExpiryDate >= minExpiry)))
               orderby lot!.ExpiryDate == null, lot.ExpiryDate, lot.CreatedAt, b.Id
               select new Candidate(b.Id, b.ProductId, b.Available);
    }

    private static BalanceKey KeyOf(InventoryBalance b) => new(b.WarehouseId, b.LocationId, b.ProductId, b.LotId);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Reservations must run inside a database transaction.");
    }
}
