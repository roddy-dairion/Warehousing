import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { TableModule } from 'primeng/table';
import { ApiError, DISPATCH_STATUSES, DispatchStatus, ReportInfoDto, ReportingApi, ReportResult, WarehouseContextService } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { saveBlob, toIsoDate } from '../../ui/format';

/** Warehouse reports: preview the first 500 rows, download the full report as CSV (opens in Excel). */
@Component({
  selector: 'wh-reports-page',
  imports: [FormsModule, Button, Select, DatePicker, TableModule, LoadError, NeedWarehouse],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Reports</h1>
          <p>For the selected warehouse. Preview shows up to 500 rows; the CSV download contains everything (up to 50 000 rows).</p>
        </div>
      </header>
      @if (!warehouses.current()) {
        <wh-need-warehouse />
      } @else {
        <div class="toolbar">
          <p-select [options]="reports()" optionLabel="title" optionValue="kind" [(ngModel)]="kind" (onChange)="result.set(null)" placeholder="Choose a report" ariaLabel="Report" appendTo="body" />
          @if (selected()?.usesDates) {
            <p-datepicker [(ngModel)]="range" selectionMode="range" [readonlyInput]="true" dateFormat="dd.mm.yy" placeholder="All dates" [showClear]="true" appendTo="body" ariaLabel="Date range" />
          }
          @if (kind === 'dispatches') {
            <p-select [options]="statuses" optionLabel="label" optionValue="value" [(ngModel)]="status" placeholder="All statuses" [showClear]="true" ariaLabel="Status" appendTo="body" />
          }
          <p-button label="Show" icon="pi pi-table" (onClick)="run()" [disabled]="!kind" [loading]="loading()" />
          <p-button label="Download CSV" icon="pi pi-download" severity="secondary" [outlined]="true" (onClick)="download()" [disabled]="!kind" [loading]="downloading()" />
        </div>
        @if (error(); as m) { <wh-load-error [message]="m" (retry)="run()" /> }
        @if (result(); as r) {
          <div class="panel">
            <div class="panel-header"><h2>{{ r.title }}</h2><span class="muted">{{ r.rows.length }} row(s){{ r.truncated ? ' shown — download the CSV for all rows' : '' }}</span></div>
            <p-table [value]="r.rows" [scrollable]="true" scrollHeight="60vh" [tableStyle]="{ 'min-width': (r.columns.length * 8) + 'rem' }" [paginator]="r.rows.length > 100" [rows]="100">
              <ng-template #header><tr>@for (c of r.columns; track $index) { <th>{{ c }}</th> }</tr></ng-template>
              <ng-template #body let-row><tr>@for (v of row; track $index) { <td>{{ v ?? '' }}</td> }</tr></ng-template>
              <ng-template #emptymessage><tr><td [attr.colspan]="r.columns.length" class="empty">No data for these filters.</td></tr></ng-template>
            </p-table>
          </div>
        }
      }
    </section>
  `,
})
export class ReportsPage implements OnInit {
  private readonly api = inject(ReportingApi);
  private readonly feedback = inject(ApiFeedback);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly reports = signal<ReportInfoDto[]>([]);
  protected readonly result = signal<ReportResult | null>(null);
  protected readonly loading = signal(false);
  protected readonly downloading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statuses = DISPATCH_STATUSES;
  protected kind: string | null = null;
  protected range: Date[] | null = null;
  protected status: DispatchStatus | null = null;

  constructor() {
    effect(() => {
      this.warehouses.current();
      this.result.set(null);
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.reports.set(await this.api.reports());
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    }
  }

  protected selected(): ReportInfoDto | undefined {
    return this.reports().find((r) => r.kind === this.kind);
  }

  protected async run(): Promise<void> {
    const q = this.query();
    if (!q || !this.kind) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(await this.api.report(this.kind, q));
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected async download(): Promise<void> {
    const q = this.query();
    if (!q || !this.kind) return;
    this.downloading.set(true);
    try {
      saveBlob(await this.api.reportCsv(this.kind, q), `${this.kind}-${toIsoDate(new Date())}.csv`);
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.downloading.set(false);
    }
  }

  private query(): { warehouseId: string; from?: string; to?: string; status?: DispatchStatus } | null {
    const wh = this.warehouses.current();
    if (!wh) return null;
    const [from, to] = this.range ?? [];
    return {
      warehouseId: wh.id,
      from: this.selected()?.usesDates ? (toIsoDate(from) ?? undefined) : undefined,
      to: this.selected()?.usesDates ? (toIsoDate(to ?? from) ?? undefined) : undefined,
      status: this.kind === 'dispatches' ? (this.status ?? undefined) : undefined,
    };
  }
}
