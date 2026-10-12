using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Common;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Dispatches;

/// <summary>
/// Picking against reservations. The server resolves barcodes, bins and lots itself (client product ids are not trusted
/// for scans), checks the scan matches an allocation of this dispatch and refuses over-picking. Picking does not move
/// stock: the goods stay on their bin, shown as reserved, until shipment deducts them.
/// </summary>
public sealed class PickingService(
    IApplicationDbContext db,
    IWarehouseAccess access,
    IStockStore store,
    DispatchService dispatches,
    ReservationService reservations,
    IUserDirectory users,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit)
{
    public async Task<PickListDto> PickListAsync(Guid dispatchId, CancellationToken cancellationToken)
    {
        var d = await db.Dispatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dispatchId, cancellationToken) ?? throw new NotFoundException("Dispatch not found.");
        await access.EnsureCanAccessAsync(d.WarehouseId, cancellationToken);
        var full = await dispatches.GetAsync(dispatchId, cancellationToken);
        var names = await users.NamesAsync([d.AssignedToId], cancellationToken);
        return new PickListDto(d.Id, d.Number, d.Status, names.NameOf(d.AssignedToId), await TasksAsync(dispatchId, cancellationToken), full.Lines);
    }

    /// <summary>Records one scan. Repeating a scan id returns the first result without counting again.</summary>
    public async Task<PickResultDto> PickAsync(Guid dispatchId, PickRequest request, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync(DispatchService.Table, dispatchId, cancellationToken);
        var d = await dispatches.LoadAsync(dispatchId, cancellationToken);

        var scanId = (request.ScanId ?? string.Empty).Trim();
        var existing = await db.PickConfirmations.AsNoTracking().SingleOrDefaultAsync(p => p.DispatchId == dispatchId && p.ScanId == scanId, cancellationToken);
        if (existing is not null)
        {
            return await ResultAsync(d, existing, replayed: true, cancellationToken);
        }

        if (d.Status == DispatchStatus.Approved) d.StartPicking(d.AssignedToId, user.IdOrNull, clock.GetUtcNow());
        d.EnsureStatus(DispatchStatus.Picking);

        // Product: from the barcode (pack barcodes carry their factor), or manual entry.
        Guid productId;
        Guid? productUnitId;
        string? barcode = null;
        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            barcode = request.Barcode.Trim();
            var hit = await (from b in db.ProductBarcodes.AsNoTracking()
                             where b.Value == barcode
                             join p in db.Products on b.ProductId equals p.Id
                             select new { p.Id, p.Sku, p.IsActive, b.ProductUnitId }).FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException($"Barcode {barcode} is not recognised.");
            if (!hit.IsActive) throw new DomainRuleException("product.inactive", $"Product {hit.Sku} is inactive.");
            productId = hit.Id;
            productUnitId = hit.ProductUnitId;
        }
        else if (request.ProductId is { } pid)
        {
            productId = pid;
            productUnitId = request.ProductUnitId;
        }
        else
        {
            throw new DomainRuleException("pick.product_required", "Scan the product barcode or choose the product.");
        }

        var line = d.Lines.SingleOrDefault(l => l.ProductId == productId);
        var product = await db.Products.AsNoTracking().Include(p => p.Units).SingleAsync(p => p.Id == productId, cancellationToken);
        if (line is null)
        {
            throw new DomainRuleException("pick.not_on_dispatch", $"{product.Sku} is not on dispatch {d.Number}. Report it as unexpected if it was found in the order.");
        }

        var location = await FindLocationAsync(d.WarehouseId, request.Location, cancellationToken);
        Guid? lotId = null;
        string? lotNo = null;
        if (product.IsLotTracked)
        {
            if (string.IsNullOrWhiteSpace(request.LotNo)) throw new DomainRuleException("lot.required", $"{product.Sku} is lot-tracked: scan or enter the lot number.");
            lotNo = ProductLot.NormalizeLotNo(request.LotNo);
            lotId = await db.ProductLots.Where(l => l.ProductId == productId && l.LotNo == lotNo).Select(l => (Guid?)l.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new DomainRuleException("lot.unknown", $"Lot {lotNo} of {product.Sku} does not exist.");
        }
        else if (!string.IsNullOrWhiteSpace(request.LotNo))
        {
            throw new DomainRuleException("lot.not_tracked", $"{product.Sku} is not lot-tracked: leave the lot empty.");
        }

        var unitDecimals = await db.UnitsOfMeasure.Where(u => u.Id == product.BaseUnitId).Select(u => new { u.Code, u.Decimals }).SingleAsync(cancellationToken);
        var entered = request.Quantity ?? 1;
        var quantity = Quantities.Precision(Quantities.Positive(product.ToBaseQuantity(entered, productUnitId), Quantities.MaxDecimals, unitDecimals.Code),
            unitDecimals.Decimals, unitDecimals.Code);

        // The allocation (reservation) for this line at this bin and lot.
        var candidates = await (from r in db.StockReservations.AsTracking()
                                join b in db.InventoryBalances on r.BalanceId equals b.Id
                                where r.SourceType == SourceTypes.Dispatch && r.SourceId == dispatchId && r.SourceLineId == line.Id && r.Status == ReservationStatus.Open
                                select new { Reservation = r, b.LocationId, b.LotId }).ToListAsync(cancellationToken);
        var match = candidates.FirstOrDefault(c => c.LocationId == location.Id && c.LotId == lotId);
        if (match is null)
        {
            var allocated = await DescribeAllocationsAsync(candidates.Select(c => (c.LocationId, c.LotId)).ToList(), cancellationToken);
            throw new DomainRuleException("pick.not_allocated",
                $"{product.Sku}{(lotNo is null ? "" : $" lot {lotNo}")} in bin {location.Code} is not allocated to this dispatch. Allocated: {allocated}. Pick from there, or reallocate first.");
        }

        var picked = await PickedAsync(match.Reservation.Id, cancellationToken);
        var remaining = match.Reservation.Outstanding - picked;
        if (quantity > remaining)
        {
            throw new DomainRuleException("pick.over_pick", $"Only {remaining:0.###} {unitDecimals.Code} of {product.Sku} remain to pick from bin {location.Code}.");
        }

        var pick = PickConfirmation.Create(dispatchId, line.Id, match.Reservation.Id, scanId, location.Id, lotId, quantity, barcode, null, user.IdOrNull, clock.GetUtcNow());
        db.PickConfirmations.Add(pick);
        line.AddPicked(quantity);
        audit.Add("dispatch.picked", "dispatch", dispatchId.ToString(), d.WarehouseId, new { d.Number, product.Sku, bin = location.Code, lot = lotNo, quantity, scanId });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ResultAsync(d, pick, replayed: false, cancellationToken);
    }

    /// <summary>Reverses a pick (recorded as a negative pick, so history is kept). Only while picking.</summary>
    public async Task<PickResultDto> UndoAsync(Guid dispatchId, Guid pickId, string scanId, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRowAsync(DispatchService.Table, dispatchId, cancellationToken);
        var d = await dispatches.LoadAsync(dispatchId, cancellationToken);

        var replay = await db.PickConfirmations.AsNoTracking().SingleOrDefaultAsync(p => p.DispatchId == dispatchId && p.ScanId == scanId.Trim(), cancellationToken);
        if (replay is not null) return await ResultAsync(d, replay, replayed: true, cancellationToken);

        d.EnsureStatus(DispatchStatus.Picking);
        var original = await db.PickConfirmations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pickId && p.DispatchId == dispatchId, cancellationToken)
            ?? throw new NotFoundException("Pick not found.");
        if (original.Quantity < 0) throw new DomainRuleException("pick.undo_undo", "An undo cannot be undone. Pick the goods again instead.");
        if (await db.PickConfirmations.AnyAsync(p => p.UndoOfId == pickId, cancellationToken))
        {
            throw new DomainRuleException("pick.already_undone", "This pick was already undone.", isConflict: true);
        }

        var line = d.Lines.Single(l => l.Id == original.LineId);
        var undo = PickConfirmation.Create(dispatchId, line.Id, original.ReservationId, scanId, original.LocationId, original.LotId, -original.Quantity,
            original.Barcode, original.Id, user.IdOrNull, clock.GetUtcNow());
        db.PickConfirmations.Add(undo);
        line.AddPicked(-original.Quantity);
        audit.Add("dispatch.pick_undone", "dispatch", dispatchId.ToString(), d.WarehouseId, new { d.Number, pickId, quantity = original.Quantity });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ResultAsync(d, undo, replayed: false, cancellationToken);
    }

    /// <summary>
    /// Moves part of an allocation to another bin/lot (the picker takes the goods from elsewhere). The new place must be
    /// eligible, available stock; only the unpicked part of an allocation can move.
    /// </summary>
    public async Task<PickListDto> ReallocateAsync(Guid dispatchId, ReallocateRequest request, CancellationToken cancellationToken)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await store.LockRowAsync(DispatchService.Table, dispatchId, cancellationToken);
            var d = await dispatches.LoadAsync(dispatchId, cancellationToken);
            d.EnsureStatus(DispatchStatus.Approved, DispatchStatus.Picking);

            var reservation = await db.StockReservations.AsTracking()
                .SingleOrDefaultAsync(r => r.Id == request.ReservationId && r.SourceType == SourceTypes.Dispatch && r.SourceId == dispatchId, cancellationToken)
                ?? throw new NotFoundException("Allocation not found on this dispatch.");
            var picked = await PickedAsync(reservation.Id, cancellationToken);
            var movable = reservation.Outstanding - picked;
            if (request.Quantity <= 0 || request.Quantity > movable)
            {
                throw new DomainRuleException("reservation.reallocate", $"Only {movable:0.###} of this allocation is not yet picked and can be moved.", isConflict: true);
            }

            var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == reservation.ProductId, cancellationToken);
            var location = await FindLocationAsync(d.WarehouseId, request.Location, cancellationToken);
            Guid? lotId = null;
            if (product.IsLotTracked)
            {
                if (string.IsNullOrWhiteSpace(request.LotNo)) throw new DomainRuleException("lot.required", $"{product.Sku} is lot-tracked: enter the lot number.");
                var lotNo = ProductLot.NormalizeLotNo(request.LotNo);
                lotId = await db.ProductLots.Where(l => l.ProductId == product.Id && l.LotNo == lotNo).Select(l => (Guid?)l.Id).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new DomainRuleException("lot.unknown", $"Lot {lotNo} of {product.Sku} does not exist.");
            }

            await reservations.ReallocateAsync(reservation, location.Id, lotId, request.Quantity, d.RequestedDate, cancellationToken);
            audit.Add("dispatch.reallocated", "dispatch", dispatchId.ToString(), d.WarehouseId,
                new { d.Number, product.Sku, to = location.Code, lot = request.LotNo, request.Quantity });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await PickListAsync(dispatchId, cancellationToken);
    }

    public async Task<DiscrepancyDto> ReportDiscrepancyAsync(Guid dispatchId, DiscrepancyRequest request, CancellationToken cancellationToken)
    {
        var d = await dispatches.LoadAsync(dispatchId, cancellationToken);
        d.EnsureStatus(DispatchStatus.Approved, DispatchStatus.Picking, DispatchStatus.ReadyForDispatch);
        Guid? productId = null;
        if (request.LineId is { } lineId)
        {
            productId = d.Lines.SingleOrDefault(l => l.Id == lineId)?.ProductId ?? throw new DomainRuleException("discrepancy.line_unknown", "That line is not on this dispatch.");
        }
        else if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var code = request.Barcode.Trim();
            productId = await db.ProductBarcodes.Where(b => b.Value == code).Select(b => (Guid?)b.ProductId).FirstOrDefaultAsync(cancellationToken);
        }
        Guid? locationId = string.IsNullOrWhiteSpace(request.Location) ? null : (await FindLocationAsync(d.WarehouseId, request.Location, cancellationToken)).Id;

        var discrepancy = DispatchDiscrepancy.Create(dispatchId, request.LineId, productId, locationId, request.Type, request.Quantity, request.Barcode, request.Note,
            user.IdOrNull, clock.GetUtcNow());
        db.DispatchDiscrepancies.Add(discrepancy);
        audit.Add("dispatch.discrepancy", "dispatch", dispatchId.ToString(), d.WarehouseId, new { d.Number, type = request.Type.ToString(), request.Quantity, request.Note });
        await db.SaveChangesAsync(cancellationToken);
        var full = await dispatches.GetAsync(dispatchId, cancellationToken);
        return full.Discrepancies.Single(x => x.Id == discrepancy.Id);
    }

    // ---- Internals -------------------------------------------------------------------------------

    private async Task<IReadOnlyList<PickTaskDto>> TasksAsync(Guid dispatchId, CancellationToken cancellationToken)
    {
        var rows = await (from r in db.StockReservations.AsNoTracking()
                          where r.SourceType == SourceTypes.Dispatch && r.SourceId == dispatchId && r.QuantityReserved - r.QuantityReleased > 0
                          join b in db.InventoryBalances on r.BalanceId equals b.Id
                          join loc in db.WarehouseLocations on b.LocationId equals loc.Id
                          join p in db.Products on r.ProductId equals p.Id
                          join line in db.Set<DispatchLine>() on r.SourceLineId equals line.Id
                          from lot in db.ProductLots.Where(l => l.Id == b.LotId).DefaultIfEmpty()
                          select new
                          {
                              r.Id, LineId = line.Id, line.LineNo, p.Sku, ProductId = p.Id, ProductName = p.Name,
                              Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
                              LocationId = loc.Id, loc.Code, loc.Barcode, LotId = b.LotId, LotNo = lot == null ? null : lot.LotNo, Expiry = lot == null ? null : lot.ExpiryDate,
                              Quantity = r.QuantityReserved - r.QuantityReleased,
                              Picked = db.PickConfirmations.Where(pc => pc.ReservationId == r.Id).Sum(pc => (decimal?)pc.Quantity) ?? 0,
                          }).ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.Code, StringComparer.Ordinal).ThenBy(r => r.LineNo)
            .Select(r => new PickTaskDto(r.Id, r.LineId, r.LineNo, r.ProductId, r.Sku, r.ProductName, r.Unit, r.LocationId, r.Code, r.Barcode, r.LotId, r.LotNo, r.Expiry,
                r.Quantity, r.Picked, r.Quantity - r.Picked)).ToList();
    }

    private async Task<decimal> PickedAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await db.PickConfirmations.Where(p => p.ReservationId == reservationId).SumAsync(p => (decimal?)p.Quantity, cancellationToken) ?? 0;

    /// <summary>Finds a bin of the warehouse by code or label barcode.</summary>
    private async Task<WarehouseLocation> FindLocationAsync(Guid warehouseId, string? codeOrBarcode, CancellationToken cancellationToken)
    {
        var raw = (codeOrBarcode ?? string.Empty).Trim();
        if (raw.Length == 0) throw new DomainRuleException("pick.location_required", "Scan or enter the bin.");
        var upper = raw.ToUpperInvariant();
        return await db.WarehouseLocations.AsNoTracking()
                   .Where(l => l.WarehouseId == warehouseId && (l.Code == upper || l.Barcode == raw))
                   .OrderBy(l => l.Code == upper ? 0 : 1).FirstOrDefaultAsync(cancellationToken)
               ?? throw new DomainRuleException("location.unknown", $"Bin {raw} was not found in this warehouse.");
    }

    private async Task<string> DescribeAllocationsAsync(List<(Guid LocationId, Guid? LotId)> places, CancellationToken cancellationToken)
    {
        if (places.Count == 0) return "nothing (all picked or released)";
        var locIds = places.Select(p => p.LocationId).Distinct().ToList();
        var lotIds = places.Where(p => p.LotId != null).Select(p => p.LotId!.Value).Distinct().ToList();
        var bins = await db.WarehouseLocations.AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);
        var lots = await db.ProductLots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.LotNo, cancellationToken);
        return string.Join(", ", places.Select(p => p.LotId is { } lid ? $"{bins[p.LocationId]} lot {lots[lid]}" : bins[p.LocationId]));
    }

    private async Task<PickResultDto> ResultAsync(Dispatch d, PickConfirmation pick, bool replayed, CancellationToken cancellationToken)
    {
        var line = await db.Set<DispatchLine>().AsNoTracking().SingleAsync(l => l.Id == pick.LineId, cancellationToken);
        var sku = await db.Products.Where(p => p.Id == line.ProductId).Select(p => p.Sku).SingleAsync(cancellationToken);
        var bin = await db.WarehouseLocations.Where(l => l.Id == pick.LocationId).Select(l => l.Code).SingleAsync(cancellationToken);
        var lot = pick.LotId is null ? null : await db.ProductLots.Where(l => l.Id == pick.LotId).Select(l => l.LotNo).SingleAsync(cancellationToken);
        var reservation = await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == pick.ReservationId, cancellationToken);
        var picked = await PickedAsync(reservation.Id, cancellationToken);
        var status = await db.Dispatches.Where(x => x.Id == d.Id).Select(x => x.Status).SingleAsync(cancellationToken);
        return new PickResultDto(pick.Id, replayed, line.Id, line.LineNo, sku, bin, lot, pick.Quantity, line.QuantityPicked, line.QuantityReserved,
            reservation.QuantityReserved - reservation.QuantityReleased - picked, status);
    }
}
