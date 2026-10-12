import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard, Permissions } from '@wh/shared';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · HelloCherie Warehousing',
    canMatch: [guestGuard],
    loadComponent: () => import('./pages/login/login').then((m) => m.LoginPage),
  },
  {
    path: '',
    canMatch: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', title: 'Dashboard · HelloCherie Warehousing', loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.DashboardPage) },
      {
        path: 'dispatches',
        canMatch: [permissionGuard(Permissions.DispatchesRead)],
        children: [
          { path: '', title: 'Dispatches', loadComponent: () => import('./pages/dispatches/dispatch-list').then((m) => m.DispatchList) },
          { path: 'new', title: 'New dispatch', canMatch: [permissionGuard(Permissions.DispatchesCreate)], loadComponent: () => import('./pages/dispatches/dispatch-edit').then((m) => m.DispatchEdit) },
          { path: ':id/edit', title: 'Edit dispatch', canMatch: [permissionGuard(Permissions.DispatchesCreate)], loadComponent: () => import('./pages/dispatches/dispatch-edit').then((m) => m.DispatchEdit) },
          { path: ':id', title: 'Dispatch', loadComponent: () => import('./pages/dispatches/dispatch-detail').then((m) => m.DispatchDetail) },
        ],
      },
      {
        path: 'receipts',
        canMatch: [permissionGuard(Permissions.InventoryReceive, Permissions.InventoryReceivePost)],
        children: [
          { path: '', title: 'Goods receipts', loadComponent: () => import('./pages/receipts/receipt-list').then((m) => m.ReceiptList) },
          { path: ':id', title: 'Goods receipt', loadComponent: () => import('./pages/receipts/receipt-edit').then((m) => m.ReceiptEdit) },
        ],
      },
      { path: 'inventory', title: 'Stock', canMatch: [permissionGuard(Permissions.InventoryRead)], loadComponent: () => import('./pages/inventory/stock-page').then((m) => m.StockPage) },
      {
        path: 'adjustments',
        canMatch: [permissionGuard(Permissions.InventoryAdjust, Permissions.InventoryAdjustApprove)],
        children: [
          { path: '', title: 'Adjustments', loadComponent: () => import('./pages/adjustments/adjustment-list').then((m) => m.AdjustmentList) },
          { path: ':id', title: 'Adjustment', loadComponent: () => import('./pages/adjustments/adjustment-edit').then((m) => m.AdjustmentEdit) },
        ],
      },
      { path: 'transfers', title: 'Bin moves', canMatch: [permissionGuard(Permissions.InventoryTransfer)], loadComponent: () => import('./pages/transfers/transfer-list').then((m) => m.TransferList) },
      {
        path: 'products',
        canMatch: [permissionGuard(Permissions.ProductsRead)],
        children: [
          { path: '', title: 'Products', loadComponent: () => import('./pages/products/product-list').then((m) => m.ProductList) },
          { path: 'new', title: 'New product', canMatch: [permissionGuard(Permissions.ProductsManage)], loadComponent: () => import('./pages/products/product-edit').then((m) => m.ProductEdit) },
          { path: ':id', title: 'Product', loadComponent: () => import('./pages/products/product-edit').then((m) => m.ProductEdit) },
        ],
      },
      {
        path: 'customers',
        canMatch: [permissionGuard(Permissions.CustomersManage, Permissions.DispatchesRead, Permissions.DispatchesCreate)],
        children: [
          { path: '', title: 'Customers', loadComponent: () => import('./pages/customers/customer-list').then((m) => m.CustomerList) },
          { path: 'new', title: 'New customer', canMatch: [permissionGuard(Permissions.CustomersManage)], loadComponent: () => import('./pages/customers/customer-edit').then((m) => m.CustomerEdit) },
          { path: ':id', title: 'Customer', loadComponent: () => import('./pages/customers/customer-edit').then((m) => m.CustomerEdit) },
        ],
      },
      {
        path: 'warehouses',
        canMatch: [permissionGuard(Permissions.WarehousesManage, Permissions.InventoryRead)],
        children: [
          { path: '', title: 'Warehouses', loadComponent: () => import('./pages/warehouses/warehouse-list').then((m) => m.WarehouseList) },
          { path: ':id', title: 'Warehouse', loadComponent: () => import('./pages/warehouses/warehouse-detail').then((m) => m.WarehouseDetail) },
        ],
      },
      { path: 'catalog', title: 'Categories and units', canMatch: [permissionGuard(Permissions.ProductsRead)], loadComponent: () => import('./pages/catalog/catalog-page').then((m) => m.CatalogPage) },
      { path: 'users', title: 'Users', canMatch: [permissionGuard(Permissions.UsersManage)], loadComponent: () => import('./pages/users/user-list').then((m) => m.UserList) },
      {
        path: 'import',
        title: 'Import',
        canMatch: [permissionGuard(Permissions.ProductsManage, Permissions.CustomersManage, Permissions.WarehousesManage)],
        loadComponent: () => import('./pages/import/import-page').then((m) => m.ImportPage),
      },
      { path: 'reports', title: 'Reports', canMatch: [permissionGuard(Permissions.ReportsRead)], loadComponent: () => import('./pages/reports/reports-page').then((m) => m.ReportsPage) },
      {
        path: 'settings',
        canMatch: [permissionGuard(Permissions.SettingsManage)],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'company' },
          { path: 'company', title: 'Company and email', loadComponent: () => import('./pages/settings/company-settings').then((m) => m.CompanySettings) },
          { path: 'templates', title: 'Document templates', loadComponent: () => import('./pages/settings/template-editor').then((m) => m.TemplateEditor) },
        ],
      },
      { path: 'audit', title: 'Audit log', canMatch: [permissionGuard(Permissions.AuditRead)], loadComponent: () => import('./pages/audit/audit-page').then((m) => m.AuditPage) },
      { path: 'forbidden', title: 'No access', loadComponent: () => import('./pages/forbidden/forbidden').then((m) => m.ForbiddenPage) },
    ],
  },
  { path: '**', title: 'Not found', loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFoundPage) },
];
