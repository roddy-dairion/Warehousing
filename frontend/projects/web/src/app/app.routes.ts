import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard, Permissions } from '@wh/shared';

const placeholder = () => import('./pages/placeholder/placeholder').then((m) => m.PlaceholderPage);

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
      { path: 'dispatches', title: 'Dispatches', canMatch: [permissionGuard(Permissions.DispatchesRead)], loadComponent: placeholder, data: { title: 'Dispatches', phase: 6 } },
      { path: 'receipts', title: 'Goods receipts', canMatch: [permissionGuard(Permissions.InventoryReceive)], loadComponent: placeholder, data: { title: 'Goods receipts', phase: 6 } },
      { path: 'inventory', title: 'Stock', canMatch: [permissionGuard(Permissions.InventoryRead)], loadComponent: placeholder, data: { title: 'Stock', phase: 6 } },
      { path: 'adjustments', title: 'Adjustments', canMatch: [permissionGuard(Permissions.InventoryAdjust, Permissions.InventoryAdjustApprove)], loadComponent: placeholder, data: { title: 'Stock adjustments', phase: 6 } },
      { path: 'transfers', title: 'Bin moves', canMatch: [permissionGuard(Permissions.InventoryTransfer)], loadComponent: placeholder, data: { title: 'Bin moves', phase: 6 } },
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
      { path: 'reports', title: 'Reports', canMatch: [permissionGuard(Permissions.ReportsRead)], loadComponent: placeholder, data: { title: 'Reports', phase: 6 } },
      { path: 'audit', title: 'Audit log', canMatch: [permissionGuard(Permissions.AuditRead)], loadComponent: placeholder, data: { title: 'Audit log', phase: 6 } },
      { path: 'forbidden', title: 'No access', loadComponent: () => import('./pages/forbidden/forbidden').then((m) => m.ForbiddenPage) },
    ],
  },
  { path: '**', title: 'Not found', loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFoundPage) },
];
