import { Component, inject, input, OnInit, signal, viewChild } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Tag } from 'primeng/tag';
import { Skeleton } from 'primeng/skeleton';
import { ApiError, AuthService, LOCATION_TYPES, LocationDto, LocationType, PageQuery, Permissions, WarehouseDto, WarehousesApi } from '@wh/shared';
import { ApiFeedback, nullIfBlank } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';
import { ACTIVE_FILTERS, debounce, fromLazyEvent } from '../../ui/table-query';

@Component({
  selector: 'wh-warehouse-detail',
  imports: [FormsModule, ReactiveFormsModule, RouterLink, TableModule, Button, Dialog, InputText, IconField, InputIcon, Select, ToggleSwitch, Tag, Skeleton, FieldError, StatusTag, LoadError],
  templateUrl: './warehouse-detail.html',
})
export class WarehouseDetail implements OnInit {
  private readonly api = inject(WarehousesApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.WarehousesManage);
  private readonly table = viewChild<Table>('binTable');

  readonly id = input.required<string>();

  protected readonly warehouse = signal<WarehouseDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly bins = signal<LocationDto[]>([]);
  protected readonly binTotal = signal(0);
  protected readonly binsLoading = signal(true);
  protected readonly binDialog = signal(false);
  protected readonly editingBin = signal<LocationDto | null>(null);
  protected readonly types = LOCATION_TYPES;
  protected readonly activeFilters = ACTIVE_FILTERS;
  protected binSearch = '';
  protected binType: LocationType | null = null;
  protected binActive: boolean | null = true;
  private binQuery: PageQuery = { page: 1, pageSize: 50, sortBy: 'code', isActive: true };

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    addressLine1: [''],
    addressLine2: [''],
    city: [''],
    country: [''],
    phone: ['', Validators.maxLength(40)],
    email: ['', Validators.email],
  });

  protected readonly binForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/)]],
    type: this.fb.control<LocationType>('storage'),
    zone: ['', Validators.maxLength(32)],
    barcode: ['', [Validators.maxLength(64), Validators.pattern(/^\S*$/)]],
    isPickable: [true],
  });

  async ngOnInit(): Promise<void> {
    if (!this.canManage) this.form.disable();
    this.binForm.controls.type.valueChanges.subscribe((t) => {
      const pickable = this.binForm.controls.isPickable;
      if (t === 'storage') pickable.enable();
      else {
        pickable.setValue(false);
        pickable.disable();
      }
    });
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.setWarehouse(await this.api.getOne(this.id()));
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    const w = this.warehouse();
    if (!w || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      const v = nullIfBlank(this.form.getRawValue());
      this.setWarehouse(await this.api.update(w.id, { ...v, name: v.name!, version: w.version }));
      this.feedback.success('Warehouse saved.');
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.saving.set(false);
    }
  }

  protected toggleActive(): void {
    const w = this.warehouse();
    if (!w) return;
    const activate = !w.isActive;
    this.confirm.confirm({
      header: activate ? 'Reactivate warehouse' : 'Deactivate warehouse',
      message: activate ? `Reactivate ${w.name}?` : `Deactivate ${w.name}? Staff will no longer be able to work in it. Its stock and history are kept.`,
      acceptLabel: activate ? 'Reactivate' : 'Deactivate',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: activate ? undefined : 'p-button-danger',
      accept: async () => {
        try {
          await this.api.setActive(w.id, activate);
          await this.load();
        } catch (e) {
          this.feedback.error(e);
        }
      },
    });
  }

  // ---- Bins ----

  protected onBinsLazyLoad(event: TableLazyLoadEvent): void {
    this.binQuery = fromLazyEvent(event, this.binQuery);
    void this.loadBins();
  }

  protected readonly onBinSearch = debounce(() => this.applyBinFilters());

  protected applyBinFilters(): void {
    this.binQuery = { ...this.binQuery, page: 1, search: this.binSearch || undefined, type: this.binType ?? undefined, isActive: this.binActive ?? undefined };
    const t = this.table();
    if (t) t.first = 0;
    void this.loadBins();
  }

  protected async loadBins(): Promise<void> {
    this.binsLoading.set(true);
    try {
      const page = await this.api.locations(this.id(), this.binQuery);
      this.bins.set(page.items);
      this.binTotal.set(page.totalCount);
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.binsLoading.set(false);
    }
  }

  protected typeLabel(type: LocationType): string {
    return this.types.find((t) => t.value === type)?.label ?? type;
  }

  protected openBin(bin: LocationDto | null): void {
    this.editingBin.set(bin);
    this.binForm.reset(bin ? { code: bin.code, type: bin.type, zone: bin.zone ?? '', barcode: bin.barcode ?? '', isPickable: bin.isPickable } : { code: '', type: 'storage', zone: '', barcode: '', isPickable: true });
    if (bin) this.binForm.controls.code.disable();
    else this.binForm.controls.code.enable();
    if (bin?.isSystem) this.binForm.controls.type.disable();
    else this.binForm.controls.type.enable();
    this.binDialog.set(true);
  }

  protected async saveBin(): Promise<void> {
    if (this.binForm.invalid) {
      this.binForm.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const v = nullIfBlank(this.binForm.getRawValue());
    try {
      const editing = this.editingBin();
      if (editing) {
        await this.api.updateLocation(editing.id, { type: v.type!, zone: v.zone, barcode: v.barcode, isPickable: !!v.isPickable, version: editing.version });
        this.feedback.success(`Bin ${editing.code} saved.`);
      } else {
        const created = await this.api.createLocation(this.id(), { code: v.code!, type: v.type!, zone: v.zone, barcode: v.barcode, isPickable: v.isPickable });
        this.feedback.success(`Bin ${created.code} added.`);
      }
      this.binDialog.set(false);
      await this.loadBins();
    } catch (e) {
      this.feedback.error(e, this.binForm);
    } finally {
      this.saving.set(false);
    }
  }

  protected async toggleBin(bin: LocationDto): Promise<void> {
    try {
      await this.api.setLocationActive(bin.id, !bin.isActive);
      await this.loadBins();
    } catch (e) {
      this.feedback.error(e);
    }
  }

  private setWarehouse(w: WarehouseDto): void {
    this.warehouse.set(w);
    this.form.reset({
      name: w.name,
      addressLine1: w.addressLine1 ?? '',
      addressLine2: w.addressLine2 ?? '',
      city: w.city ?? '',
      country: w.country ?? '',
      phone: w.phone ?? '',
      email: w.email ?? '',
    });
  }
}
