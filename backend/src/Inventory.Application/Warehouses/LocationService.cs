using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Warehouses;

/// <summary>Bins and areas inside a warehouse. Every call checks the caller's access to that warehouse.</summary>
public sealed class LocationService(IApplicationDbContext db, IWarehouseAccess access, BarcodeRegistry barcodes, IAuditWriter audit)
{
    private static readonly Expression<Func<WarehouseLocation, LocationDto>> ToDto = l => new LocationDto(
        l.Id, l.WarehouseId, l.Code, l.Zone, l.Type, l.Barcode, l.IsPickable, l.IsSystem, l.IsActive, EF.Property<uint>(l, Versioning.Column));

    private static readonly Dictionary<string, Expression<Func<WarehouseLocation, object?>>> Sorts = new()
    {
        ["code"] = l => l.Code,
        ["zone"] = l => l.Zone,
        ["type"] = l => l.Type,
        ["isActive"] = l => l.IsActive,
    };

    public async Task<PagedResult<LocationDto>> ListAsync(Guid warehouseId, LocationListQuery query, CancellationToken cancellationToken)
    {
        await access.EnsureCanManageAsync(warehouseId, cancellationToken);
        var q = db.WarehouseLocations.AsNoTracking().Where(l => l.WarehouseId == warehouseId);
        if (query.Term is { } term)
        {
            q = q.Where(l => l.Code.ToLower().Contains(term) || (l.Zone != null && l.Zone.ToLower().Contains(term)) || (l.Barcode != null && l.Barcode.ToLower().Contains(term)));
        }
        if (query.IsActive is { } active) q = q.Where(l => l.IsActive == active);
        if (query.Type is { } type) q = q.Where(l => l.Type == type);
        return await q.SortBy(query, Sorts, "code", l => l.Id).Select(ToDto).ToPagedAsync(query, cancellationToken);
    }

    public async Task<LocationDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var location = await db.WarehouseLocations.AsNoTracking().Where(l => l.Id == id).Select(ToDto).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Bin not found.");
        await access.EnsureCanManageAsync(location.WarehouseId, cancellationToken);
        return location;
    }

    public async Task<LocationDto> CreateAsync(Guid warehouseId, CreateLocationRequest request, CancellationToken cancellationToken)
    {
        await access.EnsureCanManageAsync(warehouseId, cancellationToken);
        var location = WarehouseLocation.Create(warehouseId, request.Code, request.Type, request.Zone, request.Barcode, request.IsPickable);
        await EnsureCodeFreeAsync(warehouseId, location.Code, null, cancellationToken);
        if (location.Barcode is not null) await barcodes.EnsureFreeAsync(location.Barcode, null, null, cancellationToken);
        db.WarehouseLocations.Add(location);
        audit.Add("location.created", "location", location.Id.ToString(), warehouseId, new { location.Code, location.Type });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(location.Id, cancellationToken);
    }

    public async Task<LocationDto> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        var location = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(location, request.Version);
        location.Update(request.Zone, request.Type, request.Barcode, request.IsPickable);
        if (location.Barcode is not null) await barcodes.EnsureFreeAsync(location.Barcode, null, location.Id, cancellationToken);
        audit.Add("location.updated", "location", id.ToString(), location.WarehouseId, new { location.Code, location.Type, location.IsPickable });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var location = await LoadAsync(id, cancellationToken);
        if (location.IsActive == active) return;
        if (!active && await db.InventoryBalances.AnyAsync(b => b.LocationId == id && b.OnHand > 0, cancellationToken))
        {
            throw new DomainRuleException("location.has_stock", $"Bin {location.Code} still holds stock. Move it to another bin first.", isConflict: true);
        }
        if (active) location.Activate(); else location.Deactivate();
        audit.Add(active ? "location.activated" : "location.deactivated", "location", id.ToString(), location.WarehouseId);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task EnsureCodeFreeAsync(Guid warehouseId, string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await db.WarehouseLocations.AnyAsync(l => l.WarehouseId == warehouseId && l.Code == code && l.Id != exceptId, cancellationToken))
        {
            throw new DomainRuleException("location.code_taken", $"Bin {code} already exists in this warehouse.", isConflict: true);
        }
    }

    private async Task<WarehouseLocation> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var location = await db.WarehouseLocations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw new NotFoundException("Bin not found.");
        await access.EnsureCanManageAsync(location.WarehouseId, cancellationToken);
        return location;
    }
}
