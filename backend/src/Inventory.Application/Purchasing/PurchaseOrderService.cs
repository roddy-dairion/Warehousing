using System.Linq.Expressions;
using FluentValidation;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Purchasing;

public sealed record PurchaseOrderListItemDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, Guid SupplierId, string SupplierCode, string SupplierName,
    DateOnly OrderDate, DateOnly? ExpectedDate, string? SupplierReference, PurchaseOrderStatus Status, int LineCount,
    /// <summary>Share of the ordered base quantity received so far, 0–100.</summary>
    int ReceivedPercent, bool IsOverdue, DateTimeOffset CreatedAt);

public sealed record PurchaseOrderLineDto(
    Guid Id, int LineNo, Guid ProductId, string Sku, string ProductName, Guid? ProductUnitId, string UnitCode, decimal FactorToBase,
    decimal EnteredQuantity, decimal Quantity, string BaseUnitCode, decimal ReceivedQuantity, decimal Outstanding, string? Notes);

public sealed record PurchaseOrderReceiptDto(Guid Id, string Number, DateOnly ReceiptDate, ReceiptStatus Status);

public sealed record PurchaseOrderDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseCode, Guid SupplierId, string SupplierCode, string SupplierName, string? SupplierEmail,
    DateOnly OrderDate, DateOnly? ExpectedDate, string? SupplierReference, string? Notes, PurchaseOrderStatus Status, bool IsOverdue,
    IReadOnlyList<PurchaseOrderLineDto> Lines, IReadOnlyList<PurchaseOrderReceiptDto> Receipts,
    DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset? OrderedAt, string? OrderedByName,
    DateTimeOffset? ClosedAt, string? ClosedByName, string? CloseReason, uint Version);

/// <param name="ProductUnitId">Pack unit the quantity is entered in; null = base unit.</param>
public sealed record PurchaseOrderLineRequest(Guid ProductId, Guid? ProductUnitId, decimal Quantity, string? Notes);

/// <param name="ExpectedDate">Null = order date + the supplier's lead time (when known).</param>
public sealed record CreatePurchaseOrderRequest(
    Guid WarehouseId, Guid SupplierId, DateOnly? OrderDate, DateOnly? ExpectedDate, string? SupplierReference, string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest>? Lines);

/// <summary>A draft can change everything; a placed order only its dates, supplier reference and notes (send <c>lines: null</c>).</summary>
public sealed record UpdatePurchaseOrderRequest(
    Guid SupplierId, DateOnly OrderDate, DateOnly? ExpectedDate, string? SupplierReference, string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest>? Lines, uint Version);

public sealed record PurchaseOrderReasonRequest(string? Reason);

public sealed class PurchaseOrderListQuery : ListQuery
{
    public Guid WarehouseId { get; set; }
    public PurchaseOrderStatus? Status { get; set; }
    /// <summary>Only orders still expected to deliver (ordered or partially received).</summary>
    public bool OpenOnly { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ProductId { get; set; }
}

public sealed class PurchaseOrderLineRequestValidator : AbstractValidator<PurchaseOrderLineRequest>
{
    public PurchaseOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose a product.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Enter a quantity greater than zero.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("Choose a supplier.");
        RuleFor(x => x.SupplierReference).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).Must(l => l is null || l.Count <= PurchaseOrder.MaxLines);
        RuleForEach(x => x.Lines).SetValidator(new PurchaseOrderLineRequestValidator());
    }
}

public sealed class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("Choose a supplier.");
        RuleFor(x => x.SupplierReference).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).Must(l => l is null || l.Count <= PurchaseOrder.MaxLines);
        RuleForEach(x => x.Lines).SetValidator(new PurchaseOrderLineRequestValidator());
    }
}

