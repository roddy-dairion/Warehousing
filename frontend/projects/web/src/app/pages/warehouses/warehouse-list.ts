import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { ApiError, AuthService, Permissions, WarehouseDto, WarehousesApi } from '@wh/shared';
import { ApiFeedback, nullIfBlank } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';

@Component({
  selector: 'wh-warehouse-list',
  imports: [ReactiveFormsModule, TableModule, Button, Dialog, InputText, FieldError, StatusTag, LoadError],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Warehouses</h1>
          <p>Branches, their contact details and bins.</p>
        </div>
        @if (canManage) {
          <p-button label="New warehouse" icon="pi pi-plus" (onClick)="openNew()" />
        }
      </header>

      @if (error(); as message) {
        <wh-load-error [message]="message" (retry)="load()" />
      }

      <p-table [value]="rows()" [loading]="loading()" dataKey="id" [rowHover]="true" [tableStyle]="{ 'min-width': '40rem' }">
        <ng-template #header>
          <tr><th>Code</th><th>Name</th><th>City</th><th>Phone</th><th>Status</th></tr>
        </ng-template>
        <ng-template #body let-w>
          <tr class="clickable-row" [class.inactive]="!w.isActive" (click)="open(w)" (keydown.enter)="open(w)" tabindex="0">
            <td class="code">{{ w.code }}</td>
            <td>{{ w.name }}</td>
            <td>{{ w.city ?? '—' }}</td>
            <td>{{ w.phone ?? '—' }}</td>
            <td><wh-status-tag [active]="w.isActive" /></td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr><td colspan="5" class="empty">No warehouses are assigned to your account.</td></tr>
        </ng-template>
      </p-table>
    </section>

    <p-dialog header="New warehouse" [visible]="dialog()" (visibleChange)="dialog.set($event)" [modal]="true" [style]="{ width: '32rem' }" [breakpoints]="{ '640px': '95vw' }" [draggable]="false">
      <form [formGroup]="form" (ngSubmit)="create()" novalidate id="whForm">
        <div class="form-grid">
          <div class="field">
            <label for="w-code">Code</label>
            <input pInputText id="w-code" formControlName="code" placeholder="e.g. NORTH" autocomplete="off" />
            <wh-field-error [control]="form.controls.code" requiredText="Enter a code." />
          </div>
          <div class="field">
            <label for="w-name">Name</label>
            <input pInputText id="w-name" formControlName="name" />
            <wh-field-error [control]="form.controls.name" requiredText="Enter a name." />
          </div>
          <div class="field"><label for="w-city">City</label><input pInputText id="w-city" formControlName="city" /></div>
          <div class="field"><label for="w-phone">Phone</label><input pInputText id="w-phone" formControlName="phone" type="tel" /></div>
        </div>
        <p class="muted" style="margin: 1rem 0 0">Receiving, dispatch dock and quarantine areas are created automatically. No users get access until you grant it.</p>
      </form>
      <ng-template #footer>
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="dialog.set(false)" />
        <p-button (onClick)="create()" label="Create warehouse" [loading]="saving()" />
      </ng-template>
    </p-dialog>
  `,
})
export class WarehouseList implements OnInit {
  private readonly api = inject(WarehousesApi);
  private readonly router = inject(Router);
  private readonly feedback = inject(ApiFeedback);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.WarehousesManage);

  protected readonly rows = signal<WarehouseDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly dialog = signal(false);
  protected readonly saving = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{1,15}$/)]],
    name: ['', [Validators.required, Validators.maxLength(120)]],
    city: [''],
    phone: [''],
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rows.set(await this.api.list(this.canManage));
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected open(w: WarehouseDto): void {
    void this.router.navigate(['/warehouses', w.id]);
  }

  protected openNew(): void {
    this.form.reset();
    this.dialog.set(true);
  }

  protected async create(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      const v = nullIfBlank(this.form.getRawValue());
      const created = await this.api.create({ ...v, code: v.code!, name: v.name! });
      this.dialog.set(false);
      this.feedback.success(`Warehouse ${created.code} created.`);
      await this.router.navigate(['/warehouses', created.id]);
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.saving.set(false);
    }
  }
}
