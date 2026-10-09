using Inventory.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Warehouses;

/// <summary>Read side for warehouses, always limited to the caller's accessible warehouses.</summary>
public sealed class WarehouseQueries(IApplicationDbContext db, IWarehouseAccess access)
{
    public async Task<IReadOnlyList<WarehouseDto>> ListAccessibleAsync(CancellationToken cancellationToken)
    {
        var ids = await access.GetAccessibleWarehouseIdsAsync(cancellationToken);
        return await db.Warehouses
            .AsNoTracking()
            .Where(w => ids.Contains(w.Id))
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.AddressLine1, w.AddressLine2, w.City, w.Country, w.Phone, w.Email, w.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<WarehouseDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(id, cancellationToken);
        return await db.Warehouses
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.AddressLine1, w.AddressLine2, w.City, w.Country, w.Phone, w.Email, w.IsActive))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Warehouse not found.");
    }
}
