/**
 * Permission codes. Must match the codes seeded by the API (Inventory.Domain `Permissions`).
 * The UI uses them only to show/hide things; the API enforces them.
 */
export const Permissions = {
  UsersManage: 'users.manage',
  SettingsManage: 'settings.manage',
  CustomersManage: 'customers.manage',
  WarehousesManage: 'warehouses.manage',
  ProductsRead: 'products.read',
  ProductsManage: 'products.manage',
  InventoryRead: 'inventory.read',
  InventoryReceive: 'inventory.receive',
  InventoryReceivePost: 'inventory.receive.post',
  InventoryAdjust: 'inventory.adjust',
  InventoryAdjustApprove: 'inventory.adjust.approve',
  InventoryTransfer: 'inventory.transfer',
  DispatchesRead: 'dispatches.read',
  DispatchesCreate: 'dispatches.create',
  DispatchesApprove: 'dispatches.approve',
  DispatchesPick: 'dispatches.pick',
  DispatchesShip: 'dispatches.ship',
  DispatchesDeliver: 'dispatches.deliver',
  DispatchesCancel: 'dispatches.cancel',
  PurchasingRead: 'purchasing.read',
  PurchasingManage: 'purchasing.manage',
  ReportsRead: 'reports.read',
  AuditRead: 'audit.read',
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];
