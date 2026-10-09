namespace Inventory.Domain.Security;

/// <summary>Built-in roles and the permissions they receive when first created.</summary>
public static class Roles
{
    public const string Administrator = "ADMIN";
    public const string WarehouseManager = "WH_MANAGER";
    public const string WarehouseOperator = "WH_OPERATOR";

    public sealed record RoleDefinition(string Code, string Name, IReadOnlyList<string> Permissions);

    public static IReadOnlyList<RoleDefinition> Defaults { get; } =
    [
        new(Administrator, "Administrator", Security.Permissions.All.Keys.ToList()),
        new(WarehouseManager, "Warehouse manager",
        [
            Security.Permissions.ProductsRead, Security.Permissions.ProductsManage,
            Security.Permissions.InventoryRead, Security.Permissions.InventoryReceive, Security.Permissions.InventoryReceivePost,
            Security.Permissions.InventoryAdjust, Security.Permissions.InventoryAdjustApprove, Security.Permissions.InventoryTransfer,
            Security.Permissions.DispatchesRead, Security.Permissions.DispatchesCreate, Security.Permissions.DispatchesApprove,
            Security.Permissions.DispatchesPick, Security.Permissions.DispatchesShip, Security.Permissions.DispatchesDeliver,
            Security.Permissions.DispatchesCancel, Security.Permissions.ReportsRead,
        ]),
        new(WarehouseOperator, "Warehouse operator",
        [
            Security.Permissions.ProductsRead, Security.Permissions.InventoryRead, Security.Permissions.InventoryReceive,
            Security.Permissions.InventoryAdjust, Security.Permissions.DispatchesRead, Security.Permissions.DispatchesCreate,
            Security.Permissions.DispatchesPick, Security.Permissions.DispatchesDeliver,
        ]),
    ];
}
