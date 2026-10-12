using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Inventory;

/// <summary>Goods receipts: draft → posted (adds stock once) → reversed (removes it again, auditable).</summary>
public sealed class ReceiptService(
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
    private static readonly Dictionary<string, Expression<Func<GoodsReceipt, object?>>> Sorts = new()
    {
        ["number"] = r => r.Number,
        ["receiptDate"] = r => r.ReceiptDate,
        ["status"] = r => r.Status,
        ["createdAt"] = r => r.CreatedAt,
    };

    public async Task<PagedResult<ReceiptListItemDto>> ListAsync(ReceiptListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var q = db.GoodsReceipts.AsNoTracking().Where(r => r.WarehouseId == query.WarehouseId);
        if (query.Status is { } status) q = q.Where(r => r.Status == status);
        if (query.From is { } from) q = q.Where(r => r.ReceiptDate >= from);
        if (query.To is { } to) q = q.Where(r => r.ReceiptDate <= to);
        if (query.Term is { } term)
        {
            q = q.Where(r => r.Number.ToLower().Contains(term) || (r.SupplierName != null && r.SupplierName.ToLower().Contains(term))
                || (r.SupplierReference != null && r.SupplierReference.ToLower().Contains(term)));
        }
        if (query.SortBy is null) { query.SortBy = "createdAt"; query.SortDir ??= "desc"; }
        return await q.SortBy(query, Sorts, "createdAt", r => r.Id)
            .Select(r => new ReceiptListItemDto(r.Id, r.Number, r.WarehouseId, db.Warehouses.Where(w => w.Id == r.WarehouseId).Select(w => w.Code).First(),
                r.ReceiptDate, r.SupplierName, r.SupplierReference, r.Status, r.Lines.Count, r.CreatedAt, r.PostedAt))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<ReceiptDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var r = await db.GoodsReceipts.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Receipt not found.");
        await access.EnsureCanAccessAsync(r.WarehouseId, cancellationToken);
        var version = await db.GoodsReceipts.Where(x => x.Id == id).Select(x => EF.Property<uint>(x, Versioning.Column)).SingleAsync(cancellationToken);
        return await ToDtoAsync(r, version, cancellationToken);
    }

    public async Task<ReceiptDto> CreateAsync(CreateReceiptRequest request, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(request.WarehouseId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == request.WarehouseId, cancellationToken);
        var date = request.ReceiptDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var number = await store.NextNumberAsync("GR", warehouse.Id, warehouse.Code, date.Year, cancellationToken);
        var receipt = GoodsReceipt.Create(number, warehouse.Id, date);
        receipt.SetHeader(date, request.SupplierName, request.SupplierReference, request.Notes);
        receipt.ReplaceLines(await BuildLinesAsync(warehouse.Id, request.Lines ?? [], cancellationToken));
        db.GoodsReceipts.Add(receipt);
        audit.Add("receipt.created", "receipt", receipt.Id.ToString(), warehouse.Id, new { receipt.Number, lines = receipt.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(receipt.Id, cancellationToken);
    }

    public async Task<ReceiptDto> UpdateAsync(Guid id, UpdateReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(receipt, request.Version);
        receipt.SetHeader(request.ReceiptDate, request.SupplierName, request.SupplierReference, request.Notes);
        receipt.ReplaceLines(await BuildLinesAsync(receipt.WarehouseId, request.Lines, cancellationToken));
        audit.Add("receipt.updated", "receipt", id.ToString(), receipt.WarehouseId, new { lines = receipt.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Adds the received quantities to stock. The receipt row is locked and its status re-checked,
    /// so a repeated or concurrent post cannot add stock twice (and the ledger's unique key backs this up).
    /// </summary>
    public async Task<ReceiptDto> PostAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("goods_receipts", id, cancellationToken);
        var receipt = await LoadAsync(id, cancellationToken);
        if (receipt.Status == ReceiptStatus.Posted)
        {
            throw new DomainRuleException("receipt.already_posted", $"Receipt {receipt.Number} is already posted.", isConflict: true);
        }

        var changes = new List<StockChange>();
        foreach (var line in receipt.Lines)
        {
            var sp = await rules.ProductAsync(line.ProductId, cancellationToken);
            await rules.LocationAsync(receipt.WarehouseId, line.LocationId, cancellationToken);
            StockRules.CheckLot(sp, line.LotNo, line.ExpiryDate, requireExpiry: false);
            Guid? lotId = null;
            if (sp.Product.IsLotTracked)
            {
                var lot = await store.GetOrCreateLotAsync(sp.Product.Id, line.LotNo!, line.ExpiryDate, cancellationToken);
                lot.ConfirmExpiry(line.ExpiryDate);
                if (sp.Product.HasExpiry && lot.ExpiryDate is null)
                {
                    throw new DomainRuleException("lot.expiry_required", $"Line {line.LineNo}: {sp.Product.Sku} has expiry dates: enter the expiry date for lot {lot.LotNo}.");
                }
                lotId = lot.Id;
                line.SetLot(lot.Id);
            }
            changes.Add(new StockChange(new BalanceKey(receipt.WarehouseId, line.LocationId, line.ProductId, lotId), line.Quantity, 0,
                MovementType.Receipt, new StockSource(SourceTypes.Receipt, receipt.Id, line.Id, receipt.Number)));
        }

        receipt.MarkPosted(user.IdOrNull, clock.GetUtcNow());
        await ledger.ApplyAsync(changes, cancellationToken);
        audit.Add("receipt.posted", "receipt", id.ToString(), receipt.WarehouseId, new { receipt.Number, lines = receipt.Lines.Count, quantity = receipt.Lines.Sum(l => l.Quantity) });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Removes the posted quantities again. Fails if the stock has since been used or reserved.</summary>
    public async Task<ReceiptDto> ReverseAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("goods_receipts", id, cancellationToken);
        var receipt = await LoadAsync(id, cancellationToken);
        receipt.MarkReversed(user.IdOrNull, clock.GetUtcNow(), reason);

        var changes = receipt.Lines.Select(line => new StockChange(
            new BalanceKey(receipt.WarehouseId, line.LocationId, line.ProductId, line.LotId), -line.Quantity, 0,
            MovementType.ReceiptReversal, new StockSource(SourceTypes.Receipt, receipt.Id, line.Id, receipt.Number, reason))).ToList();
        await ledger.ApplyAsync(changes, cancellationToken);

        audit.Add("receipt.reversed", "receipt", id.ToString(), receipt.WarehouseId, new { receipt.Number, reason });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ReceiptDto> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);
        receipt.Cancel();
        audit.Add("receipt.cancelled", "receipt", id.ToString(), receipt.WarehouseId, new { receipt.Number });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<List<GoodsReceiptLine>> BuildLinesAsync(Guid warehouseId, IReadOnlyList<ReceiptLineRequest> requests, CancellationToken cancellationToken)
    {
        Guid? receivingId = null;
        var lines = new List<GoodsReceiptLine>();
        var lineNo = 0;
        foreach (var r in requests)
        {
            lineNo++;
            try
            {
                var sp = await rules.ProductAsync(r.ProductId, cancellationToken);
                var locationId = r.LocationId ?? (receivingId ??= await ReceivingAreaAsync(warehouseId, cancellationToken));
                var location = await rules.LocationAsync(warehouseId, locationId, cancellationToken);
                if (location.Type == LocationType.Dispatch)
                {
                    throw new DomainRuleException("receipt.dispatch_bin", $"Cannot receive into the dispatch dock {location.Code}.");
                }
                var baseQty = StockRules.ToBase(sp, r.Quantity, r.ProductUnitId);
                if (r.ExpectedQuantity is < 0) throw new DomainRuleException("receipt.expected_negative", "Expected quantity cannot be negative.");
                StockRules.CheckLot(sp, r.LotNo, r.ExpiryDate, requireExpiry: false);
                lines.Add(GoodsReceiptLine.Create(sp.Product.Id, r.ProductUnitId, r.Quantity, baseQty, r.ExpectedQuantity, locationId, r.LotNo, r.ExpiryDate, r.Notes));
            }
            catch (DomainRuleException e)
            {
                throw new DomainRuleException(e.Code, $"Line {lineNo}: {e.Message}", e.IsConflict);
            }
        }
        return lines;
    }

    private async Task<Guid> ReceivingAreaAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await db.WarehouseLocations.Where(l => l.WarehouseId == warehouseId && l.IsSystem && l.Type == LocationType.Receiving).Select(l => l.Id).FirstAsync(cancellationToken);

    private async Task<GoodsReceipt> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.GoodsReceipts.Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Receipt not found.");
        await access.EnsureCanAccessAsync(receipt.WarehouseId, cancellationToken);
        return receipt;
    }

    private async Task<ReceiptDto> ToDtoAsync(GoodsReceipt r, uint version, CancellationToken cancellationToken)
    {
        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, p.BaseUnitId, Units = p.Units.Select(u => new { u.Id, u.UnitId }) }).ToDictionaryAsync(p => p.Id, cancellationToken);
        var units = await db.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        var locationIds = r.Lines.Select(l => l.LocationId).Distinct().ToList();
        var locations = await db.WarehouseLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);
        var warehouseCode = await db.Warehouses.Where(w => w.Id == r.WarehouseId).Select(w => w.Code).SingleAsync(cancellationToken);
        var names = await users.NamesAsync([r.CreatedBy, r.PostedBy, r.ReversedBy], cancellationToken);

        var lines = r.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            var baseCode = units[p.BaseUnitId];
            var pack = l.ProductUnitId is null ? null : p.Units.FirstOrDefault(u => u.Id == l.ProductUnitId);
            // A pack unit removed from the product after the receipt was entered shows as "?" (the base quantity is what counts).
            var unitCode = l.ProductUnitId is null ? baseCode : pack is null ? "?" : units[pack.UnitId];
            return new ReceiptLineDto(l.Id, l.LineNo, l.ProductId, p.Sku, p.Name, l.ProductUnitId, unitCode, l.EnteredQuantity, l.Quantity, baseCode,
                l.ExpectedQuantity, l.LocationId, locations[l.LocationId], l.LotNo, l.ExpiryDate, l.Notes);
        }).ToList();

        return new ReceiptDto(r.Id, r.Number, r.WarehouseId, warehouseCode, r.ReceiptDate, r.SupplierName, r.SupplierReference, r.Notes, r.Status, lines,
            r.CreatedAt, names.NameOf(r.CreatedBy), r.PostedAt, names.NameOf(r.PostedBy), r.ReversedAt, names.NameOf(r.ReversedBy), r.ReversalReason, version);
    }
}
