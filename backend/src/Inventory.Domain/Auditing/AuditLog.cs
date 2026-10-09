namespace Inventory.Domain.Auditing;

/// <summary>Append-only record of a material action (stock change, approval, login, permission change...).</summary>
public sealed class AuditLog
{
    private AuditLog() { }

    public long Id { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }
    public Guid? UserId { get; private init; }
    public string Action { get; private init; } = null!;
    public string? EntityType { get; private init; }
    public string? EntityId { get; private init; }
    public Guid? WarehouseId { get; private init; }
    /// <summary>JSON details (before/after or context). Never contains secrets.</summary>
    public string? Data { get; private init; }
    public string? IpAddress { get; private init; }
    public string? CorrelationId { get; private init; }

    public static AuditLog Create(
        DateTimeOffset occurredAt, Guid? userId, string action, string? entityType = null, string? entityId = null,
        Guid? warehouseId = null, string? data = null, string? ipAddress = null, string? correlationId = null) =>
        new()
        {
            OccurredAt = occurredAt,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            WarehouseId = warehouseId,
            Data = data,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
        };
}
