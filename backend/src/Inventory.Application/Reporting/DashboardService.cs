using Inventory.Application.Common;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;
using Inventory.Domain.Inventory;
using Inventory.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Application.Reporting;

public sealed record DashboardMovementDto(DateTimeOffset OccurredAt, MovementType Type, string Sku, string ProductName, string LocationCode, string? LotNo, decimal Quantity, string? SourceNumber);

public sealed record DashboardDispatchDto(Guid Id, string Number, string CustomerName, DateOnly RequestedDate, DispatchStatus Status);

/// <summary>Counts for the warehouse dashboard. Sections the user has no permission for are null.</summary>
public sealed record WarehouseDashboardDto(
    Guid WarehouseId,
    StockFiguresDto? Stock,
    int? PendingReceipts,
    int? AdjustmentsAwaitingApproval,
    DispatchFiguresDto? Dispatches,
    IReadOnlyList<DashboardDispatchDto>? DispatchesToAction,
    IReadOnlyList<DashboardMovementDto>? RecentMovements,
    int? FailedDocuments,
    int? FailedEmails);

public sealed record StockFiguresDto(int ProductsInStock, decimal OnHand, decimal Reserved, decimal Available, int LowStockProducts, int ExpiringLots, int ExpiredLots);

public sealed record DispatchFiguresDto(int Draft, int AwaitingApproval, int ToPick, int Picking, int ReadyToShip, int AwaitingDelivery, int DeliveredToday);

/// <summary>Warehouse dashboard figures (each section requires the matching read permission).</summary>
public sealed class DashboardService(IApplicationDbContext db, IWarehouseAccess access, ICurrentUser user, TimeProvider clock, IOptions<Inventory.InventoryOptions> inventoryOptions)
{
    public async Task<WarehouseDashboardDto> WarehouseAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(warehouseId, cancellationToken);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        StockFiguresDto? stock = null;
        IReadOnlyList<DashboardMovementDto>? movements = null;
        if (user.HasPermission(Permissions.InventoryRead))
        {
            var balances = db.InventoryBalances.AsNoTracking().Where(b => b.WarehouseId == warehouseId);
            var totals = await balances.GroupBy(_ => 1).Select(g => new { OnHand = g.Sum(b => b.OnHand), Reserved = g.Sum(b => b.Reserved) }).FirstOrDefaultAsync(cancellationToken);
            var inStock = await balances.Where(b => b.OnHand > 0).Select(b => b.ProductId).Distinct().CountAsync(cancellationToken);
            var lowStock = await db.Products.AsNoTracking()
                .Where(p => p.IsActive && p.MinStockQty != null)
                .CountAsync(p => (db.InventoryBalances.Where(b => b.WarehouseId == warehouseId && b.ProductId == p.Id).Sum(b => (decimal?)b.Available) ?? 0) < p.MinStockQty, cancellationToken);
            var until = today.AddDays(inventoryOptions.Value.ExpiryAlertDays);
            var lotsWithStock = from b in balances
                                where b.OnHand > 0
                                join lot in db.ProductLots on b.LotId equals lot.Id
                                where lot.ExpiryDate != null && lot.ExpiryDate <= until
                                select new { lot.Id, lot.ExpiryDate };
            var expiring = await lotsWithStock.Where(l => l.ExpiryDate >= today).Select(l => l.Id).Distinct().CountAsync(cancellationToken);
            var expired = await lotsWithStock.Where(l => l.ExpiryDate < today).Select(l => l.Id).Distinct().CountAsync(cancellationToken);
            stock = new StockFiguresDto(inStock, totals?.OnHand ?? 0, totals?.Reserved ?? 0, (totals?.OnHand ?? 0) - (totals?.Reserved ?? 0), lowStock, expiring, expired);

            movements = await (from m in db.InventoryMovements.AsNoTracking()
                               where m.WarehouseId == warehouseId
                               orderby m.OccurredAt descending, m.Sequence descending
                               join p in db.Products on m.ProductId equals p.Id
                               join loc in db.WarehouseLocations on m.LocationId equals loc.Id
                               from lot in db.ProductLots.Where(l => l.Id == m.LotId).DefaultIfEmpty()
                               select new DashboardMovementDto(m.OccurredAt, m.Type, p.Sku, p.Name, loc.Code, lot == null ? null : lot.LotNo, m.Quantity, m.SourceNumber))
                .Take(10).ToListAsync(cancellationToken);
        }

