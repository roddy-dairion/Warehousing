import { Permission, Permissions } from '@wh/shared';

export interface NavItem {
  label: string;
  icon: string;
  route: string;
  /** Shown when the user holds any of these; empty = every signed-in user. */
  permissions: Permission[];
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

export const NAVIGATION: NavGroup[] = [
  {
    label: 'Operations',
    items: [
      { label: 'Dashboard', icon: 'pi pi-home', route: '/', permissions: [] },
      { label: 'Dispatches', icon: 'pi pi-truck', route: '/dispatches', permissions: [Permissions.DispatchesRead] },
      { label: 'Goods receipts', icon: 'pi pi-download', route: '/receipts', permissions: [Permissions.InventoryReceive] },
      { label: 'Stock', icon: 'pi pi-box', route: '/inventory', permissions: [Permissions.InventoryRead] },
      { label: 'Adjustments', icon: 'pi pi-sliders-h', route: '/adjustments', permissions: [Permissions.InventoryAdjust, Permissions.InventoryAdjustApprove] },
      { label: 'Bin moves', icon: 'pi pi-arrow-right-arrow-left', route: '/transfers', permissions: [Permissions.InventoryTransfer] },
    ],
  },
  {
    label: 'Master data',
    items: [
      { label: 'Products', icon: 'pi pi-tags', route: '/products', permissions: [Permissions.ProductsRead] },
      { label: 'Customers', icon: 'pi pi-building', route: '/customers', permissions: [Permissions.CustomersManage, Permissions.DispatchesCreate] },
      { label: 'Categories and units', icon: 'pi pi-sitemap', route: '/catalog', permissions: [Permissions.ProductsManage] },
      { label: 'Warehouses and bins', icon: 'pi pi-warehouse', route: '/warehouses', permissions: [Permissions.WarehousesManage] },
      { label: 'Import', icon: 'pi pi-upload', route: '/import', permissions: [Permissions.ProductsManage, Permissions.CustomersManage, Permissions.WarehousesManage] },
    ],
  },
  {
    label: 'Administration',
    items: [
      { label: 'Users', icon: 'pi pi-users', route: '/users', permissions: [Permissions.UsersManage] },
      { label: 'Reports', icon: 'pi pi-chart-bar', route: '/reports', permissions: [Permissions.ReportsRead] },
      { label: 'Audit log', icon: 'pi pi-history', route: '/audit', permissions: [Permissions.AuditRead] },
    ],
  },
];

/** Groups and items the user can see, dropping empty groups. */
export function visibleNavigation(has: (p: Permission) => boolean): NavGroup[] {
  return NAVIGATION.map((g) => ({
    ...g,
    items: g.items.filter((i) => i.permissions.length === 0 || i.permissions.some(has)),
  })).filter((g) => g.items.length > 0);
}
