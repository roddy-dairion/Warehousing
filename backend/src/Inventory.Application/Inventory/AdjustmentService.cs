using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Application.Inventory;

/// <summary>Stock adjustments: draft → submitted → approved (stock changes) or rejected.</summary>
public sealed class AdjustmentService(
    IApplicationDbContext db,
    IWarehouseAccess access,
    IStockStore store,
    StockLedger ledger,
    StockRules rules,
    IUserDirectory users,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit,
    IOptions<InventoryOptions> options)
{
    private static readonly Dictionary<string, Expression<Func<StockAdjustment, object?>>> Sorts = new()
    {
        ["number"] = a => a.Number,
        ["status"] = a => a.Status,
        ["createdAt"] = a => a.CreatedAt,
        ["submittedAt"] = a => a.SubmittedAt,
    };

    public async Task<PagedResult<AdjustmentListItemDto>> ListAsync(AdjustmentListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var q = db.StockAdjustments.AsNoTracking().Where(a => a.WarehouseId == query.WarehouseId);
        if (query.Status is { } status) q = q.Where(a => a.Status == status);
        if (query.Term is { } term) q = q.Where(a => a.Number.ToLower().Contains(term) || (a.Notes != null && a.Notes.ToLower().Contains(term)));
        if (query.SortBy is null) { query.SortBy = "createdAt"; query.SortDir ??= "desc"; }
        return await q.SortBy(query, Sorts, "createdAt", a => a.Id)
            .Select(a => new AdjustmentListItemDto(a.Id, a.Number, a.WarehouseId, db.Warehouses.Where(w => w.Id == a.WarehouseId).Select(w => w.Code).First(),
                a.Reason, a.Status, a.Lines.Count, a.CreatedAt, a.SubmittedAt, a.DecidedAt))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<AdjustmentDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var a = await db.StockAdjustments.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Adjustment not found.");
        await access.EnsureCanAccessAsync(a.WarehouseId, cancellationToken);
        var version = await db.StockAdjustments.Where(x => x.Id == id).Select(x => EF.Property<uint>(x, Versioning.Column)).SingleAsync(cancellationToken);
        return await ToDtoAsync(a, version, cancellationToken);
    }

    public async Task<AdjustmentDto> CreateAsync(CreateAdjustmentRequest request, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(request.WarehouseId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == request.WarehouseId, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var number = await store.NextNumberAsync("ADJ", warehouse.Id, warehouse.Code, clock.GetUtcNow().Year, cancellationToken);
        var adjustment = StockAdjustment.Create(number, warehouse.Id, request.Reason, request.Notes);
        adjustment.ReplaceLines(await BuildLinesAsync(warehouse.Id, request.Lines ?? [], cancellationToken));
        db.StockAdjustments.Add(adjustment);
        audit.Add("adjustment.created", "adjustment", adjustment.Id.ToString(), warehouse.Id, new { adjustment.Number, adjustment.Reason });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(adjustment.Id, cancellationToken);
    }

    public async Task<AdjustmentDto> UpdateAsync(Guid id, UpdateAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var adjustment = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(adjustment, request.Version);
        adjustment.SetHeader(request.Reason, request.Notes);
        adjustment.ReplaceLines(await BuildLinesAsync(adjustment.WarehouseId, request.Lines, cancellationToken));
        audit.Add("adjustment.updated", "adjustment", id.ToString(), adjustment.WarehouseId);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<AdjustmentDto> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, "adjustment.submitted", a => a.Submit(user.IdOrNull, clock.GetUtcNow()), cancellationToken);

    public Task<AdjustmentDto> RejectAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        TransitionAsync(id, "adjustment.rejected", a => a.Reject(user.IdOrNull, clock.GetUtcNow(), reason), cancellationToken);

    public Task<AdjustmentDto> ReopenAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, "adjustment.reopened", a => a.Reopen(), cancellationToken);

    public Task<AdjustmentDto> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, "adjustment.cancelled", a => a.Cancel(), cancellationToken);

    /// <summary>Approves and posts in one transaction: lines are re-validated against current stock.</summary>
    public async Task<AdjustmentDto> ApproveAsync(Guid id, string? note, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("stock_adjustments", id, cancellationToken);
        var adjustment = await LoadAsync(id, cancellationToken);
        adjustment.Approve(user.IdOrNull, clock.GetUtcNow(), options.Value.RequireDifferentApprover, note);

        var changes = new List<StockChange>();
        foreach (var line in adjustment.Lines)
        {
            try
            {
                var sp = await rules.ProductAsync(line.ProductId, cancellationToken, requireActive: false);
                await rules.LocationAsync(adjustment.WarehouseId, line.LocationId, cancellationToken);
                Guid? lotId = null;
                if (sp.Product.IsLotTracked)
                {
                    if (line.QuantityDelta > 0)
                    {
                        var lot = await store.GetOrCreateLotAsync(sp.Product.Id, line.LotNo!, line.ExpiryDate, cancellationToken);
                        lot.ConfirmExpiry(line.ExpiryDate);
                        if (sp.Product.HasExpiry && lot.ExpiryDate is null)
                        {
                            throw new DomainRuleException("lot.expiry_required", $"Enter the expiry date for new lot {lot.LotNo}.");
                        }
                        lotId = lot.Id;
                    }
                    else
                    {
                        lotId = await rules.ExistingLotIdAsync(sp, line.LotNo, cancellationToken);
                    }
                }
                changes.Add(new StockChange(new BalanceKey(adjustment.WarehouseId, line.LocationId, line.ProductId, lotId), line.QuantityDelta, 0,
                    line.QuantityDelta > 0 ? MovementType.AdjustmentIn : MovementType.AdjustmentOut,
                    new StockSource(SourceTypes.Adjustment, adjustment.Id, line.Id, adjustment.Number, adjustment.Reason.ToString())));
            }
            catch (DomainRuleException e)
            {
                throw new DomainRuleException(e.Code, $"Line {line.LineNo}: {e.Message}", e.IsConflict);
            }
        }
        await ledger.ApplyAsync(changes, cancellationToken);
        audit.Add("adjustment.approved", "adjustment", id.ToString(), adjustment.WarehouseId, new { adjustment.Number, adjustment.Reason, lines = adjustment.Lines.Count, note });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<AdjustmentDto> TransitionAsync(Guid id, string action, Action<StockAdjustment> change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("stock_adjustments", id, cancellationToken);
        var adjustment = await LoadAsync(id, cancellationToken);
        change(adjustment);
        audit.Add(action, "adjustment", id.ToString(), adjustment.WarehouseId, new { adjustment.Number });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<List<StockAdjustmentLine>> BuildLinesAsync(Guid warehouseId, IReadOnlyList<AdjustmentLineRequest> requests, CancellationToken cancellationToken)
    {
        var lines = new List<StockAdjustmentLine>();
        var lineNo = 0;
        foreach (var r in requests)
        {
            lineNo++;
            try
            {
                var sp = await rules.ProductAsync(r.ProductId, cancellationToken, requireActive: false);
                await rules.LocationAsync(warehouseId, r.LocationId, cancellationToken);
                Quantities.Precision(r.QuantityDelta, sp.BaseUnitDecimals, sp.BaseUnitCode);
                StockRules.CheckLot(sp, r.LotNo, r.ExpiryDate, requireExpiry: false);
                if (r.QuantityDelta < 0 && r.ExpiryDate is not null)
                {
                    throw new DomainRuleException("adjustment.expiry_on_removal", "Expiry dates are only entered when adding stock to a new lot.");
                }
                lines.Add(StockAdjustmentLine.Create(sp.Product.Id, r.LocationId, r.LotNo, r.ExpiryDate, r.QuantityDelta, r.Notes));
            }
            catch (DomainRuleException e)
            {
                throw new DomainRuleException(e.Code, $"Line {lineNo}: {e.Message}", e.IsConflict);
            }
        }
        return lines;
    }

    private async Task<StockAdjustment> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var adjustment = await db.StockAdjustments.Include(a => a.Lines).SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Adjustment not found.");
        await access.EnsureCanAccessAsync(adjustment.WarehouseId, cancellationToken);
        return adjustment;
    }

    private async Task<AdjustmentDto> ToDtoAsync(StockAdjustment a, uint version, CancellationToken cancellationToken)
    {
        var productIds = a.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First() })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var locationIds = a.Lines.Select(l => l.LocationId).Distinct().ToList();
        var locations = await db.WarehouseLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);
        var warehouseCode = await db.Warehouses.Where(w => w.Id == a.WarehouseId).Select(w => w.Code).SingleAsync(cancellationToken);
        var names = await users.NamesAsync([a.CreatedBy, a.SubmittedBy, a.DecidedBy], cancellationToken);

        // Current stock helps the approver judge the change (only meaningful before posting).
        var onHand = new Dictionary<(Guid, Guid, string?), decimal>();
        if (a.Status is AdjustmentStatus.Draft or AdjustmentStatus.PendingApproval)
        {
            var balances = await (from b in db.InventoryBalances.AsNoTracking()
                                  where b.WarehouseId == a.WarehouseId && productIds.Contains(b.ProductId) && locationIds.Contains(b.LocationId)
                                  select new { b.ProductId, b.LocationId, LotNo = db.ProductLots.Where(l => l.Id == b.LotId).Select(l => l.LotNo).FirstOrDefault(), b.OnHand })
                                 .ToListAsync(cancellationToken);
            foreach (var b in balances) onHand[(b.ProductId, b.LocationId, b.LotNo)] = b.OnHand;
        }

        var lines = a.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            decimal? current = a.Status is AdjustmentStatus.Draft or AdjustmentStatus.PendingApproval ? onHand.GetValueOrDefault((l.ProductId, l.LocationId, l.LotNo)) : null;
            return new AdjustmentLineDto(l.Id, l.LineNo, l.ProductId, p.Sku, p.Name, p.Unit, l.LocationId, locations[l.LocationId], l.LotNo, l.ExpiryDate, l.QuantityDelta, current, l.Notes);
        }).ToList();

        var canDecide = a.Status == AdjustmentStatus.PendingApproval && user.HasPermission(Permissions.InventoryAdjustApprove)
            && (!options.Value.RequireDifferentApprover || a.SubmittedBy != user.IdOrNull);

        return new AdjustmentDto(a.Id, a.Number, a.WarehouseId, warehouseCode, a.Reason, a.Notes, a.Status, lines,
            a.CreatedAt, names.NameOf(a.CreatedBy), a.SubmittedAt, names.NameOf(a.SubmittedBy), a.DecidedAt, names.NameOf(a.DecidedBy), a.DecisionNote,
            canDecide, version);
    }
}
