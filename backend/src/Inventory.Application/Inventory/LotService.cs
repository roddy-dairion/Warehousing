using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Inventory;

/// <summary>Lots of a product and their status (released / quarantine / blocked).</summary>
public sealed class LotService(IApplicationDbContext db, IWarehouseAccess access, IAuditWriter audit)
{
    /// <summary>Lots of the product with their stock in the warehouses the user can access.</summary>
    public async Task<IReadOnlyList<LotDto>> ListAsync(Guid productId, bool onlyWithStock, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken)) throw new NotFoundException("Product not found.");
        var warehouseIds = await access.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var rows = await db.ProductLots.AsNoTracking()
            .Where(l => l.ProductId == productId)
            .Select(l => new
            {
                Lot = l,
                Version = EF.Property<uint>(l, Versioning.Column),
                OnHand = db.InventoryBalances.Where(b => b.LotId == l.Id && warehouseIds.Contains(b.WarehouseId)).Sum(b => (decimal?)b.OnHand) ?? 0,
                Reserved = db.InventoryBalances.Where(b => b.LotId == l.Id && warehouseIds.Contains(b.WarehouseId)).Sum(b => (decimal?)b.Reserved) ?? 0,
            })
            .Where(x => !onlyWithStock || x.OnHand > 0)
            .OrderBy(x => x.Lot.ExpiryDate == null).ThenBy(x => x.Lot.ExpiryDate).ThenBy(x => x.Lot.LotNo)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new LotDto(x.Lot.Id, x.Lot.ProductId, x.Lot.LotNo, x.Lot.ExpiryDate, x.Lot.Status, x.OnHand, x.Reserved, x.Lot.CreatedAt, x.Version)).ToList();
    }

    /// <summary>
    /// Quarantines, blocks or releases a lot. Lots are product-wide, so the user needs access to every warehouse that
    /// holds the lot. Reservations already made stay in place (the dispatch screen flags them); new reservations skip the lot.
    /// </summary>
    public async Task<LotDto> SetStatusAsync(Guid lotId, SetLotStatusRequest request, CancellationToken cancellationToken)
    {
        var lot = await db.ProductLots.SingleOrDefaultAsync(l => l.Id == lotId, cancellationToken) ?? throw new NotFoundException("Lot not found.");
        if (!Enum.IsDefined(request.Status)) throw new DomainRuleException("lot.status_invalid", "Choose a valid status.");
        if (request.Status != LotStatus.Released && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainRuleException("lot.reason_required", "Enter a reason for quarantining or blocking the lot.");
        }

        var holding = await db.InventoryBalances.Where(b => b.LotId == lotId && b.OnHand > 0).Select(b => b.WarehouseId).Distinct().ToListAsync(cancellationToken);
        var accessible = await access.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (holding.Any(w => !accessible.Contains(w)))
        {
            throw new ForbiddenException("This lot is also stored in a warehouse you cannot access. Ask an administrator.");
        }

        db.ExpectVersion(lot, request.Version);
        var previous = lot.Status;
        lot.SetStatus(request.Status);
        audit.Add("lot.status_changed", "lot", lotId.ToString(), holding.Count == 1 ? holding[0] : null,
            new { lot.LotNo, from = previous.ToString(), to = request.Status.ToString(), reason = request.Reason?.Trim() });
        await db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(lot.ProductId, false, cancellationToken)).Single(l => l.Id == lotId);
    }
}
