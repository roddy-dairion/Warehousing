using Inventory.Application.Common;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrEmpty(u.DisplayName) ? u.Email ?? u.Id.ToString() : u.DisplayName, cancellationToken);
    }
}
