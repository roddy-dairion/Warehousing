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

    /// <summary>True when the user may act in every warehouse (administrators).</summary>
    Task<bool> HasAllWarehousesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For management screens: like <see cref="EnsureCanAccessAsync"/> but also allows inactive warehouses
    /// the user is entitled to (all for administrators, explicitly granted ones otherwise).
    /// </summary>
    Task EnsureCanManageAsync(Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>True when another user is active, holds the picking permission and may access the warehouse (for assigning picking).</summary>
    Task<bool> UserCanPickAsync(Guid userId, Guid warehouseId, CancellationToken cancellationToken);
}
