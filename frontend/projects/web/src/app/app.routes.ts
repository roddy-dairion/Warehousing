import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard, Permissions } from '@wh/shared';

const placeholder = () => import('./pages/placeholder/placeholder').then((m) => m.PlaceholderPage);

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · Warehousing',
    canMatch: [guestGuard],
    loadComponent: () => import('./pages/login/login').then((m) => m.LoginPage),
  },
  {
    path: '',
    canMatch: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', title: 'Dashboard · Warehousing', loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.DashboardPage) },
      { path: 'dispatches', title: 'Dispatches', canMatch: [permissionGuard(Permissions.DispatchesRead)], loadComponent: placeholder, data: { title: 'Dispatches', phase: 6 } },
      { path: 'receipts', title: 'Goods receipts', canMatch: [permissionGuard(Permissions.InventoryReceive)], loadComponent: placeholder, data: { title: 'Goods receipts', phase: 6 } },
      { path: 'inventory', title: 'Stock', canMatch: [permissionGuard(Permissions.InventoryRead)], loadComponent: placeholder, data: { title: 'Stock', phase: 6 } },
      { path: 'adjustments', title: 'Adjustments', canMatch: [permissionGuard(Permissions.InventoryAdjust, Permissions.InventoryAdjustApprove)], loadComponent: placeholder, data: { title: 'Stock adjustments', phase: 6 } },
      { path: 'transfers', title: 'Bin moves', canMatch: [permissionGuard(Permissions.InventoryTransfer)], loadComponent: placeholder, data: { title: 'Bin moves', phase: 6 } },
      { path: 'products', title: 'Products', canMatch: [permissionGuard(Permissions.ProductsRead)], loadComponent: placeholder, data: { title: 'Products', phase: 3 } },
      { path: 'customers', title: 'Customers', canMatch: [permissionGuard(Permissions.CustomersManage, Permissions.DispatchesCreate)], loadComponent: placeholder, data: { title: 'Customers', phase: 3 } },
      { path: 'warehouses', title: 'Warehouses', canMatch: [permissionGuard(Permissions.WarehousesManage)], loadComponent: placeholder, data: { title: 'Warehouses and bins', phase: 3 } },
      { path: 'users', title: 'Users', canMatch: [permissionGuard(Permissions.UsersManage)], loadComponent: placeholder, data: { title: 'Users and access', phase: 3 } },
      { path: 'reports', title: 'Reports', canMatch: [permissionGuard(Permissions.ReportsRead)], loadComponent: placeholder, data: { title: 'Reports', phase: 6 } },
      { path: 'audit', title: 'Audit log', canMatch: [permissionGuard(Permissions.AuditRead)], loadComponent: placeholder, data: { title: 'Audit log', phase: 6 } },
      { path: 'forbidden', title: 'No access', loadComponent: () => import('./pages/forbidden/forbidden').then((m) => m.ForbiddenPage) },
    ],
  },
  { path: '**', title: 'Not found', loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFoundPage) },
];
