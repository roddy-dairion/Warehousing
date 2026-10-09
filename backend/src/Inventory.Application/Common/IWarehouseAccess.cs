namespace Inventory.Application.Common;

/// <summary>
/// Resolves which warehouses the current user may act in. Every warehouse-scoped query and
/// command must go through this service; warehouse ids from the client are never trusted.
/// </summary>
public interface IWarehouseAccess
{
    /// <summary>Ids of the active warehouses the current user may access.</summary>
    Task<IReadOnlyCollection<Guid>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken);

    /// <summary>Throws <see cref="NotFoundException"/> when the warehouse is unknown, inactive or not accessible.</summary>
    Task EnsureCanAccessAsync(Guid warehouseId, CancellationToken cancellationToken);
}
