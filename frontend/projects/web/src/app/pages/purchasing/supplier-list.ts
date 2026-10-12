import { Component, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { Dialog } from 'primeng/dialog';
import { Textarea } from 'primeng/textarea';
import { ApiError, AuthService, PageQuery, Permissions, SupplierDto, SupplierListItemDto, SuppliersApi } from '@wh/shared';
import { ApiFeedback, nullIfBlank } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { LoadError } from '../../ui/load-error';
import { StatusTag } from '../../ui/status-tag';
import { ACTIVE_FILTERS, debounce, fromLazyEvent } from '../../ui/table-query';

/** Suppliers that goods are ordered from (including the company's own production). Edited in a dialog. */
@Component({
  selector: 'wh-supplier-list',
  imports: [FormsModule, ReactiveFormsModule, TableModule, Button, InputText, InputNumber, IconField, InputIcon, Select, Dialog, Textarea, FieldError, LoadError, StatusTag],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Suppliers</h1>
          <p>Who goods are ordered from. The lead time proposes the expected delivery date of new orders.</p>
        </div>
        @if (canManage) {
          <div class="page-actions"><p-button label="New supplier" icon="pi pi-plus" (onClick)="edit(null)" data-testid="new-supplier" /></div>
        }
      </header>

      <div class="toolbar">
        <p-iconfield class="grow">
          <p-inputicon styleClass="pi pi-search" />
          <input pInputText type="search" placeholder="Code, name, contact or email" [(ngModel)]="search" (ngModelChange)="onSearch()" aria-label="Search suppliers" />
        </p-iconfield>
        <p-select [options]="activeFilters" optionLabel="label" optionValue="value" [(ngModel)]="isActive" (onChange)="applyFilters()"
          placeholder="Active and inactive" [showClear]="true" ariaLabel="Status" />
      </div>

      @if (error(); as message) { <wh-load-error [message]="message" (retry)="load()" /> }

      <p-table #table [tableStyle]="{ 'min-width': '48rem' }" [value]="rows()" [lazy]="true" (onLazyLoad)="onLazyLoad($event)" [loading]="loading()" [paginator]="true"
        [rows]="25" [rowsPerPageOptions]="[25, 50, 100]" [totalRecords]="total()" sortField="name" [sortOrder]="1" dataKey="id" [rowHover]="true"
        [showCurrentPageReport]="true" currentPageReportTemplate="{first}–{last} of {totalRecords}">
        <ng-template #header>
          <tr>
            <th pSortableColumn="code">Code <p-sortIcon field="code" /></th>
            <th pSortableColumn="name">Name <p-sortIcon field="name" /></th>
            <th>Contact</th>
            <th pSortableColumn="city">City <p-sortIcon field="city" /></th>
            <th class="num" pSortableColumn="leadTimeDays">Lead time <p-sortIcon field="leadTimeDays" /></th>
            <th>Status</th>
          </tr>
        </ng-template>
        <ng-template #body let-s>
          <tr class="clickable-row" [class.inactive]="!s.isActive" (click)="edit(s)" (keydown.enter)="edit(s)" tabindex="0">
            <td class="code">{{ s.code }}</td>
            <td>{{ s.name }}</td>
            <td>{{ s.contactName ?? '—' }}@if (s.email) { <div class="muted">{{ s.email }}</div> }</td>
            <td>{{ s.city ?? '—' }}</td>
            <td class="num">{{ s.leadTimeDays === null ? '—' : s.leadTimeDays + ' days' }}</td>
            <td><wh-status-tag [active]="s.isActive" /></td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr><td colspan="6" class="empty">{{ search ? 'No suppliers match this search.' : 'No suppliers yet.' }}</td></tr>
        </ng-template>
      </p-table>
    </section>

    <p-dialog [visible]="dialog()" (visibleChange)="dialog.set($event)" [header]="editing() ? editing()!.code + ' · ' + editing()!.name : 'New supplier'"
      [modal]="true" [style]="{ width: 'min(44rem, 95vw)' }">
      <form class="form-grid" [formGroup]="form" novalidate>
        <div class="field">
          <label for="code">Code</label>
          <input pInputText id="code" formControlName="code" placeholder="e.g. HC-PROD" data-testid="supplier-code" />
          <wh-field-error [control]="form.controls.code" requiredText="Enter a code." />
        </div>
        <div class="field">
          <label for="name">Name</label>
          <input pInputText id="name" formControlName="name" data-testid="supplier-name" />
          <wh-field-error [control]="form.controls.name" requiredText="Enter the name." />
        </div>
        <div class="field">
          <label for="contactName">Contact</label>
          <input pInputText id="contactName" formControlName="contactName" />
        </div>
        <div class="field">
          <label for="email">Email</label>
          <input pInputText id="email" type="email" formControlName="email" />
          <wh-field-error [control]="form.controls.email" />
        </div>
        <div class="field">
          <label for="phone">Phone</label>
          <input pInputText id="phone" formControlName="phone" />
        </div>
        <div class="field">
          <label for="leadTimeDays">Lead time (days)</label>
          <p-inputnumber inputId="leadTimeDays" formControlName="leadTimeDays" [min]="0" [max]="365" [useGrouping]="false" />
        </div>
        <div class="field">
          <label for="addressLine1">Address</label>
          <input pInputText id="addressLine1" formControlName="addressLine1" />
        </div>
        <div class="field">
          <label for="addressLine2">Address line 2</label>
          <input pInputText id="addressLine2" formControlName="addressLine2" />
        </div>
        <div class="field">
          <label for="city">City</label>
          <input pInputText id="city" formControlName="city" />
        </div>
        <div class="field">
          <label for="country">Country</label>
          <input pInputText id="country" formControlName="country" />
        </div>
        <div class="field span-all">
          <label for="notes">Notes</label>
          <textarea pTextarea id="notes" formControlName="notes" rows="2" [autoResize]="true"></textarea>
        </div>
      </form>
      <ng-template #footer>
        @if (canManage && editing(); as s) {
          <p-button [label]="s.isActive ? 'Deactivate' : 'Activate'" severity="secondary" [text]="true" (onClick)="toggleActive(s)" [disabled]="busy()" class="left" />
        }
        <p-button label="Close" severity="secondary" [text]="true" (onClick)="dialog.set(false)" />
        @if (canManage) { <p-button label="Save" icon="pi pi-check" (onClick)="save()" [loading]="busy()" data-testid="supplier-save" /> }
      </ng-template>
    </p-dialog>
  `,
  styles: `
    :host ::ng-deep .left { margin-right: auto; }
  `,
})
export class SupplierList {
  private readonly api = inject(SuppliersApi);
  private readonly feedback = inject(ApiFeedback);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.PurchasingManage);
  private readonly table = viewChild.required<Table>('table');

  protected readonly rows = signal<SupplierListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly dialog = signal(false);
  protected readonly editing = signal<SupplierDto | null>(null);
  protected readonly busy = signal(false);
  protected readonly activeFilters = ACTIVE_FILTERS;
  protected search = '';
  protected isActive: boolean | null = true;
  private query: PageQuery = { page: 1, pageSize: 25, sortBy: 'name', isActive: true };

  private readonly fb = inject(FormBuilder);
  protected readonly form = this.fb.group({
    code: this.fb.control('', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{0,31}$/)]),
    name: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
    contactName: this.fb.control<string | null>(null, Validators.maxLength(120)),
    email: this.fb.control<string | null>(null, [Validators.email, Validators.maxLength(256)]),
    phone: this.fb.control<string | null>(null, Validators.maxLength(40)),
    leadTimeDays: this.fb.control<number | null>(null, [Validators.min(0), Validators.max(365)]),
    addressLine1: this.fb.control<string | null>(null, Validators.maxLength(200)),
    addressLine2: this.fb.control<string | null>(null, Validators.maxLength(200)),
    city: this.fb.control<string | null>(null, Validators.maxLength(100)),
    country: this.fb.control<string | null>(null, Validators.maxLength(100)),
    notes: this.fb.control<string | null>(null, Validators.maxLength(2000)),
  });

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

  protected async edit(row: SupplierListItemDto | null): Promise<void> {
    this.form.reset({ code: '', name: '' });
    this.editing.set(null);
    if (row) {
      try {
        const s = await this.api.getOne(row.id);
        this.editing.set(s);
        this.form.patchValue(s);
      } catch (e) {
        this.feedback.error(e);
        return;
      }
    }
    if (this.canManage) this.form.enable({ emitEvent: false });
    else this.form.disable();
    if (row) this.form.controls.code.disable();
    this.dialog.set(true);
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = nullIfBlank(this.form.getRawValue());
    const body = {
      name: (v.name ?? '').trim(), contactName: v.contactName, email: v.email, phone: v.phone, leadTimeDays: v.leadTimeDays,
      addressLine1: v.addressLine1, addressLine2: v.addressLine2, city: v.city, country: v.country, notes: v.notes,
    };
    this.busy.set(true);
    try {
      const current = this.editing();
      const saved = current
        ? await this.api.update(current.id, { ...body, version: current.version })
        : await this.api.create({ ...body, code: (v.code ?? '').trim().toUpperCase() });
      this.feedback.success(current ? 'Supplier saved.' : `Supplier ${saved.code} created.`);
      this.dialog.set(false);
      await this.load();
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggleActive(s: SupplierDto): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.setActive(s.id, !s.isActive);
      this.feedback.success(s.isActive ? 'Supplier deactivated: it can no longer be used for new orders.' : 'Supplier activated.');
      this.dialog.set(false);
      await this.load();
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
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
