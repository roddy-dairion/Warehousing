namespace Inventory.Application.Common;

/// <summary>Adds audit entries to the current unit of work (saved with the business change).</summary>
public interface IAuditWriter
{
    void Add(string action, string? entityType = null, string? entityId = null, Guid? warehouseId = null, object? data = null, Guid? userId = null);
}
