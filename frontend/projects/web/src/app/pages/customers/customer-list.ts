import { Component, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { ApiError, AuthService, CustomerListItemDto, CustomersApi, PageQuery, Permissions } from '@wh/shared';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';
import { ACTIVE_FILTERS, debounce, fromLazyEvent } from '../../ui/table-query';

@Component({
  selector: 'wh-customer-list',
  imports: [FormsModule, RouterLink, TableModule, Button, InputText, IconField, InputIcon, Select, StatusTag, LoadError],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Customers</h1>
          <p>Companies that receive dispatches, with their delivery addresses.</p>
        </div>
        @if (canManage) {
          <div class="page-actions">
            <p-button label="Import" icon="pi pi-upload" severity="secondary" [outlined]="true" routerLink="/import" [queryParams]="{ kind: 'customers' }" />
            <p-button label="New customer" icon="pi pi-plus" routerLink="/customers/new" />
          </div>
        }
      </header>

      <div class="toolbar">
        <p-iconfield class="grow">
          <p-inputicon styleClass="pi pi-search" />
          <input pInputText type="search" placeholder="Reference, company, contact or email" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search customers" />
        </p-iconfield>
        <p-select [options]="activeFilters" optionLabel="label" optionValue="value" [(ngModel)]="isActive" (onChange)="applyFilters()"
          placeholder="Active and inactive" [showClear]="true" ariaLabel="Status" />
      </div>

      @if (error(); as message) {
        <wh-load-error [message]="message" (retry)="load()" />
      }

      <p-table #table [tableStyle]="{ 'min-width': '52rem' }" [value]="rows()" [lazy]="true" (onLazyLoad)="onLazyLoad($event)" [loading]="loading()" [paginator]="true"
        [rows]="25" [rowsPerPageOptions]="[25, 50, 100]" [totalRecords]="total()" sortField="companyName" [sortOrder]="1" dataKey="id" [rowHover]="true"
        [showCurrentPageReport]="true" currentPageReportTemplate="{first}–{last} of {totalRecords}">
        <ng-template #header>
          <tr>
            <th pSortableColumn="reference">Reference <p-sortIcon field="reference" /></th>
            <th pSortableColumn="companyName">Company <p-sortIcon field="companyName" /></th>
            <th>Contact</th>
            <th pSortableColumn="city">City <p-sortIcon field="city" /></th>
            <th class="num">Addresses</th>
            <th>Status</th>
          </tr>
        </ng-template>
        <ng-template #body let-c>
          <tr class="clickable-row" [class.inactive]="!c.isActive" (click)="open(c)" (keydown.enter)="open(c)" tabindex="0">
            <td class="code">{{ c.reference }}</td>
            <td>{{ c.companyName }}</td>
            <td>
              {{ c.contactName ?? '—' }}
              @if (c.email) { <div class="muted">{{ c.email }}</div> }
            </td>
            <td>{{ c.city ?? '—' }}</td>
            <td class="num">{{ c.addressCount }}</td>
            <td><wh-status-tag [active]="c.isActive" /></td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr><td colspan="6" class="empty">{{ search ? 'No customers match this search.' : 'No customers yet.' }}</td></tr>
        </ng-template>
      </p-table>
    </section>
  `,
})
export class CustomerList {
  private readonly api = inject(CustomersApi);
  private readonly router = inject(Router);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.CustomersManage);
  private readonly table = viewChild.required<Table>('table');

  protected readonly rows = signal<CustomerListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly activeFilters = ACTIVE_FILTERS;
  protected search = '';
  protected isActive: boolean | null = true;
  private query: PageQuery = { page: 1, pageSize: 25, sortBy: 'companyName', isActive: true };

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    this.query = fromLazyEvent(event, this.query);
    void this.load();
  }

  protected readonly onSearch = debounce(() => this.applyFilters());

  protected applyFilters(): void {
    this.query = { ...this.query, page: 1, search: this.search || undefined, isActive: this.isActive ?? undefined };
    this.table().first = 0;
    void this.load();
  }

  protected open(c: CustomerListItemDto): void {
    void this.router.navigate(['/customers', c.id]);
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list(this.query);
      this.rows.set(page.items);
      this.total.set(page.totalCount);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }
}
