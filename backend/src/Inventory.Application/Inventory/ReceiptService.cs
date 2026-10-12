using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Purchasing;
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
        if (query.PurchaseOrderId is { } poId) q = q.Where(r => r.PurchaseOrderId == poId);
        if (query.Term is { } term)
        {
            q = q.Where(r => r.Number.ToLower().Contains(term) || (r.SupplierName != null && r.SupplierName.ToLower().Contains(term))
                || (r.SupplierReference != null && r.SupplierReference.ToLower().Contains(term)));
        }
        if (query.SortBy is null) { query.SortBy = "createdAt"; query.SortDir ??= "desc"; }
        return await q.SortBy(query, Sorts, "createdAt", r => r.Id)
            .Select(r => new ReceiptListItemDto(r.Id, r.Number, r.WarehouseId, db.Warehouses.Where(w => w.Id == r.WarehouseId).Select(w => w.Code).First(),
                r.ReceiptDate, r.SupplierName, r.SupplierReference, r.Status, r.Lines.Count, r.CreatedAt, r.PostedAt,
                r.PurchaseOrderId, db.PurchaseOrders.Where(o => o.Id == r.PurchaseOrderId).Select(o => o.Number).FirstOrDefault()))
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
        var order = request.PurchaseOrderId is { } poId ? await OrderForReceivingAsync(poId, warehouse.Id, cancellationToken) : null;
        var supplierName = request.SupplierName;
        if (order is not null && string.IsNullOrWhiteSpace(supplierName))
        {
            supplierName = await db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => s.Name).SingleAsync(cancellationToken);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var number = await store.NextNumberAsync("GR", warehouse.Id, warehouse.Code, date.Year, cancellationToken);
        var receipt = GoodsReceipt.Create(number, warehouse.Id, date, order?.Id);
        receipt.SetHeader(date, supplierName, request.SupplierReference, request.Notes);
        receipt.ReplaceLines(await BuildLinesAsync(warehouse.Id, request.Lines ?? [], order, cancellationToken));
        db.GoodsReceipts.Add(receipt);
        audit.Add("receipt.created", "receipt", receipt.Id.ToString(), warehouse.Id, new { receipt.Number, lines = receipt.Lines.Count, purchaseOrder = order?.Number });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(receipt.Id, cancellationToken);
    }

    /// <summary>
    /// A draft receipt for what is still expected on a purchase order: one line per order line with an outstanding quantity,
    /// in the order's unit when it divides evenly (else the base unit), into the RECEIVING area. Lots are filled in before posting.
    /// </summary>
    public async Task<ReceiptDto> CreateFromOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");
        await access.EnsureCanAccessAsync(order.WarehouseId, cancellationToken);
        var lines = new List<ReceiptLineRequest>();
        foreach (var l in order.Lines.Where(l => l.Outstanding > 0).OrderBy(l => l.LineNo))
        {
            var sp = await rules.ProductAsync(l.ProductId, cancellationToken, requireActive: false);
            var factor = l.ProductUnitId is { } puId ? sp.Product.Units.FirstOrDefault(u => u.Id == puId)?.FactorToBase : null;
            var inPacks = factor is > 0 && l.Outstanding % factor.Value == 0;
            var qty = inPacks ? l.Outstanding / factor!.Value : l.Outstanding;
            lines.Add(new ReceiptLineRequest(l.ProductId, inPacks ? l.ProductUnitId : null, qty, qty, null, null, null, null));
        }
        if (lines.Count == 0)
        {
            throw new DomainRuleException("purchase_order.nothing_outstanding", $"Everything on {order.Number} has been received.", isConflict: true);
        }
        return await CreateAsync(new CreateReceiptRequest(order.WarehouseId, null, null, null, null, lines, order.Id), cancellationToken);
    }

    private async Task<PurchaseOrder> OrderForReceivingAsync(Guid purchaseOrderId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken)
            ?? throw new DomainRuleException("purchase_order.unknown", "Purchase order not found.");
        if (order.WarehouseId != warehouseId)
        {
            // Same answer as an unknown order: the number of another branch's order is not revealed.
            throw new DomainRuleException("purchase_order.unknown", "Purchase order not found in this warehouse.");
        }
        if (!order.CanReceive)
        {
            throw new DomainRuleException("purchase_order.not_receivable",
                $"Purchase order {order.Number} is {PurchaseOrder.Label(order.Status)}: goods cannot be received against it.", isConflict: true);
        }
        return order;
    }

    public async Task<ReceiptDto> UpdateAsync(Guid id, UpdateReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(receipt, request.Version);
        receipt.SetHeader(request.ReceiptDate, request.SupplierName, request.SupplierReference, request.Notes);
        var order = receipt.PurchaseOrderId is { } poId
            ? await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == poId, cancellationToken)
            : null;
        receipt.ReplaceLines(await BuildLinesAsync(receipt.WarehouseId, request.Lines, order, cancellationToken));
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
        await RecordOnOrderAsync(receipt, +1, cancellationToken);
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
        await RecordOnOrderAsync(receipt, -1, cancellationToken);

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

    /// <summary>
    /// Records the posted (sign +1) or reversed (sign −1) quantities on the linked purchase order. The order row is locked so
    /// concurrent receipts for the same order add up correctly.
    /// </summary>
    private async Task RecordOnOrderAsync(GoodsReceipt receipt, int sign, CancellationToken cancellationToken)
    {
        if (receipt.PurchaseOrderId is not { } poId) return;
        await store.LockRowAsync("purchase_orders", poId, cancellationToken);
        var order = await db.PurchaseOrders.Include(o => o.Lines).SingleAsync(o => o.Id == poId, cancellationToken);
        await db.Entry(order).ReloadAsync(cancellationToken);
        foreach (var line in receipt.Lines.Where(l => l.PurchaseOrderLineId is not null))
        {
            var orderLine = order.Lines.Single(l => l.Id == line.PurchaseOrderLineId);
            await db.Entry(orderLine).ReloadAsync(cancellationToken);
            order.RecordReceived(orderLine.Id, sign * line.Quantity);
        }
        audit.Add(sign > 0 ? "purchase_order.received" : "purchase_order.receipt_reversed", "purchase_order", poId.ToString(), order.WarehouseId,
            new { order.Number, receipt = receipt.Number, status = order.Status.ToString() });
    }

    private async Task<List<GoodsReceiptLine>> BuildLinesAsync(Guid warehouseId, IReadOnlyList<ReceiptLineRequest> requests, PurchaseOrder? order,
        CancellationToken cancellationToken)
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
                // Drafts may be saved before the lot is known; posting requires it.
                StockRules.CheckLot(sp, r.LotNo, r.ExpiryDate, requireExpiry: false, requireLot: false);
                var line = GoodsReceiptLine.Create(sp.Product.Id, r.ProductUnitId, r.Quantity, baseQty, r.ExpectedQuantity, locationId, r.LotNo, r.ExpiryDate, r.Notes);
                line.LinkOrderLine(order?.Lines.FirstOrDefault(l => l.ProductId == sp.Product.Id)?.Id);
                lines.Add(line);
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
        var orderNumber = r.PurchaseOrderId is { } poId ? await db.PurchaseOrders.Where(o => o.Id == poId).Select(o => o.Number).SingleAsync(cancellationToken) : null;

        var lines = r.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            var baseCode = units[p.BaseUnitId];
            var pack = l.ProductUnitId is null ? null : p.Units.FirstOrDefault(u => u.Id == l.ProductUnitId);
            // A pack unit removed from the product after the receipt was entered shows as "?" (the base quantity is what counts).
            var unitCode = l.ProductUnitId is null ? baseCode : pack is null ? "?" : units[pack.UnitId];
            return new ReceiptLineDto(l.Id, l.LineNo, l.ProductId, p.Sku, p.Name, l.ProductUnitId, unitCode, l.EnteredQuantity, l.Quantity, baseCode,
                l.ExpectedQuantity, l.LocationId, locations[l.LocationId], l.LotNo, l.ExpiryDate, l.Notes, l.PurchaseOrderLineId);
        }).ToList();

        return new ReceiptDto(r.Id, r.Number, r.WarehouseId, warehouseCode, r.ReceiptDate, r.SupplierName, r.SupplierReference, r.Notes, r.Status, lines,
            r.CreatedAt, names.NameOf(r.CreatedBy), r.PostedAt, names.NameOf(r.PostedBy), r.ReversedAt, names.NameOf(r.ReversedBy), r.ReversalReason, version, r.PurchaseOrderId, orderNumber);
    }
}
