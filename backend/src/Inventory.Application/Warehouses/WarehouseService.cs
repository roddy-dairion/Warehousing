using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Security;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Warehouses;

/// <summary>Warehouses (branches). Reads are limited to the caller's warehouses.</summary>
public sealed class WarehouseService(IApplicationDbContext db, IWarehouseAccess access, ICurrentUser user, IAuditWriter audit)
{
    private static readonly Expression<Func<Warehouse, WarehouseDto>> ToDto = w => new WarehouseDto(
        w.Id, w.Code, w.Name, w.AddressLine1, w.AddressLine2, w.City, w.Country, w.Phone, w.Email, w.IsActive, EF.Property<uint>(w, Versioning.Column));

    /// <param name="includeInactive">Only honoured for administrators with warehouse management rights.</param>
    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<Warehouse> query = db.Warehouses.AsNoTracking();
        if (includeInactive && user.HasPermission(Permissions.WarehousesManage) && await access.HasAllWarehousesAsync(cancellationToken))
        {
            // all warehouses
        }
        else
        {
            var ids = await access.GetAccessibleWarehouseIdsAsync(cancellationToken);
            query = query.Where(w => ids.Contains(w.Id));
        }
        return await query.OrderBy(w => w.Code).Select(ToDto).ToListAsync(cancellationToken);
    }

    public async Task<WarehouseDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (user.HasPermission(Permissions.WarehousesManage)) await access.EnsureCanManageAsync(id, cancellationToken);
        else await access.EnsureCanAccessAsync(id, cancellationToken);
        return await db.Warehouses.AsNoTracking().Where(w => w.Id == id).Select(ToDto).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Warehouse not found.");
    }

    /// <summary>Creates a branch with its built-in areas (RECEIVING, DOCK, QUARANTINE).</summary>
    public async Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        if (!await access.HasAllWarehousesAsync(cancellationToken))
        {
            throw new ForbiddenException("Only users with access to all warehouses can create a warehouse.");
        }
        var warehouse = Warehouse.Create(request.Code, request.Name);
        warehouse.SetContact(request.AddressLine1, request.AddressLine2, request.City, request.Country, request.Phone, request.Email);
        if (await db.Warehouses.AnyAsync(w => w.Code == warehouse.Code, cancellationToken))
        {
            throw new DomainRuleException("warehouse.code_taken", $"Warehouse code {warehouse.Code} is already used.", isConflict: true);
        }
        db.Warehouses.Add(warehouse);
        foreach (var (code, type) in WarehouseLocation.SystemLocations)
        {
            db.WarehouseLocations.Add(WarehouseLocation.CreateSystem(warehouse.Id, code, type));
        }
        audit.Add("warehouse.created", "warehouse", warehouse.Id.ToString(), warehouse.Id, new { warehouse.Code, warehouse.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(warehouse.Id, cancellationToken);
    }

    public async Task<WarehouseDto> UpdateAsync(Guid id, UpdateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await LoadForManagementAsync(id, cancellationToken);
        db.ExpectVersion(warehouse, request.Version);
        warehouse.Rename(request.Name);
        warehouse.SetContact(request.AddressLine1, request.AddressLine2, request.City, request.Country, request.Phone, request.Email);
        audit.Add("warehouse.updated", "warehouse", id.ToString(), id, new { warehouse.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var warehouse = await LoadForManagementAsync(id, cancellationToken);
        if (warehouse.IsActive == active) return;
        if (!active && !await db.Warehouses.AnyAsync(w => w.IsActive && w.Id != id, cancellationToken))
        {
            throw new DomainRuleException("warehouse.last_active", "At least one warehouse must stay active.", isConflict: true);
        }
        if (active) warehouse.Activate(); else warehouse.Deactivate();
        audit.Add(active ? "warehouse.activated" : "warehouse.deactivated", "warehouse", id.ToString(), id);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Warehouse> LoadForManagementAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.EnsureCanManageAsync(id, cancellationToken);
        return await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, cancellationToken) ?? throw new NotFoundException("Warehouse not found.");
    }
}
