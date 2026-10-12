using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Purchasing;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Application.Inventory;

/// <summary>Read side of the inventory: balances, summaries, movements and expiry alerts. All queries are warehouse-scoped.</summary>
public sealed class InventoryQueries(IApplicationDbContext db, IWarehouseAccess access, IUserDirectory users, TimeProvider clock, IOptions<InventoryOptions> options)
{
    private static readonly Dictionary<string, Expression<Func<BalanceDto, object?>>> BalanceSorts = new()
    {
        ["sku"] = b => b.Sku,
        ["productName"] = b => b.ProductName,
        ["locationCode"] = b => b.LocationCode,
        ["lotNo"] = b => b.LotNo,
        ["expiryDate"] = b => b.ExpiryDate,
        ["onHand"] = b => b.OnHand,
        ["reserved"] = b => b.Reserved,
        ["available"] = b => b.Available,
        ["lastMovementAt"] = b => b.LastMovementAt,
    };

    private static readonly Dictionary<string, Expression<Func<StockSummaryDto, object?>>> SummarySorts = new()
    {
        ["sku"] = s => s.Sku,
        ["productName"] = s => s.ProductName,
        ["onHand"] = s => s.OnHand,
        ["reserved"] = s => s.Reserved,
        ["available"] = s => s.Available,
        ["sellable"] = s => s.Sellable,
    };

    private static readonly Dictionary<string, Expression<Func<MovementRow, object?>>> MovementSorts = new()
    {
        ["occurredAt"] = m => m.OccurredAt,
        ["sku"] = m => m.Sku,
        ["type"] = m => m.Type,
        ["quantity"] = m => m.Quantity,
    };

    public async Task<PagedResult<BalanceDto>> BalancesAsync(BalanceListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var q = BalanceRows(b => b.WarehouseId == query.WarehouseId);
        if (query.ProductId is { } productId) q = q.Where(b => b.ProductId == productId);
        if (query.LocationId is { } locationId) q = q.Where(b => b.LocationId == locationId);
        if (query.LotId is { } lotId) q = q.Where(b => b.LotId == lotId);
        if (query.OnlyWithStock) q = q.Where(b => b.OnHand > 0);
        if (query.Term is { } term)
        {
            q = q.Where(b => b.Sku.ToLower().Contains(term) || b.ProductName.ToLower().Contains(term) || b.LocationCode.ToLower().Contains(term)
                || (b.LotNo != null && b.LotNo.ToLower().Contains(term)));
        }
        return await q.SortBy(query, BalanceSorts, "sku", b => b.Id).ToPagedAsync(query, cancellationToken);
    }

    public async Task<PagedResult<StockSummaryDto>> SummaryAsync(StockSummaryQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var wh = query.WarehouseId;
        var today = Today();

        var products = db.Products.AsNoTracking().AsQueryable();
        if (query.CategoryId is { } categoryId) products = products.Where(p => p.CategoryId == categoryId);
        if (query.Term is { } term) products = products.Where(p => p.Sku.ToLower().Contains(term) || p.Name.ToLower().Contains(term));

        var rows =
            from p in products
            let onHand = db.InventoryBalances.Where(b => b.WarehouseId == wh && b.ProductId == p.Id).Sum(b => (decimal?)b.OnHand) ?? 0
            let reserved = db.InventoryBalances.Where(b => b.WarehouseId == wh && b.ProductId == p.Id).Sum(b => (decimal?)b.Reserved) ?? 0
            let sellable = (from b in db.InventoryBalances
                            join loc in db.WarehouseLocations on b.LocationId equals loc.Id
                            from lot in db.ProductLots.Where(l => l.Id == b.LotId).DefaultIfEmpty()
                            where b.WarehouseId == wh && b.ProductId == p.Id
                                  && loc.IsActive && loc.IsPickable && loc.Type == LocationType.Storage
                                  && (b.LotId == null || (lot!.Status == LotStatus.Released && (lot.ExpiryDate == null || lot.ExpiryDate >= today)))
                            select (decimal?)b.Available).Sum() ?? 0
            let onOrder = db.PurchaseOrders
                .Where(o => o.WarehouseId == wh && (o.Status == PurchaseOrderStatus.Ordered || o.Status == PurchaseOrderStatus.PartiallyReceived))
                .SelectMany(o => o.Lines).Where(l => l.ProductId == p.Id && l.Quantity > l.ReceivedQuantity)
                .Sum(l => (decimal?)(l.Quantity - l.ReceivedQuantity)) ?? 0
            select new StockSummaryDto
            {
                ProductId = p.Id, Sku = p.Sku, ProductName = p.Name,
                CategoryName = db.ProductCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                BaseUnitCode = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
                IsActive = p.IsActive, OnHand = onHand, Reserved = reserved, Available = onHand - reserved, Sellable = sellable,
                MinStockQty = p.MinStockQty, OnOrder = onOrder, IsLow = p.MinStockQty != null && onHand - reserved < p.MinStockQty,
            };

        rows = query.OnlyWithStock
            ? rows.Where(r => r.OnHand > 0)
            : rows.Where(r => r.OnHand > 0 || r.OnOrder > 0 || (r.MinStockQty != null && r.IsActive));
        if (query.LowStockOnly) rows = rows.Where(r => r.IsLow);
        return await rows.SortBy(query, SummarySorts, "sku", r => r.ProductId).ToPagedAsync(query, cancellationToken);
    }

