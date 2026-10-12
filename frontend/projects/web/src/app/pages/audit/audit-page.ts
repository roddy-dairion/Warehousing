import { Component, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { ApiError, AuditEntryDto, PageQuery, ReportingApi } from '@wh/shared';
import { LoadError } from '../../ui/load-error';
import { debounce, fromLazyEvent } from '../../ui/table-query';

const AREAS = [
  { value: 'dispatch.', label: 'Dispatches' },
  { value: 'receipt.', label: 'Receipts' },
  { value: 'adjustment.', label: 'Adjustments' },
  { value: 'transfer.', label: 'Bin moves' },
  { value: 'lot.', label: 'Lots' },
  { value: 'product.', label: 'Products' },
  { value: 'customer.', label: 'Customers' },
  { value: 'warehouse.', label: 'Warehouses' },
  { value: 'location.', label: 'Bins' },
  { value: 'user.', label: 'Users' },
  { value: 'auth.', label: 'Sign-ins' },
  { value: 'template.', label: 'Templates' },
  { value: 'company.', label: 'Company settings' },
  { value: 'delivery_note.', label: 'Delivery-note emails' },
];

/** The audit log: who did what and when. */
@Component({
  selector: 'wh-audit-page',
  imports: [DatePipe, FormsModule, TableModule, InputText, IconField, InputIcon, Select, DatePicker, LoadError],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Audit log</h1>
          <p>Every stock, dispatch, master-data and access change, newest first. Entries cannot be edited.</p>
        </div>
      </header>
      <div class="toolbar">
        <p-iconfield class="grow">
          <p-inputicon styleClass="pi pi-search" />
          <input pInputText type="search" placeholder="Action, document number or details" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search audit log" />
        </p-iconfield>
        <p-select [options]="areas" optionLabel="label" optionValue="value" [(ngModel)]="area" (onChange)="apply()" placeholder="All areas" [showClear]="true" ariaLabel="Area" />
        <p-datepicker [(ngModel)]="range" selectionMode="range" [readonlyInput]="true" dateFormat="dd.mm.yy" placeholder="Any date" [showClear]="true"
          (onSelect)="apply()" (onClear)="apply()" appendTo="body" ariaLabel="Date range" />
      </div>
      @if (error(); as m) { <wh-load-error [message]="m" (retry)="load()" /> }
      <p-table #table [value]="rows()" [lazy]="true" (onLazyLoad)="onLazy($event)" [loading]="loading()" [paginator]="true" [rows]="50" [rowsPerPageOptions]="[50, 100, 200]"
        [totalRecords]="total()" dataKey="id" [rowHover]="true" [tableStyle]="{ 'min-width': '60rem' }" [expandedRowKeys]="expanded"
        [showCurrentPageReport]="true" currentPageReportTemplate="{first}–{last} of {totalRecords}">
        <ng-template #header><tr><th>When</th><th>Action</th><th>User</th><th>Warehouse</th><th>Record</th><th></th></tr></ng-template>
        <ng-template #body let-a let-expanded="expanded">
          <tr>
            <td class="nowrap">{{ a.occurredAt | date: 'dd.MM.yyyy HH:mm:ss' }}</td>
            <td class="code">{{ a.action }}</td>
            <td>{{ a.userName ?? '—' }}</td>
            <td>{{ a.warehouseCode ?? '' }}</td>
            <td class="muted">{{ a.entityType }} {{ summary(a) }}</td>
            <td>@if (a.data) { <button type="button" class="link" [pRowToggler]="a">{{ expanded ? 'Hide' : 'Details' }}</button> }</td>
          </tr>
        </ng-template>
        <ng-template #expandedrow let-a>
          <tr><td colspan="6"><pre>{{ pretty(a.data) }}</pre>@if (a.ipAddress) { <span class="muted">IP {{ a.ipAddress }}</span> }</td></tr>
        </ng-template>
        <ng-template #emptymessage><tr><td colspan="6" class="empty">No entries match these filters.</td></tr></ng-template>
      </p-table>
    </section>
  `,
  styles: `
    .nowrap { white-space: nowrap; }
    pre { margin: 0; white-space: pre-wrap; font-size: 0.8125rem; background: var(--wh-concrete); padding: 0.5rem; border-radius: 4px; }
    .link { background: none; border: 0; color: var(--p-primary-color); cursor: pointer; padding: 0; }
  `,
})
export class AuditPage {
  private readonly api = inject(ReportingApi);
  private readonly table = viewChild<Table>('table');
  protected readonly rows = signal<AuditEntryDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly areas = AREAS;
  protected expanded: Record<string, boolean> = {};
  protected search = '';
  protected area: string | null = null;
  protected range: Date[] | null = null;
  private query: PageQuery = { page: 1, pageSize: 50 };

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

  protected summary(a: AuditEntryDto): string {
    if (!a.data) return '';
    try {
      const d = JSON.parse(a.data) as Record<string, unknown>;
      const n = d['number'] ?? d['Number'] ?? d['sku'] ?? d['Sku'] ?? d['code'] ?? d['email'];
      return n ? String(n) : '';
    } catch {
      return '';
    }
  }

  protected pretty(data: string): string {
    try {
      return JSON.stringify(JSON.parse(data), null, 2);
    } catch {
      return data;
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [start, end] = this.range ?? [];
      const to = end ?? start;
      const page = await this.api.audit({
        ...this.query, search: this.search || undefined, action: this.area ?? undefined,
        from: start ? new Date(start.getFullYear(), start.getMonth(), start.getDate()).toISOString() : undefined,
        to: to ? new Date(to.getFullYear(), to.getMonth(), to.getDate() + 1).toISOString() : undefined,
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