/// <summary>
/// Purchase orders: draft → ordered → (partially) received, closed or cancelled. Orders never change stock themselves;
/// goods receipts linked to an order do (see <see cref="ReceiptService"/>), and record what arrived on the order lines.
/// </summary>
public sealed class PurchaseOrderService(
    IApplicationDbContext db,
    IWarehouseAccess access,
    IStockStore store,
    StockRules rules,
    IUserDirectory users,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit)
{
    private static readonly Dictionary<string, Expression<Func<PurchaseOrder, object?>>> Sorts = new()
    {
        ["number"] = o => o.Number,
        ["orderDate"] = o => o.OrderDate,
        ["expectedDate"] = o => o.ExpectedDate,
        ["status"] = o => o.Status,
        ["createdAt"] = o => o.CreatedAt,
    };

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<PagedResult<PurchaseOrderListItemDto>> ListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var today = Today;
        var q = db.PurchaseOrders.AsNoTracking().Where(o => o.WarehouseId == query.WarehouseId);
        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.OpenOnly) q = q.Where(o => o.Status == PurchaseOrderStatus.Ordered || o.Status == PurchaseOrderStatus.PartiallyReceived);
        if (query.SupplierId is { } supplierId) q = q.Where(o => o.SupplierId == supplierId);
        if (query.ProductId is { } productId) q = q.Where(o => o.Lines.Any(l => l.ProductId == productId));
        if (query.Term is { } term)
        {
            q = q.Where(o => o.Number.ToLower().Contains(term) || (o.SupplierReference != null && o.SupplierReference.ToLower().Contains(term))
                || db.Suppliers.Any(s => s.Id == o.SupplierId && (s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term))));
        }
        if (query.SortBy is null) { query.SortBy = "createdAt"; query.SortDir ??= "desc"; }
        return await q.SortBy(query, Sorts, "createdAt", o => o.Id)
            .Select(o => new
            {
                o,
                Supplier = db.Suppliers.Where(s => s.Id == o.SupplierId).Select(s => new { s.Code, s.Name }).First(),
                WarehouseCode = db.Warehouses.Where(w => w.Id == o.WarehouseId).Select(w => w.Code).First(),
                Ordered = o.Lines.Sum(l => (decimal?)l.Quantity) ?? 0,
                Received = o.Lines.Sum(l => (decimal?)(l.ReceivedQuantity < l.Quantity ? l.ReceivedQuantity : l.Quantity)) ?? 0,
                LineCount = o.Lines.Count,
            })
            .Select(x => new PurchaseOrderListItemDto(x.o.Id, x.o.Number, x.o.WarehouseId, x.WarehouseCode, x.o.SupplierId, x.Supplier.Code, x.Supplier.Name,
                x.o.OrderDate, x.o.ExpectedDate, x.o.SupplierReference, x.o.Status, x.LineCount,
                x.Ordered == 0 ? 0 : (int)(x.Received * 100 / x.Ordered),
                (x.o.Status == PurchaseOrderStatus.Ordered || x.o.Status == PurchaseOrderStatus.PartiallyReceived) && x.o.ExpectedDate != null && x.o.ExpectedDate < today,
                x.o.CreatedAt))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var o = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");
        await access.EnsureCanAccessAsync(o.WarehouseId, cancellationToken);
        var version = await db.PurchaseOrders.Where(x => x.Id == id).Select(x => EF.Property<uint>(x, Versioning.Column)).SingleAsync(cancellationToken);
        return await ToDtoAsync(o, version, cancellationToken);
    }

    public async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(request.WarehouseId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == request.WarehouseId, cancellationToken);
        var supplier = await ActiveSupplierAsync(request.SupplierId, cancellationToken);
        var orderDate = request.OrderDate ?? Today;
        var expected = request.ExpectedDate ?? (supplier.LeadTimeDays is { } days ? orderDate.AddDays(days) : null);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var number = await store.NextNumberAsync("PO", warehouse.Id, warehouse.Code, orderDate.Year, cancellationToken);
        var order = PurchaseOrder.Create(number, warehouse.Id, supplier.Id, orderDate);
        order.SetDetails(orderDate, expected, request.SupplierReference, request.Notes);
        order.ReplaceLines(await BuildLinesAsync(request.Lines ?? [], cancellationToken));
        db.PurchaseOrders.Add(order);
        audit.Add("purchase_order.created", "purchase_order", order.Id.ToString(), warehouse.Id, new { order.Number, supplier = supplier.Code, lines = order.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(order.Id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> UpdateAsync(Guid id, UpdatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(order, request.Version);
        if (request.SupplierId != order.SupplierId)
        {
            await ActiveSupplierAsync(request.SupplierId, cancellationToken);
            order.ChangeSupplier(request.SupplierId);
        }
        order.SetDetails(request.OrderDate, request.ExpectedDate, request.SupplierReference, request.Notes);
        if (request.Lines is not null) order.ReplaceLines(await BuildLinesAsync(request.Lines, cancellationToken));
        audit.Add("purchase_order.updated", "purchase_order", id.ToString(), order.WarehouseId,
            new { order.ExpectedDate, lines = request.Lines is null ? (int?)null : order.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Sends the order: from now on it counts as "on order" and goods can be received against it.</summary>
    public async Task<PurchaseOrderDto> PlaceAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("purchase_orders", id, cancellationToken);
        var order = await LoadAsync(id, cancellationToken);
        if (order.Status == PurchaseOrderStatus.Ordered)
        {
            throw new DomainRuleException("purchase_order.already_placed", $"Purchase order {order.Number} is already placed.", isConflict: true);
        }
        await ActiveSupplierAsync(order.SupplierId, cancellationToken);
        foreach (var line in order.Lines) await rules.ProductAsync(line.ProductId, cancellationToken);
        order.Place(user.IdOrNull, clock.GetUtcNow());
        audit.Add("purchase_order.placed", "purchase_order", id.ToString(), order.WarehouseId, new { order.Number, lines = order.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("purchase_orders", id, cancellationToken);
        var order = await LoadAsync(id, cancellationToken);
        await EnsureNoDraftReceiptsAsync(order, cancellationToken);
        order.Cancel(user.IdOrNull, clock.GetUtcNow(), reason);
        audit.Add("purchase_order.cancelled", "purchase_order", id.ToString(), order.WarehouseId, new { order.Number, reason });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Stops expecting the rest (short delivery). Received quantities stay as they are.</summary>
    public async Task<PurchaseOrderDto> CloseAsync(Guid id, string? reason, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync("purchase_orders", id, cancellationToken);
        var order = await LoadAsync(id, cancellationToken);
        await EnsureNoDraftReceiptsAsync(order, cancellationToken);
        order.Close(user.IdOrNull, clock.GetUtcNow(), reason ?? string.Empty);
        audit.Add("purchase_order.closed", "purchase_order", id.ToString(), order.WarehouseId,
            new { order.Number, reason, outstanding = order.Lines.Sum(l => l.Outstanding) });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>A draft receipt for a closed or cancelled order could not be posted: finish or cancel it first.</summary>
    private async Task EnsureNoDraftReceiptsAsync(PurchaseOrder order, CancellationToken cancellationToken)
    {
        var draft = await db.GoodsReceipts.Where(r => r.PurchaseOrderId == order.Id && r.Status == ReceiptStatus.Draft)
            .Select(r => r.Number).FirstOrDefaultAsync(cancellationToken);
        if (draft is not null)
        {
            throw new DomainRuleException("purchase_order.draft_receipt",
                $"Receipt {draft} for this order is still a draft: post or cancel it first.", isConflict: true);
        }
    }

    private async Task<Supplier> ActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == supplierId, cancellationToken)
            ?? throw new DomainRuleException("supplier.unknown", "Supplier not found.");
        if (!supplier.IsActive) throw new DomainRuleException("supplier.inactive", $"Supplier {supplier.Name} is inactive.");
        return supplier;
    }

    private async Task<List<PurchaseOrderLine>> BuildLinesAsync(IReadOnlyList<PurchaseOrderLineRequest> requests, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseOrderLine>();
        var lineNo = 0;
        foreach (var r in requests)
        {
            lineNo++;
            try
            {
                var sp = await rules.ProductAsync(r.ProductId, cancellationToken);
                var baseQty = StockRules.ToBase(sp, r.Quantity, r.ProductUnitId);
                lines.Add(PurchaseOrderLine.Create(sp.Product.Id, r.ProductUnitId, r.Quantity, baseQty, r.Notes));
            }
            catch (DomainRuleException e)
            {
                throw new DomainRuleException(e.Code, $"Line {lineNo}: {e.Message}", e.IsConflict);
            }
        }
        return lines;
    }

    private async Task<PurchaseOrder> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");
        await access.EnsureCanAccessAsync(order.WarehouseId, cancellationToken);
        return order;
    }

    private async Task<PurchaseOrderDto> ToDtoAsync(PurchaseOrder o, uint version, CancellationToken cancellationToken)
    {
        var productIds = o.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, p.BaseUnitId, Units = p.Units.Select(u => new { u.Id, u.UnitId, u.FactorToBase }) })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var units = await db.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == o.SupplierId, cancellationToken);
        var warehouseCode = await db.Warehouses.Where(w => w.Id == o.WarehouseId).Select(w => w.Code).SingleAsync(cancellationToken);
        var receipts = await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == o.Id).OrderBy(r => r.CreatedAt)
            .Select(r => new PurchaseOrderReceiptDto(r.Id, r.Number, r.ReceiptDate, r.Status)).ToListAsync(cancellationToken);
        var names = await users.NamesAsync([o.CreatedBy, o.OrderedBy, o.ClosedBy], cancellationToken);

        var lines = o.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            var baseCode = units[p.BaseUnitId];
            var pack = l.ProductUnitId is null ? null : p.Units.FirstOrDefault(u => u.Id == l.ProductUnitId);
            var unitCode = l.ProductUnitId is null ? baseCode : pack is null ? "?" : units[pack.UnitId];
            return new PurchaseOrderLineDto(l.Id, l.LineNo, l.ProductId, p.Sku, p.Name, l.ProductUnitId, unitCode, pack?.FactorToBase ?? 1,
                l.EnteredQuantity, l.Quantity, baseCode, l.ReceivedQuantity, l.Outstanding, l.Notes);
        }).ToList();

        var overdue = o.IsOpen && o.ExpectedDate is { } e && e < Today;
        return new PurchaseOrderDto(o.Id, o.Number, o.WarehouseId, warehouseCode, supplier.Id, supplier.Code, supplier.Name, supplier.Email,
            o.OrderDate, o.ExpectedDate, o.SupplierReference, o.Notes, o.Status, overdue, lines, receipts,
            o.CreatedAt, names.NameOf(o.CreatedBy), o.OrderedAt, names.NameOf(o.OrderedBy), o.ClosedAt, names.NameOf(o.ClosedBy), o.CloseReason, version);
    }
}