        int? pendingReceipts = user.HasPermission(Permissions.InventoryReceive) || user.HasPermission(Permissions.InventoryReceivePost)
            ? await db.GoodsReceipts.CountAsync(r => r.WarehouseId == warehouseId && r.Status == ReceiptStatus.Draft, cancellationToken)
            : null;
        int? pendingAdjustments = user.HasPermission(Permissions.InventoryAdjust) || user.HasPermission(Permissions.InventoryAdjustApprove)
            ? await db.StockAdjustments.CountAsync(a => a.WarehouseId == warehouseId && a.Status == AdjustmentStatus.PendingApproval, cancellationToken)
            : null;

        DispatchFiguresDto? dispatchFigures = null;
        IReadOnlyList<DashboardDispatchDto>? toAction = null;
        int? failedDocs = null, failedEmails = null;
        if (user.HasPermission(Permissions.DispatchesRead))
        {
            var counts = await db.Dispatches.AsNoTracking().Where(d => d.WarehouseId == warehouseId)
                .GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
            var startOfDay = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var deliveredToday = await db.Dispatches.CountAsync(d => d.WarehouseId == warehouseId && d.DeliveredAt >= startOfDay, cancellationToken);
            dispatchFigures = new DispatchFiguresDto(counts.GetValueOrDefault(DispatchStatus.Draft), counts.GetValueOrDefault(DispatchStatus.Submitted),
                counts.GetValueOrDefault(DispatchStatus.Approved), counts.GetValueOrDefault(DispatchStatus.Picking), counts.GetValueOrDefault(DispatchStatus.ReadyForDispatch),
                counts.GetValueOrDefault(DispatchStatus.Dispatched), deliveredToday);

            // What this user can move forward: approvals for approvers, picking for pickers, shipping for shippers.
            var statuses = new List<DispatchStatus>();
            if (user.HasPermission(Permissions.DispatchesApprove)) statuses.Add(DispatchStatus.Submitted);
            if (user.HasPermission(Permissions.DispatchesPick)) statuses.AddRange([DispatchStatus.Approved, DispatchStatus.Picking]);
            if (user.HasPermission(Permissions.DispatchesShip)) statuses.Add(DispatchStatus.ReadyForDispatch);
            if (user.HasPermission(Permissions.DispatchesDeliver)) statuses.Add(DispatchStatus.Dispatched);
            toAction = await db.Dispatches.AsNoTracking()
                .Where(d => d.WarehouseId == warehouseId && statuses.Contains(d.Status))
                .OrderBy(d => d.RequestedDate).ThenBy(d => d.CreatedAt)
                .Select(d => new DashboardDispatchDto(d.Id, d.Number, db.Customers.Where(c => c.Id == d.CustomerId).Select(c => c.CompanyName).First(), d.RequestedDate, d.Status))
                .Take(10).ToListAsync(cancellationToken);

            failedDocs = await db.DispatchDocuments.CountAsync(x => x.Status == DocumentStatus.Failed && db.Dispatches.Any(d => d.Id == x.DispatchId && d.WarehouseId == warehouseId), cancellationToken);
            failedEmails = await db.EmailMessages.CountAsync(m => m.Status == EmailStatus.Failed && m.RelatedType == "dispatch"
                && db.Dispatches.Any(d => d.Id == m.RelatedId && d.WarehouseId == warehouseId), cancellationToken);
        }

        return new WarehouseDashboardDto(warehouseId, stock, pendingReceipts, pendingAdjustments, dispatchFigures, toAction, movements, failedDocs, failedEmails);
    }
}
