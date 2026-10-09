using Inventory.Application.Common;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

/// <summary>Warehouse scope of the current user, read from the database on each request (not from the token).</summary>
internal sealed class WarehouseAccess(AppDbContext db, ICurrentUser currentUser) : IWarehouseAccess
{
    private IReadOnlyCollection<Guid>? _cached;

    public async Task<IReadOnlyCollection<Guid>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null) return _cached;
        if (!currentUser.IsAuthenticated) return _cached = [];

        var userId = currentUser.Id;
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.AllWarehouses })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return _cached = [];

        var query = db.Warehouses.AsNoTracking().Where(w => w.IsActive);
        if (!user.AllWarehouses)
        {
            query = query.Where(w => db.UserWarehouseAccess.Any(a => a.UserId == userId && a.WarehouseId == w.Id));
        }
        return _cached = await query.Select(w => w.Id).ToListAsync(cancellationToken);
    }

    public async Task EnsureCanAccessAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var ids = await GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (!ids.Contains(warehouseId))
        {
            throw new NotFoundException("Warehouse not found.");
        }
    }
}
