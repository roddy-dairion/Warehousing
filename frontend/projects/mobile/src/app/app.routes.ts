import { Routes } from '@angular/router';
import { authGuard, guestGuard, Permissions, permissionGuard } from '@wh/shared';
import { warehouseSelectedGuard } from './core/warehouse-selected.guard';

export const routes: Routes = [
  { path: 'login', canMatch: [guestGuard], loadComponent: () => import('./pages/login/login.page').then((m) => m.LoginPage) },
  {
    path: 'select-warehouse',
    canMatch: [authGuard],
    loadComponent: () => import('./pages/select-warehouse/select-warehouse.page').then((m) => m.SelectWarehousePage),
  },
  {
    // Children are matched after the session is restored and a warehouse is selected.
    path: '',
    canMatch: [authGuard, warehouseSelectedGuard],
    children: [
      { path: '', loadComponent: () => import('./pages/home/home.page').then((m) => m.HomePage) },
      {
        path: 'scan',
        canMatch: [permissionGuard(Permissions.ProductsRead)],
        loadComponent: () => import('./pages/scan/scan.page').then((m) => m.ScanPage),
      },
      {
        path: 'products/:id',
        canMatch: [permissionGuard(Permissions.InventoryRead)],
        loadComponent: () => import('./pages/product-stock/product-stock.page').then((m) => m.ProductStockPage),
      },
      {
        path: 'dispatches',
        canMatch: [permissionGuard(Permissions.DispatchesRead)],
        loadComponent: () => import('./pages/dispatches/dispatch-list.page').then((m) => m.DispatchListPage),
      },
      {
        path: 'dispatches/:id',
        canMatch: [permissionGuard(Permissions.DispatchesRead)],
        loadComponent: () => import('./pages/dispatches/dispatch.page').then((m) => m.DispatchPage),
      },
      {
        path: 'receipts',
        canMatch: [permissionGuard(Permissions.InventoryReceive, Permissions.InventoryReceivePost)],
        loadComponent: () => import('./pages/receipts/receipt-list.page').then((m) => m.ReceiptListPage),
      },
      {
        path: 'receipts/:id',
        canMatch: [permissionGuard(Permissions.InventoryReceive, Permissions.InventoryReceivePost)],
        loadComponent: () => import('./pages/receipts/receipt.page').then((m) => m.ReceiptPage),
      },
      {
        path: 'activity',
        canMatch: [permissionGuard(Permissions.InventoryRead)],
        loadComponent: () => import('./pages/activity/activity.page').then((m) => m.ActivityPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
