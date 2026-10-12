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
import { ApiError, PageQuery, ReceiptListItemDto, ReceiptsApi, ReceiptStatus, WarehouseContextService } from '@wh/shared';
import { DocStatus } from '../../ui/doc-status';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { debounce, fromLazyEvent } from '../../ui/table-query';

const STATUSES: { value: ReceiptStatus; label: string }[] = [
  { value: 'draft', label: 'Draft' },
  { value: 'posted', label: 'Posted' },
  { value: 'reversed', label: 'Reversed' },
  { value: 'cancelled', label: 'Cancelled' },
];

@Component({
  selector: 'wh-receipt-list',
  imports: [DatePipe, FormsModule, RouterLink, TableModule, Button, InputText, IconField, InputIcon, Select, DocStatus, LoadError, NeedWarehouse],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Goods receipts</h1>
          <p>Drafts do not change stock. Posting adds the received quantities once.</p>
        </div>
        @if (warehouses.current()) {
          <div class="page-actions"><p-button label="New receipt" icon="pi pi-plus" routerLink="/receipts/new" /></div>
        }
      </header>
      @if (!warehouses.current()) {
        <wh-need-warehouse />
      } @else {
        <div class="toolbar">
          <p-iconfield class="grow">
            <p-inputicon styleClass="pi pi-search" />
            <input pInputText type="search" placeholder="Number, supplier or supplier reference" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search receipts" />
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
              <th pSortableColumn="receiptDate">Date <p-sortIcon field="receiptDate" /></th>
              <th>Supplier</th>
              <th class="num">Lines</th>
              <th pSortableColumn="status">Status <p-sortIcon field="status" /></th>
            </tr>
          </ng-template>
          <ng-template #body let-r>
            <tr class="clickable-row" (click)="open(r)" (keydown.enter)="open(r)" tabindex="0">
              <td class="code">{{ r.number }}</td>
              <td>{{ r.receiptDate | date: 'dd.MM.yyyy' }}</td>
              <td>{{ r.supplierName ?? '—' }}@if (r.supplierReference) { <span class="muted"> · {{ r.supplierReference }}</span> }</td>
              <td class="num">{{ r.lineCount }}</td>
              <td><wh-doc-status kind="receipt" [status]="r.status" /></td>
            </tr>
          </ng-template>
          <ng-template #emptymessage><tr><td colspan="5" class="empty">No receipts found.</td></tr></ng-template>
        </p-table>
      }
    </section>
  `,
})
export class ReceiptList {
  private readonly api = inject(ReceiptsApi);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);
  private readonly table = viewChild<Table>('table');
  protected readonly rows = signal<ReceiptListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statuses = STATUSES;
  protected search = '';
  protected status: ReceiptStatus | null = (inject(ActivatedRoute).snapshot.queryParamMap.get('status') as ReceiptStatus | null) ?? null;
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
  protected open(r: ReceiptListItemDto): void {
    void this.router.navigate(['/receipts', r.id]);
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
