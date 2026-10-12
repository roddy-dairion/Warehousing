import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { InputNumber } from 'primeng/inputnumber';
import { Tag } from 'primeng/tag';
import { Button } from 'primeng/button';
import { DatePicker } from 'primeng/datepicker';
import {
  ApiError, AuthService, BalanceDto, ExpiringLotDto, InventoryApi, MOVEMENT_TYPE_LABELS, MovementDto, MovementType, PageQuery, Permissions,
  ReconciliationResult, StockSummaryDto, WarehouseContextService,
} from '@wh/shared';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { debounce, fromLazyEvent } from '../../ui/table-query';
import { qty, toIsoDate } from '../../ui/format';
import { ProductStockDrawer } from './product-stock-drawer';

type TabKey = 'summary' | 'balances' | 'movements' | 'expiring' | 'reconciliation';

/** Stock overview: per product, per bin/lot, the ledger, expiry alerts and the reconciliation check. */
@Component({
  selector: 'wh-stock-page',
  imports: [
    DatePipe, FormsModule, TableModule, Tabs, TabList, Tab, InputText, IconField, InputIcon, Select, ToggleSwitch, InputNumber, Tag, Button, DatePicker,
    LoadError, NeedWarehouse, ProductStockDrawer,
  ],
  templateUrl: './stock-page.html',
  styleUrl: './stock-page.scss',
})
export class StockPage {
  private readonly api = inject(InventoryApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canReconcile = this.auth.hasPermission(Permissions.InventoryAdjustApprove) || this.auth.hasPermission(Permissions.ReportsRead);
  protected readonly qty = qty;
  protected movementLabel(type: string): string {
    return MOVEMENT_TYPE_LABELS[type as MovementType] ?? type;
  }
  protected readonly movementTypes = (Object.keys(MOVEMENT_TYPE_LABELS) as MovementType[]).map((value) => ({ value, label: MOVEMENT_TYPE_LABELS[value] }));

  protected readonly tab = signal<TabKey>('summary');
  protected readonly warehouseId = computed(() => this.warehouses.current()?.id ?? null);
  protected readonly drawerProductId = signal<string | null>(null);

  // Shared list state per tab.
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly total = signal(0);
  protected search = '';

  protected readonly summary = signal<StockSummaryDto[]>([]);
  protected lowOnly = false;
  protected withStockOnly = false;
  private summaryQuery: PageQuery = { page: 1, pageSize: 50, sortBy: 'sku' };

  protected readonly balances = signal<BalanceDto[]>([]);
  private balanceQuery: PageQuery = { page: 1, pageSize: 50, sortBy: 'sku' };

  protected readonly movements = signal<MovementDto[]>([]);
  protected movementType: MovementType | null = null;
  protected movementRange: Date[] | null = null;
  private movementQuery: PageQuery = { page: 1, pageSize: 50 };

  protected readonly expiring = signal<ExpiringLotDto[]>([]);
  protected expiryDays = 30;

  protected readonly reconciliation = signal<ReconciliationResult | null>(null);

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    const t = params.get('tab') as TabKey | null;
    if (t && ['summary', 'balances', 'movements', 'expiring', 'reconciliation'].includes(t)) this.tab.set(t);
    this.lowOnly = params.get('low') === '1';
    effect(() => {
      // Reload when the warehouse changes.
      if (this.warehouseId()) this.reload();
    });
  }

  protected selectTab(value: string | number | undefined): void {
    const t = value as TabKey;
    this.tab.set(t);
    this.search = '';
    void this.router.navigate([], { queryParams: { tab: t }, replaceUrl: true });
    this.reload();
  }

  protected readonly onSearch = debounce(() => this.reload());

  protected reload(): void {
    switch (this.tab()) {
      case 'summary':
        this.summaryQuery = { ...this.summaryQuery, page: 1 };
        void this.loadSummary();
        break;
      case 'balances':
        this.balanceQuery = { ...this.balanceQuery, page: 1 };
        void this.loadBalances();
        break;
      case 'movements':
        this.movementQuery = { ...this.movementQuery, page: 1 };
        void this.loadMovements();
        break;
      case 'expiring':
        void this.run(async (wh) => this.expiring.set(await this.api.expiring(wh, this.expiryDays)));
        break;
      case 'reconciliation':
        this.reconciliation.set(null);
        break;
    }
  }

  protected onSummaryLazy(e: TableLazyLoadEvent): void {
    this.summaryQuery = fromLazyEvent(e, this.summaryQuery);
    void this.loadSummary();
  }
  protected onBalancesLazy(e: TableLazyLoadEvent): void {
    this.balanceQuery = fromLazyEvent(e, this.balanceQuery);
    void this.loadBalances();
  }
  protected onMovementsLazy(e: TableLazyLoadEvent): void {
    this.movementQuery = fromLazyEvent(e, this.movementQuery);
    void this.loadMovements();
  }

  protected async runReconciliation(): Promise<void> {
    await this.run(async (wh) => this.reconciliation.set(await this.api.reconciliation(wh)));
  }

  protected openProduct(productId: string): void {
    this.drawerProductId.set(productId);
  }

  private loadSummary(): Promise<void> {
    return this.run(async (wh) => {
      const page = await this.api.summary({ ...this.summaryQuery, warehouseId: wh, search: this.search || undefined, lowStockOnly: this.lowOnly || undefined, onlyWithStock: this.withStockOnly || undefined });
      this.summary.set(page.items);
      this.total.set(page.totalCount);
    });
  }

  private loadBalances(): Promise<void> {
    return this.run(async (wh) => {
      const page = await this.api.balances({ ...this.balanceQuery, warehouseId: wh, search: this.search || undefined });
      this.balances.set(page.items);
      this.total.set(page.totalCount);
    });
  }

  private loadMovements(): Promise<void> {
    return this.run(async (wh) => {
      const [start, end] = this.movementRange ?? [];
      const to = end ?? start;
      const page = await this.api.movements({
        ...this.movementQuery, warehouseId: wh, search: this.search || undefined, type: this.movementType ?? undefined,
        from: start ? new Date(start.getFullYear(), start.getMonth(), start.getDate()).toISOString() : undefined,
        to: to ? new Date(to.getFullYear(), to.getMonth(), to.getDate() + 1).toISOString() : undefined,
      });
      this.movements.set(page.items);
      this.total.set(page.totalCount);
    });
  }

  private async run(action: (warehouseId: string) => Promise<void>): Promise<void> {
    const wh = this.warehouseId();
    if (!wh) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      await action(wh);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected isoDate = toIsoDate;
}
