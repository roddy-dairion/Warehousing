import { Component, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Skeleton } from 'primeng/skeleton';
import { Button } from 'primeng/button';
import { ApiError, AuthService, MOVEMENT_TYPE_LABELS, MovementType, ReportingApi, WarehouseContextService, WarehouseDashboardDto } from '@wh/shared';
import { DocStatus } from '../../ui/doc-status';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { qty } from '../../ui/format';

@Component({
  selector: 'wh-dashboard',
  imports: [DatePipe, RouterLink, Skeleton, Button, DocStatus, LoadError, NeedWarehouse],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardPage {
  private readonly auth = inject(AuthService);
  private readonly api = inject(ReportingApi);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly data = signal<WarehouseDashboardDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected movementLabel(type: string): string {
    return MOVEMENT_TYPE_LABELS[type as MovementType] ?? type;
  }
  protected readonly qty = qty;

  constructor() {
    effect(() => {
      const wh = this.warehouses.current();
      if (wh) void this.load(wh.id);
      else this.data.set(null);
    });
  }

  protected firstName(): string {
    return this.auth.user()?.displayName.split(' ')[0] ?? '';
  }

  protected reload(): void {
    const wh = this.warehouses.current();
    if (wh) void this.load(wh.id);
  }

  private async load(warehouseId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await this.api.dashboard(warehouseId));
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }
}
