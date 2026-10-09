using Inventory.Application.Roles;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

internal sealed class RoleQueries(AppDbContext db) : IRoleQueries
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var grants = await db.RolePermissions.AsNoTracking().ToListAsync(cancellationToken);
        return roles
            .Select(r => new RoleDto(r.Id, r.Name!, r.DisplayName, grants.Where(g => g.RoleId == r.Id).Select(g => g.PermissionCode).OrderBy(c => c).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken) =>
        await db.Permissions.AsNoTracking().OrderBy(p => p.Code).Select(p => new PermissionDto(p.Code, p.Description)).ToListAsync(cancellationToken);
}
