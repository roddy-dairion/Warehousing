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
import { ADJUSTMENT_REASONS, AdjustmentListItemDto, AdjustmentsApi, AdjustmentStatus, ApiError, AuthService, PageQuery, Permissions, WarehouseContextService } from '@wh/shared';
import { DocStatus } from '../../ui/doc-status';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { debounce, fromLazyEvent } from '../../ui/table-query';

const STATUSES: { value: AdjustmentStatus; label: string }[] = [
  { value: 'draft', label: 'Draft' },
  { value: 'pendingApproval', label: 'Awaiting approval' },
  { value: 'posted', label: 'Posted' },
  { value: 'rejected', label: 'Rejected' },
  { value: 'cancelled', label: 'Cancelled' },
];

@Component({
  selector: 'wh-adjustment-list',
  imports: [DatePipe, FormsModule, RouterLink, TableModule, Button, InputText, IconField, InputIcon, Select, DocStatus, LoadError, NeedWarehouse],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Stock adjustments</h1>
          <p>Corrections to stock (counts, damage, losses). Operators request them; a manager approves, which changes stock.</p>
        </div>
        @if (warehouses.current() && canRequest) {
          <div class="page-actions"><p-button label="New adjustment" icon="pi pi-plus" routerLink="/adjustments/new" /></div>
        }
      </header>
      @if (!warehouses.current()) {
        <wh-need-warehouse />
      } @else {
        <div class="toolbar">
          <p-iconfield class="grow">
            <p-inputicon styleClass="pi pi-search" />
            <input pInputText type="search" placeholder="Number or notes" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search adjustments" />
          </p-iconfield>
          <p-select [options]="statuses" optionLabel="label" optionValue="value" [(ngModel)]="status" (onChange)="apply()" placeholder="All statuses" [showClear]="true" ariaLabel="Status" />
        </div>
        @if (error(); as m) { <wh-load-error [message]="m" (retry)="load()" /> }
        <p-table #table [value]="rows()" [lazy]="true" (onLazyLoad)="onLazy($event)" [loading]="loading()" [paginator]="true" [rows]="25" [rowsPerPageOptions]="[25, 50, 100]"
          [totalRecords]="total()" dataKey="id" [rowHover]="true" [tableStyle]="{ 'min-width': '48rem' }" sortField="createdAt" [sortOrder]="-1"
          [showCurrentPageReport]="true" currentPageReportTemplate="{first}–{last} of {totalRecords}">
          <ng-template #header>
            <tr>
              <th pSortableColumn="number">Number <p-sortIcon field="number" /></th>
              <th>Reason</th>
              <th class="num">Lines</th>
              <th pSortableColumn="createdAt">Created <p-sortIcon field="createdAt" /></th>
              <th pSortableColumn="status">Status <p-sortIcon field="status" /></th>
            </tr>
          </ng-template>
          <ng-template #body let-a>
            <tr class="clickable-row" (click)="open(a)" (keydown.enter)="open(a)" tabindex="0">
              <td class="code">{{ a.number }}</td>
              <td>{{ reasonLabel(a.reason) }}</td>
              <td class="num">{{ a.lineCount }}</td>
              <td>{{ a.createdAt | date: 'dd.MM.yyyy HH:mm' }}</td>
              <td><wh-doc-status kind="adjustment" [status]="a.status" /></td>
            </tr>
          </ng-template>
          <ng-template #emptymessage><tr><td colspan="5" class="empty">No adjustments found.</td></tr></ng-template>
        </p-table>
      }
    </section>
  `,
})
export class AdjustmentList {
  private readonly api = inject(AdjustmentsApi);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canRequest = inject(AuthService).hasPermission(Permissions.InventoryAdjust);
  private readonly table = viewChild<Table>('table');
  protected readonly rows = signal<AdjustmentListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statuses = STATUSES;
  protected search = '';
  protected status: AdjustmentStatus | null = (inject(ActivatedRoute).snapshot.queryParamMap.get('status') as AdjustmentStatus | null) ?? null;
  private query: PageQuery = { page: 1, pageSize: 25 };

  constructor() {
    effect(() => {
      if (this.warehouses.current()) this.apply();
    });
  }

  protected reasonLabel(r: string): string {
    return ADJUSTMENT_REASONS.find((x) => x.value === r)?.label ?? r;
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
  protected open(a: AdjustmentListItemDto): void {
    void this.router.navigate(['/adjustments', a.id]);
  }
  protected async load(): Promise<void> {
    const wh = this.warehouses.current();
    if (!wh) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list({ ...this.query, warehouseId: wh.id, search: this.search || undefined, status: this.status ?? undefined });
      this.rows.set(page.items);
      this.total.set(page.totalCount);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }
}
