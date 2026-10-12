using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Application.Documents;
using Inventory.Application.Inventory;
using Inventory.Domain.Common;
using Inventory.Domain.Customers;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;
using Inventory.Domain.Inventory;
using Inventory.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Application.Dispatches;

/// <summary>
/// Dispatch lifecycle: draft → submitted → approved (stock reserved) → picking → ready → dispatched (stock deducted)
/// → delivered, or cancelled before shipment. Every stock-relevant transition locks the dispatch row first.
/// </summary>
public sealed class DispatchService(
    IApplicationDbContext db,
    IWarehouseAccess access,
    IStockStore store,
    StockRules rules,
    ReservationService reservations,
    DocumentService documents,
    IUserDirectory users,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit,
    IOptions<DispatchOptions> options)
{
    internal const string Table = "dispatches";

    private static readonly Dictionary<string, Expression<Func<Dispatch, object?>>> Sorts = new()
    {
        ["number"] = d => d.Number,
        ["requestedDate"] = d => d.RequestedDate,
        ["status"] = d => d.Status,
        ["createdAt"] = d => d.CreatedAt,
        ["shippedAt"] = d => d.ShippedAt,
    };

    // ---- Queries ------------------------------------------------------------------------------

    public async Task<PagedResult<DispatchListItemDto>> ListAsync(DispatchListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(query.WarehouseId, cancellationToken);
        var q = db.Dispatches.AsNoTracking().Where(d => d.WarehouseId == query.WarehouseId);
        if (query.Status is { } status) q = q.Where(d => d.Status == status);
        if (query.Open == true) q = q.Where(d => d.Status != DispatchStatus.Delivered && d.Status != DispatchStatus.Cancelled);
        if (query.Open == false) q = q.Where(d => d.Status == DispatchStatus.Delivered || d.Status == DispatchStatus.Cancelled);
        if (query.CustomerId is { } customerId) q = q.Where(d => d.CustomerId == customerId);
        if (query.AssignedToMe) q = q.Where(d => d.AssignedToId == user.IdOrNull);
        else if (query.AssignedToId is { } assignee) q = q.Where(d => d.AssignedToId == assignee);
        if (query.From is { } from) q = q.Where(d => d.RequestedDate >= from);
        if (query.To is { } to) q = q.Where(d => d.RequestedDate <= to);
        if (query.Term is { } term)
        {
            q = q.Where(d => d.Number.ToLower().Contains(term)
                || (d.CustomerReference != null && d.CustomerReference.ToLower().Contains(term))
                || db.Customers.Any(c => c.Id == d.CustomerId && (c.CompanyName.ToLower().Contains(term) || c.Reference.ToLower().Contains(term))));
        }
        if (query.SortBy is null) { query.SortBy = "createdAt"; query.SortDir ??= "desc"; }

        var page = await q.SortBy(query, Sorts, "createdAt", d => d.Id)
            .Select(d => new
            {
                d.Id, d.Number, d.WarehouseId, d.CustomerId, d.CustomerReference, d.RequestedDate, d.Status, d.AssignedToId, d.CreatedAt, d.ShippedAt, d.DeliveredAt,
                WarehouseCode = db.Warehouses.Where(w => w.Id == d.WarehouseId).Select(w => w.Code).First(),
                Customer = db.Customers.Where(c => c.Id == d.CustomerId).Select(c => new { c.Reference, c.CompanyName }).First(),
                LineCount = d.Lines.Count,
                Requested = d.Lines.Sum(l => l.Quantity),
                Shipped = d.Lines.Sum(l => l.QuantityShipped),
                BackorderOf = db.Dispatches.Where(p => p.Id == d.BackorderOfId).Select(p => p.Number).FirstOrDefault(),
            })
            .ToPagedAsync(query, cancellationToken);
        var names = await users.NamesAsync(page.Items.Select(i => i.AssignedToId), cancellationToken);
        return new PagedResult<DispatchListItemDto>(page.Items.Select(i => new DispatchListItemDto(
            i.Id, i.Number, i.WarehouseId, i.WarehouseCode, i.CustomerId, i.Customer.Reference, i.Customer.CompanyName, i.CustomerReference,
            i.RequestedDate, i.Status, i.LineCount, i.Requested, i.Shipped, i.BackorderOf, names.NameOf(i.AssignedToId), i.CreatedAt, i.ShippedAt, i.DeliveredAt)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<DispatchDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var d = await db.Dispatches.AsNoTracking().Include(x => x.Lines).Include(x => x.History)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Dispatch not found.");
        await access.EnsureCanAccessAsync(d.WarehouseId, cancellationToken);
        var version = await db.Dispatches.Where(x => x.Id == id).Select(x => EF.Property<uint>(x, Versioning.Column)).SingleAsync(cancellationToken);
        return await ToDtoAsync(d, version, cancellationToken);
    }

    // ---- Draft --------------------------------------------------------------------------------

    public async Task<DispatchDto> CreateAsync(CreateDispatchRequest request, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(request.WarehouseId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == request.WarehouseId, cancellationToken);
        await EnsureCustomerAsync(request.CustomerId, request.DeliveryAddressId, cancellationToken);
        var date = request.RequestedDate ?? Today();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var number = await store.NextNumberAsync("DSP", warehouse.Id, warehouse.Code, clock.GetUtcNow().Year, cancellationToken);
        var dispatch = Dispatch.Create(number, warehouse.Id, request.CustomerId, request.DeliveryAddressId, date, request.CustomerOrderReference, request.Notes,
            user.IdOrNull, clock.GetUtcNow());
        dispatch.ReplaceLines(await BuildLinesAsync(request.Lines ?? [], cancellationToken));
        db.Dispatches.Add(dispatch);
        audit.Add("dispatch.created", "dispatch", dispatch.Id.ToString(), warehouse.Id, new { dispatch.Number, lines = dispatch.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(dispatch.Id, cancellationToken);
    }

    public async Task<DispatchDto> UpdateAsync(Guid id, UpdateDispatchRequest request, CancellationToken cancellationToken)
    {
        var dispatch = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(dispatch, request.Version);
        await EnsureCustomerAsync(request.CustomerId, request.DeliveryAddressId, cancellationToken);
        dispatch.SetHeader(request.CustomerId, request.DeliveryAddressId, request.RequestedDate, request.CustomerOrderReference, request.Notes);
        dispatch.ReplaceLines(await BuildLinesAsync(request.Lines, cancellationToken));
        audit.Add("dispatch.updated", "dispatch", id.ToString(), dispatch.WarehouseId, new { lines = dispatch.Lines.Count });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<DispatchDto> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.submitted", async d =>
        {
            // Re-validate everything that may have changed since the draft was saved.
            foreach (var line in d.Lines)
            {
                var sp = await rules.ProductAsync(line.ProductId, cancellationToken);
                if (line.ProductUnitId is not null) StockRules.ToBase(sp, line.EnteredQuantity, line.ProductUnitId);
            }
            var shipTo = await ResolveShipToAsync(d.CustomerId, d.DeliveryAddressId, cancellationToken);
            d.Submit(shipTo, user.IdOrNull, clock.GetUtcNow());
        }, cancellationToken);

    public Task<DispatchDto> ReturnAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.returned", d =>
        {
            d.Return(user.IdOrNull, clock.GetUtcNow(), reason);
            return Task.CompletedTask;
        }, cancellationToken);

    // ---- Approval (reserves stock) ------------------------------------------------------------

    /// <summary>Approves and reserves stock for every line (FEFO), all or nothing. Shortages return 409 with the list.</summary>
    public Task<DispatchDto> ApproveAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.approved", async d =>
        {
            var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == d.CustomerId, cancellationToken);
            if (!customer.IsActive) throw new DomainRuleException("customer.inactive", $"Customer {customer.Reference} is inactive.");
            foreach (var line in d.Lines) await rules.ProductAsync(line.ProductId, cancellationToken);

            d.Approve(user.IdOrNull, clock.GetUtcNow(), options.Value.RequireDifferentApprover, note);
            await reservations.ReserveAsync(SourceTypes.Dispatch, d.Id, d.WarehouseId,
                d.Lines.Select(l => new ReservationRequest(l.Id, l.ProductId, l.Quantity)).ToList(), d.RequestedDate, cancellationToken);
        }, cancellationToken);

    // ---- Picking ------------------------------------------------------------------------------

    public Task<DispatchDto> StartPickingAsync(Guid id, Guid? assignedToId, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.picking_started", async d =>
        {
            if (assignedToId is not null) await EnsureAssigneeAsync(assignedToId.Value, d.WarehouseId, cancellationToken);
            d.StartPicking(assignedToId, user.IdOrNull, clock.GetUtcNow());
        }, cancellationToken);

    public Task<DispatchDto> AssignAsync(Guid id, Guid? assignedToId, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.assigned", async d =>
        {
            if (assignedToId is not null) await EnsureAssigneeAsync(assignedToId.Value, d.WarehouseId, cancellationToken);
            d.Assign(assignedToId);
        }, cancellationToken);

    public Task<DispatchDto> CompletePickingAsync(Guid id, bool allowShort, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.picking_completed", d =>
        {
            if (allowShort && !options.Value.AllowPartialShipment)
            {
                throw new DomainRuleException("dispatch.partial_disabled", "Partial dispatches are not allowed. Pick every line in full.");
            }
            d.CompletePicking(allowShort, user.IdOrNull, clock.GetUtcNow());
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<DispatchDto> ReopenPickingAsync(Guid id, string? reason, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.picking_reopened", d =>
        {
            d.ReopenPicking(user.IdOrNull, clock.GetUtcNow(), reason);
            return Task.CompletedTask;
        }, cancellationToken);

    // ---- Shipment (deducts stock) --------------------------------------------------------------

    /// <summary>
    /// Ships the picked quantities: one transaction deducts on-hand stock, consumes the reservations and changes the status.
    /// For short lines the remainder is released (close short) or moved to a new backorder dispatch. A second call
    /// finds the dispatch already shipped and returns 409, so stock is never deducted twice.
    /// </summary>
    public async Task<DispatchDto> ShipAsync(Guid id, ShipRequest request, CancellationToken cancellationToken)
    {
        Guid packingSlipId;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await store.LockRowAsync(Table, id, cancellationToken);
            var d = await LoadAsync(id, cancellationToken);
            var now = clock.GetUtcNow();
            d.Ship(request.Carrier, request.TrackingNumber, request.ShortDecision, user.IdOrNull, now);

            // Consume what was picked, per reservation.
            var picked = await db.PickConfirmations.Where(p => p.DispatchId == id)
                .GroupBy(p => p.ReservationId).Select(g => new { g.Key, Qty = g.Sum(p => p.Quantity) })
                .Where(x => x.Qty > 0).ToListAsync(cancellationToken);
            await reservations.ConsumeAsync(SourceTypes.Dispatch, id, d.Number, picked.Select(p => new ReservationConsumption(p.Key, p.Qty)).ToList(), cancellationToken);

            if (d.ShortDecision == ShortShipDecision.Backorder)
            {
                var number = await store.NextNumberAsync("DSP", d.WarehouseId, await WarehouseCodeAsync(d.WarehouseId, cancellationToken), now.Year, cancellationToken);
                var shortLines = d.Lines.Where(l => l.Outstanding > 0).ToList();
                var childLines = shortLines.ToDictionary(l => l.Id, l => DispatchLine.Create(l.ProductId, null, l.Outstanding, l.Outstanding, l.Notes));
                var child = Dispatch.CreateBackorder(number, d, childLines.Values, user.IdOrNull, now);
                db.Dispatches.Add(child);
                await reservations.TransferAsync(SourceTypes.Dispatch, d.Id, child.Id, childLines.ToDictionary(kv => kv.Key, kv => kv.Value.Id), cancellationToken);
                audit.Add("dispatch.backorder_created", "dispatch", child.Id.ToString(), d.WarehouseId, new { child.Number, parent = d.Number });
            }
            else
            {
                await reservations.ReleaseAsync(SourceTypes.Dispatch, id, null, cancellationToken);
            }

            var slip = DispatchDocument.Request(id, DocumentType.PackingSlip, emailToCustomer: false, user.IdOrNull, now);
            db.DispatchDocuments.Add(slip);
            packingSlipId = slip.Id;
            audit.Add("dispatch.shipped", "dispatch", id.ToString(), d.WarehouseId, new
            {
                d.Number, d.Carrier, d.TrackingNumber, decision = d.ShortDecision?.ToString(),
                shipped = d.Lines.Sum(l => l.QuantityShipped), requested = d.Lines.Sum(l => l.Quantity),
            });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        await documents.TryGenerateAsync(packingSlipId, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // ---- Cancellation ---------------------------------------------------------------------------

    public Task<DispatchDto> CancelAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        TransitionAsync(id, "dispatch.cancelled", async d =>
        {
            var heldReservations = d.HoldsReservations;
            d.Cancel(user.IdOrNull, clock.GetUtcNow(), reason);
            if (heldReservations) await reservations.ReleaseAsync(SourceTypes.Dispatch, d.Id, null, cancellationToken);
        }, cancellationToken);

    // ---- Internals --------------------------------------------------------------------------------

    /// <summary>Locks the dispatch row, loads it, applies the change and commits (with audit) in one transaction.</summary>
    private async Task<DispatchDto> TransitionAsync(Guid id, string action, Func<Dispatch, Task> change, CancellationToken cancellationToken)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await store.LockRowAsync(Table, id, cancellationToken);
            var d = await LoadAsync(id, cancellationToken);
            await change(d);
            audit.Add(action, "dispatch", id.ToString(), d.WarehouseId, new { d.Number, status = d.Status.ToString() });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    internal async Task<Dispatch> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var d = await db.Dispatches.AsTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Dispatch not found.");
        await access.EnsureCanAccessAsync(d.WarehouseId, cancellationToken);
        return d;
    }

    private async Task<List<DispatchLine>> BuildLinesAsync(IReadOnlyList<DispatchLineRequest> requests, CancellationToken cancellationToken)
    {
        var lines = new List<DispatchLine>();
        var lineNo = 0;
        foreach (var r in requests)
        {
            lineNo++;
            try
            {
                var sp = await rules.ProductAsync(r.ProductId, cancellationToken);
                var baseQty = StockRules.ToBase(sp, r.Quantity, r.ProductUnitId);
                lines.Add(DispatchLine.Create(sp.Product.Id, r.ProductUnitId, r.Quantity, baseQty, r.Notes));
            }
            catch (DomainRuleException e)
            {
                throw new DomainRuleException(e.Code, $"Line {lineNo}: {e.Message}", e.IsConflict);
            }
        }
        return lines;
    }

    private async Task EnsureCustomerAsync(Guid customerId, Guid? addressId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().Include(c => c.Addresses).SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new DomainRuleException("customer.unknown", "Customer not found.");
        if (!customer.IsActive) throw new DomainRuleException("customer.inactive", $"Customer {customer.Reference} is inactive.");
        if (addressId is { } aid && !customer.Addresses.Any(a => a.Id == aid && a.IsActive))
        {
            throw new DomainRuleException("address.unknown", "Choose an active delivery address of this customer.");
        }
    }

    private async Task<AddressSnapshot> ResolveShipToAsync(Guid customerId, Guid? addressId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().Include(c => c.Addresses).SingleAsync(c => c.Id == customerId, cancellationToken);
        if (!customer.IsActive) throw new DomainRuleException("customer.inactive", $"Customer {customer.Reference} is inactive.");
        CustomerAddress? address = addressId is { } aid
            ? customer.Addresses.SingleOrDefault(a => a.Id == aid && a.IsActive)
            : customer.Addresses.SingleOrDefault(a => a.IsDefault && a.IsActive);
        if (address is null)
        {
            throw new DomainRuleException("address.required", $"Customer {customer.Reference} has no active delivery address. Add one or choose an address.");
        }
        return new AddressSnapshot(address.Label, address.Line1, address.Line2, address.City, address.Country, address.ContactName ?? customer.ContactName, address.Phone ?? customer.Phone);
    }

    private async Task EnsureAssigneeAsync(Guid userId, Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await access.UserCanPickAsync(userId, warehouseId, cancellationToken))
        {
            throw new DomainRuleException("dispatch.assignee_invalid", "That person cannot pick in this warehouse (inactive, no picking permission or no access).");
        }
    }

    private Task<string> WarehouseCodeAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        db.Warehouses.Where(w => w.Id == warehouseId).Select(w => w.Code).SingleAsync(cancellationToken);

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private async Task<DispatchDto> ToDtoAsync(Dispatch d, uint version, CancellationToken cancellationToken)
    {
        var productIds = d.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, p.IsLotTracked, p.BaseUnitId, Units = p.Units.Select(u => new { u.Id, u.UnitId }).ToList() })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var units = await db.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        var warehouseCode = await WarehouseCodeAsync(d.WarehouseId, cancellationToken);
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == d.CustomerId).Select(c => new { c.Reference, c.CompanyName, c.Email }).SingleAsync(cancellationToken);
        var backorderOf = d.BackorderOfId is null ? null : await db.Dispatches.Where(x => x.Id == d.BackorderOfId).Select(x => x.Number).SingleAsync(cancellationToken);
        var backorders = await db.Dispatches.AsNoTracking().Where(x => x.BackorderOfId == d.Id).OrderBy(x => x.CreatedAt)
            .Select(x => new BackorderRefDto(x.Id, x.Number, x.Status)).ToListAsync(cancellationToken);

        var discrepancies = await db.DispatchDiscrepancies.AsNoTracking().Where(x => x.DispatchId == d.Id).OrderBy(x => x.At).ToListAsync(cancellationToken);
        var locationIds = discrepancies.Where(x => x.LocationId != null).Select(x => x.LocationId!.Value).Distinct().ToList();
        var locations = await db.WarehouseLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);
        var discrepancyProducts = discrepancies.Where(x => x.ProductId != null).Select(x => x.ProductId!.Value).Distinct().ToList();
        var skus = await db.Products.AsNoTracking().Where(p => discrepancyProducts.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Sku, cancellationToken);

        var docs = await (from doc in db.DispatchDocuments.AsNoTracking()
                          where doc.DispatchId == d.Id
                          orderby doc.RequestedAt
                          select new DispatchDocumentDto(doc.Id, doc.Type, doc.Status,
                              db.StoredFiles.Where(f => f.Id == doc.FileId).Select(f => f.FileName).FirstOrDefault(),
                              db.DocumentTemplates.Where(t => t.Id == doc.TemplateId).Select(t => (int?)t.Version).FirstOrDefault(),
                              doc.RequestedAt, doc.GeneratedAt, doc.LastError)).ToListAsync(cancellationToken);
        var emails = await db.EmailMessages.AsNoTracking().Where(m => m.RelatedType == "dispatch" && m.RelatedId == d.Id).OrderBy(m => m.CreatedAt)
            .Select(m => new DispatchEmailDto(m.Id, m.To, m.Subject, m.Status, m.Attempts, m.LastError, m.CreatedAt, m.SentAt)).ToListAsync(cancellationToken);

        var pod = await db.ProofsOfDelivery.AsNoTracking().SingleOrDefaultAsync(p => p.DispatchId == d.Id, cancellationToken);
        var photoIds = pod is null ? [] : await db.StoredFiles.AsNoTracking()
            .Where(f => f.OwnerType == DeliveryService.PhotoOwner && f.OwnerId == d.Id).OrderBy(f => f.UploadedAt).Select(f => f.Id).ToListAsync(cancellationToken);

        var names = await users.NamesAsync(
            new[] { d.CreatedBy, d.SubmittedBy, d.ApprovedBy, d.ShippedBy, d.CancelledBy, d.AssignedToId, pod?.RecordedBy }
                .Concat(d.History.Select(h => h.UserId)).Concat(discrepancies.Select(x => x.UserId)), cancellationToken);

        var lines = d.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            var baseCode = units[p.BaseUnitId];
            var pack = l.ProductUnitId is null ? null : p.Units.FirstOrDefault(u => u.Id == l.ProductUnitId);
            var unitCode = l.ProductUnitId is null ? baseCode : pack is null ? "?" : units[pack.UnitId];
            return new DispatchLineDto(l.Id, l.LineNo, l.ProductId, p.Sku, p.Name, p.IsLotTracked, baseCode, l.ProductUnitId, unitCode, l.EnteredQuantity,
                l.Quantity, l.QuantityReserved, l.QuantityPicked, l.QuantityShipped, l.Outstanding, l.Notes);
        }).ToList();

        var me = user.IdOrNull;
        var s = d.Status;
        var actions = new DispatchActionsDto(
            CanEdit: s == DispatchStatus.Draft && user.HasPermission(Permissions.DispatchesCreate),
            CanSubmit: s == DispatchStatus.Draft && user.HasPermission(Permissions.DispatchesCreate),
            CanReturn: s == DispatchStatus.Submitted && user.HasPermission(Permissions.DispatchesApprove),
            CanApprove: s == DispatchStatus.Submitted && user.HasPermission(Permissions.DispatchesApprove)
                && (!options.Value.RequireDifferentApprover || (me != d.CreatedBy && me != d.SubmittedBy)),
            CanStartPicking: s == DispatchStatus.Approved && user.HasPermission(Permissions.DispatchesPick),
            CanPick: s is DispatchStatus.Approved or DispatchStatus.Picking && user.HasPermission(Permissions.DispatchesPick),
            CanCompletePicking: s == DispatchStatus.Picking && user.HasPermission(Permissions.DispatchesPick),
            CanReopenPicking: s == DispatchStatus.ReadyForDispatch && user.HasPermission(Permissions.DispatchesApprove),
            CanShip: s == DispatchStatus.ReadyForDispatch && user.HasPermission(Permissions.DispatchesShip),
            CanDeliver: s == DispatchStatus.Dispatched && user.HasPermission(Permissions.DispatchesDeliver),
            CanCancel: s is not (DispatchStatus.Dispatched or DispatchStatus.Delivered or DispatchStatus.Cancelled) && user.HasPermission(Permissions.DispatchesCancel));

        return new DispatchDto(d.Id, d.Number, d.WarehouseId, warehouseCode, d.CustomerId, customer.Reference, customer.CompanyName, customer.Email,
            d.DeliveryAddressId, d.ShipTo is { } a ? new AddressDto(a.Label, a.Line1, a.Line2, a.City, a.Country, a.ContactName, a.Phone) : null,
            d.CustomerReference, d.RequestedDate, d.Notes, d.Status, d.BackorderOfId, backorderOf, backorders, d.AssignedToId, names.NameOf(d.AssignedToId),
            lines,
            d.History.OrderBy(h => h.At).Select(h => new StatusHistoryDto(h.FromStatus, h.ToStatus, h.At, names.NameOf(h.UserId), h.Note)).ToList(),
            discrepancies.Select(x => new DiscrepancyDto(x.Id, x.Type, x.LineId, x.ProductId is { } pid ? skus.GetValueOrDefault(pid) : null,
                x.LocationId is { } lid ? locations.GetValueOrDefault(lid) : null, x.Quantity, x.Barcode, x.Note, x.At, names.NameOf(x.UserId))).ToList(),
            docs, emails,
            pod is null ? null : new ProofOfDeliveryDto(pod.ReceiverName, pod.DeliveredAt, pod.Notes, pod.SignatureFileId, photoIds, pod.RecordedAt, names.NameOf(pod.RecordedBy)),
            names.NameOf(d.CreatedBy), d.CreatedAt, names.NameOf(d.SubmittedBy), d.SubmittedAt, names.NameOf(d.ApprovedBy), d.ApprovedAt,
            names.NameOf(d.ShippedBy), d.ShippedAt, d.Carrier, d.TrackingNumber, d.ShortDecision, d.DeliveredAt, d.CancelledAt, names.NameOf(d.CancelledBy), d.CancelReason,
            actions, version);
    }
}
