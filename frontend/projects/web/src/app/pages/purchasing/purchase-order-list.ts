import { Component, effect, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { ProgressBar } from 'primeng/progressbar';
import { Tag } from 'primeng/tag';
import {
  ApiError, AuthService, PageQuery, Permissions, PurchaseOrderListItemDto, PurchaseOrdersApi, PurchaseOrderStatus, WarehouseContextService,
} from '@wh/shared';
import { DocStatus, PURCHASE_ORDER_STATUSES } from '../../ui/doc-status';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { debounce, fromLazyEvent } from '../../ui/table-query';

@Component({
  selector: 'wh-purchase-order-list',
  imports: [DatePipe, FormsModule, RouterLink, TableModule, Button, InputText, IconField, InputIcon, Select, ToggleSwitch, ProgressBar, Tag, DocStatus, LoadError, NeedWarehouse],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Purchase orders</h1>
          <p>What was ordered, from whom, and what has arrived. Ordering does not change stock: receiving against the order does.</p>
        </div>
        @if (warehouses.current() && canManage) {
          <div class="page-actions">
            <p-button label="Reorder suggestions" icon="pi pi-lightbulb" severity="secondary" [outlined]="true" routerLink="/purchasing/reorder" />
            <p-button label="New order" icon="pi pi-plus" routerLink="/purchasing/orders/new" data-testid="new-order" />
          </div>
        }
      </header>
      @if (!warehouses.current()) {
        <wh-need-warehouse />
      } @else {
        <div class="toolbar">
          <p-iconfield class="grow">
            <p-inputicon styleClass="pi pi-search" />
            <input pInputText type="search" placeholder="Number, supplier or supplier reference" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search purchase orders" />
          </p-iconfield>
          <p-select [options]="statuses" optionLabel="label" optionValue="value" [(ngModel)]="status" (onChange)="apply()" placeholder="All statuses" [showClear]="true"
            ariaLabel="Status" [disabled]="openOnly" />
          <label class="toggle"><p-toggleswitch [(ngModel)]="openOnly" (onChange)="apply()" inputId="openOnly" /> <span>Open only</span></label>
        </div>
        @if (error(); as m) { <wh-load-error [message]="m" (retry)="load()" /> }
        <p-table #table [value]="rows()" [lazy]="true" (onLazyLoad)="onLazy($event)" [loading]="loading()" [paginator]="true" [rows]="25" [rowsPerPageOptions]="[25, 50, 100]"
          [totalRecords]="total()" dataKey="id" [rowHover]="true" [tableStyle]="{ 'min-width': '56rem' }" sortField="createdAt" [sortOrder]="-1"
          [showCurrentPageReport]="true" currentPageReportTemplate="{first}–{last} of {totalRecords}">
          <ng-template #header>
            <tr>
              <th pSortableColumn="number">Number <p-sortIcon field="number" /></th>
              <th>Supplier</th>
              <th pSortableColumn="orderDate">Ordered <p-sortIcon field="orderDate" /></th>
              <th pSortableColumn="expectedDate">Expected <p-sortIcon field="expectedDate" /></th>
              <th class="num">Lines</th>
              <th style="width: 9rem">Received</th>
              <th pSortableColumn="status">Status <p-sortIcon field="status" /></th>
            </tr>
          </ng-template>
          <ng-template #body let-o>
            <tr class="clickable-row" (click)="open(o)" (keydown.enter)="open(o)" tabindex="0">
              <td class="code">{{ o.number }}</td>
              <td>{{ o.supplierName }}@if (o.supplierReference) { <span class="muted"> · {{ o.supplierReference }}</span> }</td>
              <td>{{ o.orderDate | date: 'dd.MM.yyyy' }}</td>
              <td>
                {{ o.expectedDate ? (o.expectedDate | date: 'dd.MM.yyyy') : '—' }}
                @if (o.isOverdue) { <p-tag value="Overdue" severity="danger" /> }
              </td>
              <td class="num">{{ o.lineCount }}</td>
              <td><p-progressbar [value]="o.receivedPercent" [showValue]="false" [style]="{ height: '0.5rem' }" [attr.aria-label]="o.receivedPercent + '% received'" />
                <span class="muted small">{{ o.receivedPercent }}%</span></td>
              <td><wh-doc-status kind="purchaseOrder" [status]="o.status" /></td>
            </tr>
          </ng-template>
          <ng-template #emptymessage><tr><td colspan="7" class="empty">No purchase orders found.</td></tr></ng-template>
        </p-table>
      }
    </section>
  `,
  styles: `
    .toggle { display: inline-flex; align-items: center; gap: 0.5rem; white-space: nowrap; }
    .small { font-size: 0.8rem; }
  `,
})
export class PurchaseOrderList {
  private readonly api = inject(PurchaseOrdersApi);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.PurchasingManage);
  private readonly table = viewChild<Table>('table');
  protected readonly rows = signal<PurchaseOrderListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statuses = PURCHASE_ORDER_STATUSES;
  protected search = '';
  protected status: PurchaseOrderStatus | null = (inject(ActivatedRoute).snapshot.queryParamMap.get('status') as PurchaseOrderStatus | null) ?? null;
  protected openOnly = inject(ActivatedRoute).snapshot.queryParamMap.get('open') === '1';
  private query: PageQuery = { page: 1, pageSize: 25 };

  constructor() {
    effect(() => {
      if (this.warehouses.current()) this.apply();
    });
  }

  protected onLazy(e: TableLazyLoadEvent): void {
    this.query = fromLazyEvent(e, this.query);
    void this.load();
  }
  protected readonly onSearch = debounce(() => this.apply());
  protected apply(): void {
    this.query = { ...this.query, page: 1 };
    const t = this.table();
    if (t) t.first = 0;
    void this.load();
  }
  protected open(o: PurchaseOrderListItemDto): void {
    void this.router.navigate(['/purchasing/orders', o.id]);
  }
  protected async load(): Promise<void> {
    const wh = this.warehouses.current();
    if (!wh) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list({
        ...this.query, warehouseId: wh.id, search: this.search || undefined,
        status: this.openOnly ? undefined : (this.status ?? undefined), openOnly: this.openOnly || undefined,
      });
      this.rows.set(page.items);
      this.total.set(page.totalCount);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }
}
