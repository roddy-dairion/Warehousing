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
import { ApiError, AuthService, DISPATCH_STATUSES, DispatchesApi, DispatchListItemDto, DispatchStatus, PageQuery, Permissions, WarehouseContextService } from '@wh/shared';
import { DocStatus } from '../../ui/doc-status';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { debounce, fromLazyEvent } from '../../ui/table-query';
import { qty } from '../../ui/format';

type View = 'open' | 'all' | DispatchStatus;

@Component({
  selector: 'wh-dispatch-list',
  imports: [DatePipe, FormsModule, RouterLink, TableModule, Button, InputText, IconField, InputIcon, Select, ToggleSwitch, DocStatus, LoadError, NeedWarehouse],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Dispatches</h1>
          <p>Approval reserves stock; shipping deducts it. Click a dispatch to pick, ship or record delivery.</p>
        </div>
        @if (warehouses.current() && canCreate) {
          <div class="page-actions"><p-button label="New dispatch" icon="pi pi-plus" routerLink="/dispatches/new" /></div>
        }
      </header>
      @if (!warehouses.current()) {
        <wh-need-warehouse />
      } @else {
        <div class="toolbar">
          <p-iconfield class="grow">
            <p-inputicon styleClass="pi pi-search" />
            <input pInputText type="search" placeholder="Number, customer or customer reference" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search dispatches" />
          </p-iconfield>
          <p-select [options]="views" optionLabel="label" optionValue="value" [(ngModel)]="view" (onChange)="apply()" ariaLabel="Status" />
          <span class="field-inline"><p-toggleswitch inputId="mine" [(ngModel)]="mine" (onChange)="apply()" /><label for="mine">Assigned to me</label></span>
        </div>
        @if (error(); as m) { <wh-load-error [message]="m" (retry)="load()" /> }
        <p-table #table [value]="rows()" [lazy]="true" (onLazyLoad)="onLazy($event)" [loading]="loading()" [paginator]="true" [rows]="25" [rowsPerPageOptions]="[25, 50, 100]"
          [totalRecords]="total()" dataKey="id" [rowHover]="true" [tableStyle]="{ 'min-width': '58rem' }" sortField="createdAt" [sortOrder]="-1"
          [showCurrentPageReport]="true" currentPageReportTemplate="{first}–{last} of {totalRecords}">
          <ng-template #header>
            <tr>
              <th pSortableColumn="number">Number <p-sortIcon field="number" /></th>
              <th>Customer</th>
              <th pSortableColumn="requestedDate">Requested <p-sortIcon field="requestedDate" /></th>
              <th class="num">Lines</th>
              <th class="num">Shipped / ordered</th>
              <th>Picker</th>
              <th pSortableColumn="status">Status <p-sortIcon field="status" /></th>
            </tr>
          </ng-template>
          <ng-template #body let-d>
            <tr class="clickable-row" (click)="open(d)" (keydown.enter)="open(d)" tabindex="0">
              <td class="code">{{ d.number }}@if (d.backorderOfNumber) { <div class="muted small">backorder of {{ d.backorderOfNumber }}</div> }</td>
              <td>{{ d.customerName }} <span class="muted">{{ d.customerReference }}</span>@if (d.customerOrderReference) { <div class="muted small">ref. {{ d.customerOrderReference }}</div> }</td>
              <td>{{ d.requestedDate | date: 'dd.MM.yyyy' }}</td>
              <td class="num">{{ d.lineCount }}</td>
              <td class="num">{{ qty(d.quantityShipped) }} / {{ qty(d.quantityRequested) }}</td>
              <td>{{ d.assignedToName ?? '—' }}</td>
              <td><wh-doc-status kind="dispatch" [status]="d.status" /></td>
            </tr>
          </ng-template>
          <ng-template #emptymessage><tr><td colspan="7" class="empty">No dispatches found.</td></tr></ng-template>
        </p-table>
      }
    </section>
  `,
  styles: `.small { font-size: 0.8125rem; }`,
})
export class DispatchList {
  private readonly api = inject(DispatchesApi);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canCreate = inject(AuthService).hasPermission(Permissions.DispatchesCreate);
  private readonly table = viewChild<Table>('table');
  protected readonly qty = qty;
  protected readonly rows = signal<DispatchListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly views: { value: View; label: string }[] = [
    { value: 'open', label: 'Open (not delivered or cancelled)' },
    ...DISPATCH_STATUSES.map((s) => ({ value: s.value as View, label: s.label })),
    { value: 'all', label: 'All' },
  ];
  protected search = '';
  protected mine = false;
  protected view: View = (inject(ActivatedRoute).snapshot.queryParamMap.get('status') as View | null) ?? 'open';
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
    void this.router.navigate([], { queryParams: { status: this.view === 'open' ? null : this.view }, replaceUrl: true });
    void this.load();
  }
  protected open(d: DispatchListItemDto): void {
    void this.router.navigate(['/dispatches', d.id]);
  }
  protected async load(): Promise<void> {
    const wh = this.warehouses.current();
    if (!wh) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list({
        ...this.query, warehouseId: wh.id, search: this.search || undefined, assignedToMe: this.mine || undefined,
        open: this.view === 'open' ? true : undefined,
        status: this.view !== 'open' && this.view !== 'all' ? this.view : undefined,
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
