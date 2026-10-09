using System.Text.Json;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Infrastructure.Auth;
using Inventory.Infrastructure.Persistence;

namespace Inventory.Infrastructure.Services;

internal sealed class AuditWriter(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditWriter
{
    public void Add(string action, string? entityType = null, string? entityId = null, Guid? warehouseId = null, object? data = null, Guid? userId = null)
    {
        db.AuditLogs.Add(AuditLog.Create(
            clock.GetUtcNow(),
            userId ?? currentUser.IdOrNull,
            action,
            entityType,
            entityId,
            warehouseId,
            data is null ? null : JsonSerializer.Serialize(data, AuditJson.Options),
            currentUser.IpAddress,
            currentUser.CorrelationId));
    }
}