    /// <summary>Stock of one product in every warehouse the user can access, with the bin/lot breakdown.</summary>
    public async Task<ProductStockDto> ProductStockAsync(Guid productId, Guid? warehouseId, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");
        IReadOnlyCollection<Guid> warehouseIds;
        if (warehouseId is { } wid)
        {
            await access.EnsureCanAccessAsync(wid, cancellationToken);
            warehouseIds = [wid];
        }
        else
        {
            warehouseIds = await access.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var balances = await BalanceRows(b => b.ProductId == productId && warehouseIds.Contains(b.WarehouseId) && b.OnHand > 0)
            .OrderBy(b => b.LocationCode).ThenBy(b => b.ExpiryDate).ThenBy(b => b.LotNo).ToListAsync(cancellationToken);
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).OrderBy(w => w.Code).ToListAsync(cancellationToken);
        var unit = await db.UnitsOfMeasure.Where(u => u.Id == product.BaseUnitId).Select(u => u.Code).SingleAsync(cancellationToken);

        var perWarehouse = warehouses.Select(w =>
        {
            var rows = balances.Where(b => b.WarehouseId == w.Id).ToList();
            return new ProductStockWarehouseDto(w.Id, w.Code, w.Name, rows.Sum(r => r.OnHand), rows.Sum(r => r.Reserved), rows.Sum(r => r.Available), rows);
        }).ToList();

        return new ProductStockDto(product.Id, product.Sku, product.Name, unit, product.IsActive, product.IsLotTracked, product.HasExpiry, product.MinStockQty,
            perWarehouse.Sum(w => w.OnHand), perWarehouse.Sum(w => w.Reserved), perWarehouse.Sum(w => w.Available), perWarehouse);
    }

    public async Task<PagedResult<MovementDto>> MovementsAsync(MovementListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var m = db.InventoryMovements.AsNoTracking().Where(x => x.WarehouseId == query.WarehouseId);
        if (query.ProductId is { } productId) m = m.Where(x => x.ProductId == productId);
        if (query.LocationId is { } locationId) m = m.Where(x => x.LocationId == locationId);
        if (query.LotId is { } lotId) m = m.Where(x => x.LotId == lotId);
        if (query.Type is { } type) m = m.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(query.SourceType)) m = m.Where(x => x.SourceType == query.SourceType);
        if (query.SourceId is { } sourceId) m = m.Where(x => x.SourceId == sourceId);
        if (query.From is { } from) m = m.Where(x => x.OccurredAt >= from);
        if (query.To is { } to) m = m.Where(x => x.OccurredAt < to);

        var rows =
            from x in m
            join p in db.Products on x.ProductId equals p.Id
            join loc in db.WarehouseLocations on x.LocationId equals loc.Id
            from lot in db.ProductLots.Where(l => l.Id == x.LotId).DefaultIfEmpty()
            select new MovementRow
            {
                Id = x.Id, Sequence = x.Sequence, OccurredAt = x.OccurredAt, Type = x.Type, ProductId = x.ProductId, Sku = p.Sku, ProductName = p.Name,
                BaseUnitCode = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
                LocationId = x.LocationId, LocationCode = loc.Code, LotId = x.LotId, LotNo = lot == null ? null : lot.LotNo,
                Quantity = x.Quantity, OnHandAfter = x.OnHandAfter, SourceType = x.SourceType, SourceId = x.SourceId,
                SourceNumber = x.SourceNumber, Reason = x.Reason, CreatedBy = x.CreatedBy,
            };
        if (query.Term is { } term)
        {
            rows = rows.Where(r => r.Sku.ToLower().Contains(term) || r.ProductName.ToLower().Contains(term) || r.LocationCode.ToLower().Contains(term)
                || (r.LotNo != null && r.LotNo.ToLower().Contains(term)) || (r.SourceNumber != null && r.SourceNumber.ToLower().Contains(term)));
        }
        if (query.SortBy is null) { query.SortBy = "occurredAt"; query.SortDir ??= "desc"; }

