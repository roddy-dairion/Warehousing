using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Inventory;

/// <summary>Bin moves: source decreases and destination increases in one transaction. Reserved stock cannot be moved.</summary>
public sealed class TransferService(
    IApplicationDbContext db,
    IWarehouseAccess access,
    IStockStore store,
    StockLedger ledger,
    StockRules rules,
    IUserDirectory users,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit)
{
    public async Task<PagedResult<TransferListItemDto>> ListAsync(TransferListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var q = db.StockTransfers.AsNoTracking().Where(t => t.FromWarehouseId == query.WarehouseId || t.ToWarehouseId == query.WarehouseId);
        if (query.Term is { } term) q = q.Where(t => t.Number.ToLower().Contains(term) || (t.Notes != null && t.Notes.ToLower().Contains(term)));
        return await q.OrderByDescending(t => t.PostedAt).ThenBy(t => t.Id)
            .Select(t => new TransferListItemDto(t.Id, t.Number, t.Type, db.Warehouses.Where(w => w.Id == t.FromWarehouseId).Select(w => w.Code).First(), t.Lines.Count, t.PostedAt, t.Notes))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<TransferDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var t = await db.StockTransfers.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Move not found.");
        await access.EnsureCanAccessAsync(t.FromWarehouseId, cancellationToken);

        var productIds = t.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First() }).ToDictionaryAsync(p => p.Id, cancellationToken);
        var locationIds = t.Lines.SelectMany(l => new[] { l.FromLocationId, l.ToLocationId }).Distinct().ToList();
        var locations = await db.WarehouseLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);
        var lotIds = t.Lines.Where(l => l.LotId != null).Select(l => l.LotId!.Value).Distinct().ToList();
        var lots = await db.ProductLots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.LotNo, cancellationToken);
        var names = await users.NamesAsync([t.PostedBy], cancellationToken);
        var code = await db.Warehouses.Where(w => w.Id == t.FromWarehouseId).Select(w => w.Code).SingleAsync(cancellationToken);

        return new TransferDto(t.Id, t.Number, t.Type, t.FromWarehouseId, code, t.Notes, t.PostedAt, names.NameOf(t.PostedBy),
            t.Lines.OrderBy(l => l.LineNo).Select(l => new TransferLineDto(l.LineNo, l.ProductId, products[l.ProductId].Sku, products[l.ProductId].Name,
                l.LotId is null ? null : lots[l.LotId.Value], l.FromLocationId, locations[l.FromLocationId], l.ToLocationId, locations[l.ToLocationId], l.Quantity, products[l.ProductId].Unit)).ToList());
    }

    public async Task<TransferDto> CreateBinMoveAsync(CreateBinMoveRequest request, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(request.WarehouseId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == request.WarehouseId, cancellationToken);
        if (request.Lines.Count == 0) throw new DomainRuleException("transfer.empty", "Add at least one line.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var lines = new List<StockTransferLine>();
        var lineNo = 0;
        foreach (var r in request.Lines)
        {
            lineNo++;
            try
            {
                var sp = await rules.ProductAsync(r.ProductId, cancellationToken, requireActive: false);
                await rules.LocationAsync(warehouse.Id, r.FromLocationId, cancellationToken);
                await rules.LocationAsync(warehouse.Id, r.ToLocationId, cancellationToken);
                StockRules.CheckLot(sp, r.LotNo, null, requireExpiry: false);
                var lotId = await rules.ExistingLotIdAsync(sp, r.LotNo, cancellationToken);
                lines.Add(StockTransferLine.Create(sp.Product.Id, lotId, r.FromLocationId, r.ToLocationId, StockRules.ToBase(sp, r.Quantity, r.ProductUnitId)));
            }
            catch (DomainRuleException e)
            {
                throw new DomainRuleException(e.Code, $"Line {lineNo}: {e.Message}", e.IsConflict);
            }
        }

        var number = await store.NextNumberAsync("MOV", warehouse.Id, warehouse.Code, clock.GetUtcNow().Year, cancellationToken);
        var transfer = StockTransfer.CreateBinMove(number, warehouse.Id, request.Notes, lines, user.IdOrNull, clock.GetUtcNow());
        db.StockTransfers.Add(transfer);

        var changes = new List<StockChange>();
        foreach (var l in transfer.Lines)
        {
            var source = new StockSource(SourceTypes.BinMove, transfer.Id, l.Id, transfer.Number);
            changes.Add(new StockChange(new BalanceKey(warehouse.Id, l.FromLocationId, l.ProductId, l.LotId), -l.Quantity, 0, MovementType.MoveOut, source));
            changes.Add(new StockChange(new BalanceKey(warehouse.Id, l.ToLocationId, l.ProductId, l.LotId), l.Quantity, 0, MovementType.MoveIn, source));
        }
        await ledger.ApplyAsync(changes, cancellationToken);
        audit.Add("transfer.bin_move", "transfer", transfer.Id.ToString(), warehouse.Id, new { transfer.Number, lines = transfer.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(transfer.Id, cancellationToken);
    }
}
