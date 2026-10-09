namespace Inventory.Domain.Security;

/// <summary>
/// Permission codes. Keep in sync with <c>frontend/projects/shared/src/lib/auth/permissions.ts</c>
/// (a unit test compares the two lists).
/// </summary>
public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";
    public const string CustomersManage = "customers.manage";
    public const string WarehousesManage = "warehouses.manage";
    public const string ProductsRead = "products.read";
    public const string ProductsManage = "products.manage";
    public const string InventoryRead = "inventory.read";
    public const string InventoryReceive = "inventory.receive";
    public const string InventoryReceivePost = "inventory.receive.post";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryAdjustApprove = "inventory.adjust.approve";
    public const string InventoryTransfer = "inventory.transfer";
    public const string DispatchesRead = "dispatches.read";
    public const string DispatchesCreate = "dispatches.create";
    public const string DispatchesApprove = "dispatches.approve";
    public const string DispatchesPick = "dispatches.pick";
    public const string DispatchesShip = "dispatches.ship";
    public const string DispatchesDeliver = "dispatches.deliver";
    public const string DispatchesCancel = "dispatches.cancel";
    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audit.read";

    /// <summary>Every permission with a human-readable description (seeded into the database).</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [UsersManage] = "Manage users, roles and warehouse access",
        [SettingsManage] = "Change system settings",
        [CustomersManage] = "Create and edit customers and delivery addresses",
        [WarehousesManage] = "Create and edit warehouses and bin locations",
        [ProductsRead] = "View products, units and barcodes",
        [ProductsManage] = "Create and edit products, units and barcodes",
        [InventoryRead] = "View stock balances and movements",
        [InventoryReceive] = "Create goods receipts and record received quantities",
        [InventoryReceivePost] = "Post or reverse goods receipts (changes stock)",
        [InventoryAdjust] = "Request stock adjustments",
        [InventoryAdjustApprove] = "Approve and post stock adjustments",
        [InventoryTransfer] = "Move stock between bins or warehouses",
        [DispatchesRead] = "View dispatches",
        [DispatchesCreate] = "Create, edit and submit dispatches",
        [DispatchesApprove] = "Approve dispatches (reserves stock)",
        [DispatchesPick] = "Pick dispatches",
        [DispatchesShip] = "Confirm shipment (deducts stock)",
        [DispatchesDeliver] = "Record delivery and proof of delivery",
        [DispatchesCancel] = "Cancel dispatches",
        [ReportsRead] = "View reports",
        [AuditRead] = "View the audit log",
    };
}