        // Time order uses the ledger sequence as tie-breaker in the same direction, so movements of one posting keep their sequence.
        var ordered = query.SortBy == "occurredAt" || !MovementSorts.ContainsKey(query.SortBy)
            ? (query.Descending ? rows.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Sequence) : rows.OrderBy(r => r.OccurredAt).ThenBy(r => r.Sequence))
            : rows.SortBy(query, MovementSorts, "occurredAt", r => r.Sequence);
        var page = await ordered.ToPagedAsync(query, cancellationToken);
        var names = await users.NamesAsync(page.Items.Select(r => r.CreatedBy), cancellationToken);
        return new PagedResult<MovementDto>(
            page.Items.Select(r => new MovementDto(r.Id, r.OccurredAt, r.Type, r.ProductId, r.Sku, r.ProductName, r.BaseUnitCode, r.LocationId, r.LocationCode,
                r.LotId, r.LotNo, r.Quantity, r.OnHandAfter, r.SourceType, r.SourceId, r.SourceNumber, r.Reason, names.NameOf(r.CreatedBy))).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>Lots with stock in the warehouse that expire within the window (expired lots included), soonest first.</summary>
    public async Task<IReadOnlyList<ExpiringLotDto>> ExpiringAsync(ExpiringQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var days = Math.Clamp(query.Days ?? options.Value.ExpiryAlertDays, 0, 3650);
        var today = Today();
        var until = today.AddDays(days);

        var rows = await (from b in db.InventoryBalances.AsNoTracking()
                          join lot in db.ProductLots on b.LotId equals lot.Id
                          join p in db.Products on b.ProductId equals p.Id
                          join loc in db.WarehouseLocations on b.LocationId equals loc.Id
                          where b.WarehouseId == query.WarehouseId && b.OnHand > 0 && lot.ExpiryDate != null && lot.ExpiryDate <= until
                          select new
                          {
                              LotId = lot.Id, lot.LotNo, Expiry = lot.ExpiryDate!.Value, lot.Status, p.Id, p.Sku, p.Name,
                              Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
                              b.OnHand, b.Reserved, Bin = loc.Code,
                          }).ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.LotId)
            .Select(g =>
            {
                var f = g.First();
                return new ExpiringLotDto(f.LotId, f.LotNo, f.Expiry, f.Expiry.DayNumber - today.DayNumber, f.Status, f.Id, f.Sku, f.Name, f.Unit,
                    g.Sum(x => x.OnHand), g.Sum(x => x.Reserved), g.Select(x => x.Bin).Distinct().Order().ToList());
            })
            .OrderBy(l => l.ExpiryDate).ThenBy(l => l.Sku).ThenBy(l => l.LotNo)
            .ToList();
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private IQueryable<BalanceDto> BalanceRows(Expression<Func<InventoryBalance, bool>> filter) =>
        from b in db.InventoryBalances.AsNoTracking().Where(filter)
        join p in db.Products on b.ProductId equals p.Id
        join loc in db.WarehouseLocations on b.LocationId equals loc.Id
        from lot in db.ProductLots.Where(l => l.Id == b.LotId).DefaultIfEmpty()
        select new BalanceDto
        {
            Id = b.Id, WarehouseId = b.WarehouseId, ProductId = b.ProductId, Sku = p.Sku, ProductName = p.Name,
            BaseUnitCode = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
            LocationId = b.LocationId, LocationCode = loc.Code, LocationType = loc.Type, IsPickable = loc.IsPickable,
            LotId = b.LotId, LotNo = lot == null ? null : lot.LotNo, ExpiryDate = lot == null ? null : lot.ExpiryDate, LotStatus = lot == null ? null : lot.Status,
            OnHand = b.OnHand, Reserved = b.Reserved, Available = b.Available, LastMovementAt = b.LastMovementAt,
        };

    /// <summary>Flat projection (a class, so EF can sort and filter on it before paging).</summary>
    private sealed class MovementRow
    {
        public Guid Id { get; init; }
        public long Sequence { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public MovementType Type { get; init; }
        public Guid ProductId { get; init; }
        public string Sku { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string BaseUnitCode { get; init; } = "";
        public Guid LocationId { get; init; }
        public string LocationCode { get; init; } = "";
        public Guid? LotId { get; init; }
        public string? LotNo { get; init; }
        public decimal Quantity { get; init; }
        public decimal OnHandAfter { get; init; }
        public string SourceType { get; init; } = "";
        public Guid SourceId { get; init; }
        public string? SourceNumber { get; init; }
        public string? Reason { get; init; }
        public Guid? CreatedBy { get; init; }
    }
}
