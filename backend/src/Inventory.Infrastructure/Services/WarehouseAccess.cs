using Inventory.Application.Common;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

/// <summary>Warehouse scope of the current user, read from the database on each request (not from the token).</summary>
internal sealed class WarehouseAccess(AppDbContext db, ICurrentUser currentUser) : IWarehouseAccess
{
    private IReadOnlyCollection<Guid>? _accessible;
    private bool? _all;

    public async Task<IReadOnlyCollection<Guid>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken)
    {
        if (_accessible is not null) return _accessible;
        if (!currentUser.IsAuthenticated) return _accessible = [];

        var userId = currentUser.Id;
        var all = await LoadAllFlagAsync(cancellationToken);
        if (all is null) return _accessible = [];

        var query = db.Warehouses.AsNoTracking().Where(w => w.IsActive);
        if (all == false)
        {
            query = query.Where(w => db.UserWarehouseAccess.Any(a => a.UserId == userId && a.WarehouseId == w.Id));
        }
        return _accessible = await query.Select(w => w.Id).ToListAsync(cancellationToken);
    }

    public async Task EnsureCanAccessAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var ids = await GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (!ids.Contains(warehouseId))
        {
            throw new NotFoundException("Warehouse not found.");
        }
    }

    public async Task<bool> HasAllWarehousesAsync(CancellationToken cancellationToken) => await LoadAllFlagAsync(cancellationToken) == true;

    public async Task EnsureCanManageAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var all = await LoadAllFlagAsync(cancellationToken);
        var exists = await db.Warehouses.AnyAsync(w => w.Id == warehouseId, cancellationToken);
        var allowed = exists && (all == true ||
            (all == false && await db.UserWarehouseAccess.AnyAsync(a => a.UserId == currentUser.Id && a.WarehouseId == warehouseId, cancellationToken)));
        if (!allowed)
        {
            throw new NotFoundException("Warehouse not found.");
        }
    }

    public async Task<bool> UserCanPickAsync(Guid userId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == userId && x.IsActive).Select(x => new { x.AllWarehouses }).SingleOrDefaultAsync(cancellationToken);
        if (u is null) return false;
        var hasPermission = await (from ur in db.UserRoles
                                   join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                                   where ur.UserId == userId && rp.PermissionCode == Domain.Security.Permissions.DispatchesPick
                                   select rp).AnyAsync(cancellationToken);
        if (!hasPermission) return false;
        return u.AllWarehouses || await db.UserWarehouseAccess.AnyAsync(a => a.UserId == userId && a.WarehouseId == warehouseId, cancellationToken);
    }

    /// <summary>null = no active user; otherwise the user's AllWarehouses flag.</summary>
    private async Task<bool?> LoadAllFlagAsync(CancellationToken cancellationToken)
    {
        if (_all is not null) return _all;
        if (!currentUser.IsAuthenticated) return null;
        var userId = currentUser.Id;
        var flag = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive).Select(u => (bool?)u.AllWarehouses).SingleOrDefaultAsync(cancellationToken);
        _all = flag;
        return flag;
    }
}
