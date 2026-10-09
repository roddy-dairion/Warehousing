import { Routes } from '@angular/router';
import { authGuard, guestGuard } from '@wh/shared';
import { warehouseSelectedGuard } from './core/warehouse-selected.guard';

export const routes: Routes = [
  { path: 'login', canMatch: [guestGuard], loadComponent: () => import('./pages/login/login.page').then((m) => m.LoginPage) },
  {
    path: 'select-warehouse',
    canMatch: [authGuard],
    loadComponent: () => import('./pages/select-warehouse/select-warehouse.page').then((m) => m.SelectWarehousePage),
  },
  {
    path: '',
    canMatch: [authGuard, warehouseSelectedGuard],
    loadComponent: () => import('./pages/home/home.page').then((m) => m.HomePage),
  },
  { path: '**', redirectTo: '' },
];
