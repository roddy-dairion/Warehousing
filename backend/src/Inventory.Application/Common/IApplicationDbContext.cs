using Inventory.Domain.Auditing;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Common;

/// <summary>Data access used by application handlers.</summary>
public interface IApplicationDbContext
{
    DbSet<Warehouse> Warehouses { get; }

    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
