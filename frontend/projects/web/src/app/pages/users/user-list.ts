import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService } from 'primeng/api';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { MultiSelect } from 'primeng/multiselect';
import { Password } from 'primeng/password';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Tag } from 'primeng/tag';
import { ApiError, AuthService, PageQuery, RoleDto, UserListItemDto, UsersApi, WarehouseDto, WarehousesApi } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';
import { ACTIVE_FILTERS, debounce, fromLazyEvent } from '../../ui/table-query';

/** Matches the server password policy (10+ chars, upper, lower, digit). */
const PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{10,}$/;

@Component({
  selector: 'wh-user-list',
  imports: [DatePipe, FormsModule, ReactiveFormsModule, TableModule, Button, Dialog, InputText, IconField, InputIcon, Select, MultiSelect, Password, ToggleSwitch, Tag, FieldError, StatusTag, LoadError],
  templateUrl: './user-list.html',
})
export class UserList implements OnInit {
  private readonly api = inject(UsersApi);
  private readonly warehousesApi = inject(WarehousesApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  protected readonly me = inject(AuthService).user;
  private readonly table = viewChild.required<Table>('table');

  protected readonly rows = signal<UserListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly roles = signal<RoleDto[]>([]);
  protected readonly warehouses = signal<WarehouseDto[]>([]);
  protected readonly activeFilters = ACTIVE_FILTERS;
  protected search = '';
  protected role: string | null = null;
  protected isActive: boolean | null = true;
  private query: PageQuery = { page: 1, pageSize: 25, sortBy: 'displayName', isActive: true };

  protected readonly dialog = signal(false);
  protected readonly editing = signal<{ id: string; stamp: string; email: string } | null>(null);
  protected readonly saving = signal(false);
  protected readonly resetFor = signal<UserListItemDto | null>(null);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: ['', [Validators.required, Validators.maxLength(120)]],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
    roles: this.fb.control<string[]>([], Validators.required),
    allWarehouses: [false],
    warehouseIds: this.fb.control<string[]>([]),
  });
  protected readonly resetForm = this.fb.group({ password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]] });
  protected readonly roleOptions = computed(() => this.roles().map((r) => ({ value: r.code, label: r.name })));

  async ngOnInit(): Promise<void> {
    try {
      const [roles, warehouses] = await Promise.all([this.api.roles(), this.warehousesApi.list(true)]);
      this.roles.set(roles);
      this.warehouses.set(warehouses);
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    this.query = fromLazyEvent(event, this.query);
    void this.load();
  }

  protected readonly onSearch = debounce(() => this.applyFilters());

  protected applyFilters(): void {
    this.query = { ...this.query, page: 1, search: this.search || undefined, role: this.role ?? undefined, isActive: this.isActive ?? undefined };
    this.table().first = 0;
    void this.load();
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

  protected roleName(code: string): string {
    return this.roles().find((r) => r.code === code)?.name ?? code;
  }

  protected openNew(): void {
    this.editing.set(null);
    this.form.reset({ email: '', displayName: '', password: '', roles: [], allWarehouses: false, warehouseIds: this.warehouses().length === 1 ? [this.warehouses()[0]!.id] : [] });
    this.form.controls.email.enable();
    this.form.controls.password.enable();
    this.dialog.set(true);
  }

  protected async openEdit(row: UserListItemDto): Promise<void> {
    try {
      const u = await this.api.getOne(row.id);
      this.editing.set({ id: u.id, stamp: u.concurrencyStamp, email: u.email });
      this.form.reset({ email: u.email, displayName: u.displayName, password: '', roles: u.roles, allWarehouses: u.allWarehouses, warehouseIds: u.warehouses.map((w) => w.id) });
      this.form.controls.email.disable();
      this.form.controls.password.disable();
      this.dialog.set(true);
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    if (!v.allWarehouses && v.warehouseIds.length === 0) {
      this.form.controls.warehouseIds.setErrors({ server: 'Choose at least one warehouse, or give access to all.' });
      this.form.controls.warehouseIds.markAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      const editing = this.editing();
      const warehouseIds = v.allWarehouses ? [] : v.warehouseIds;
      if (editing) {
        await this.api.update(editing.id, { displayName: v.displayName, roles: v.roles, allWarehouses: v.allWarehouses, warehouseIds, concurrencyStamp: editing.stamp });
        this.feedback.success('User saved. Their current sessions were signed out so the new access applies.');
      } else {
        await this.api.create({ email: v.email, displayName: v.displayName, password: v.password, roles: v.roles, allWarehouses: v.allWarehouses, warehouseIds });
        this.feedback.success(`User ${v.email} created. Give them their initial password.`);
      }
      this.dialog.set(false);
      await this.load();
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.saving.set(false);
    }
  }

  protected toggleActive(row: UserListItemDto): void {
    const activate = !row.isActive;
    this.confirm.confirm({
      header: activate ? 'Reactivate user' : 'Deactivate user',
      message: activate ? `Allow ${row.displayName} to sign in again?` : `Deactivate ${row.displayName}? They are signed out immediately and cannot sign in.`,
      acceptLabel: activate ? 'Reactivate' : 'Deactivate',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: activate ? undefined : 'p-button-danger',
      accept: async () => {
        try {
          await this.api.setActive(row.id, activate);
          await this.load();
        } catch (e) {
          this.feedback.error(e);
        }
      },
    });
  }

  protected async unlock(row: UserListItemDto): Promise<void> {
    try {
      await this.api.unlock(row.id);
      this.feedback.success(`${row.displayName} can sign in again.`);
      await this.load();
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected openReset(row: UserListItemDto): void {
    this.resetForm.reset();
    this.resetFor.set(row);
  }

  protected async resetPassword(): Promise<void> {
    const row = this.resetFor();
    if (!row || this.resetForm.invalid) {
      this.resetForm.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      await this.api.resetPassword(row.id, this.resetForm.getRawValue().password);
      this.feedback.success(`Password changed. ${row.displayName} has been signed out everywhere.`);
      this.resetFor.set(null);
      await this.load();
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.saving.set(false);
    }
  }
}
