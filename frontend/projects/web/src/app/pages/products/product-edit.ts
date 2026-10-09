import { Component, computed, inject, input, OnDestroy, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Tooltip } from 'primeng/tooltip';
import { Skeleton } from 'primeng/skeleton';
import { ApiError, AuthService, CatalogApi, CategoryDto, Permissions, ProductDto, ProductsApi, UnitDto } from '@wh/shared';
import { ApiFeedback, nullIfBlank } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';

@Component({
  selector: 'wh-product-edit',
  imports: [ReactiveFormsModule, FormsModule, RouterLink, Button, InputText, InputNumber, Select, Textarea, ToggleSwitch, TableModule, Tag, Tooltip, Skeleton, FieldError, StatusTag, LoadError],
  templateUrl: './product-edit.html',
  styleUrl: './product-edit.scss',
})
export class ProductEdit implements OnInit, OnDestroy {
  private readonly api = inject(ProductsApi);
  private readonly catalog = inject(CatalogApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.ProductsManage);

  /** Route parameter: product id, or undefined on /products/new. */
  readonly id = input<string>();

  protected readonly product = signal<ProductDto | null>(null);
  protected readonly units = signal<UnitDto[]>([]);
  protected readonly categories = signal<CategoryDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly busy = signal(false);
  protected readonly imageUrl = signal<string | null>(null);
  protected readonly isNew = computed(() => !this.id());

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    sku: ['', [Validators.required, Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9._/-]*$/)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    categoryId: this.fb.control<string | null>(null),
    baseUnitId: ['', Validators.required],
    minStockQty: this.fb.control<number | null>(null, Validators.min(0)),
    isLotTracked: [false],
    hasExpiry: [false],
    shelfLifeDays: this.fb.control<number | null>(null, Validators.min(1)),
    barcode: [''],
  });

  protected readonly unitForm = this.fb.group({
    unitId: ['', Validators.required],
    factorToBase: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.000001)]),
  });

  protected readonly barcodeForm = this.fb.group({
    value: ['', [Validators.required, Validators.maxLength(64), Validators.pattern(/^\S+$/)]],
    unitId: this.fb.control<string | null>(null),
  });

  protected readonly baseUnit = computed(() => this.units().find((u) => u.id === this.form.controls.baseUnitId.value) ?? null);
  protected readonly activeUnits = computed(() => this.units().filter((u) => u.isActive).map((u) => ({ ...u, label: `${u.code} · ${u.name}` })));
  protected readonly packChoices = computed(() => {
    const p = this.product();
    if (!p) return [];
    const used = new Set([p.baseUnitId, ...p.units.map((u) => u.unitId)]);
    return this.activeUnits().filter((u) => !used.has(u.id));
  });
  protected readonly barcodeUnitChoices = computed(() => {
    const p = this.product();
    if (!p) return [];
    return [{ id: null as string | null, label: `${p.baseUnitCode} (single)` }, ...p.units.map((u) => ({ id: u.unitId as string | null, label: `${u.unitCode} (${u.factorToBase} ${p.baseUnitCode})` }))];
  });

  async ngOnInit(): Promise<void> {
    this.form.controls.hasExpiry.valueChanges.subscribe((v) => {
      if (v) this.form.controls.isLotTracked.setValue(true);
      else this.form.controls.shelfLifeDays.setValue(null);
    });
    this.form.controls.isLotTracked.valueChanges.subscribe((v) => {
      if (!v) this.form.controls.hasExpiry.setValue(false);
    });
    if (!this.canManage) this.form.disable();
    await this.load();
  }

  ngOnDestroy(): void {
    this.revokeImage();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const [units, categories] = await Promise.all([this.catalog.units(), this.catalog.categories()]);
      this.units.set(units);
      this.categories.set(categories.filter((c) => c.isActive));
      const id = this.id();
      if (id) {
        this.setProduct(await this.api.getOne(id));
        await this.loadImage();
      } else {
        const ea = units.find((u) => u.code === 'EA');
        if (ea) this.form.controls.baseUnitId.setValue(ea.id);
      }
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const v = nullIfBlank(this.form.getRawValue());
    const body = {
      name: v.name,
      description: v.description,
      categoryId: v.categoryId,
      baseUnitId: v.baseUnitId,
      minStockQty: v.minStockQty,
      isLotTracked: v.isLotTracked,
      hasExpiry: v.hasExpiry,
      shelfLifeDays: v.hasExpiry ? v.shelfLifeDays : null,
    };
    try {
      const current = this.product();
      if (current) {
        this.setProduct(await this.api.update(current.id, { ...body, version: current.version }));
        this.feedback.success('Product saved.');
      } else {
        const created = await this.api.create({ ...body, sku: v.sku, barcodes: v.barcode ? [{ value: v.barcode }] : [] });
        this.feedback.success(`Product ${created.sku} created. Add pack sizes and barcodes below.`);
        await this.router.navigate(['/products', created.id], { replaceUrl: true });
      }
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.saving.set(false);
    }
  }

  protected toggleActive(): void {
    const p = this.product();
    if (!p) return;
    const activate = !p.isActive;
    this.confirm.confirm({
      header: activate ? 'Reactivate product' : 'Deactivate product',
      message: activate
        ? `Make ${p.sku} available again for receipts and dispatches?`
        : `Deactivate ${p.sku}? It will no longer be offered for new receipts and dispatches. Its history is kept.`,
      acceptLabel: activate ? 'Reactivate' : 'Deactivate',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: activate ? undefined : 'p-button-danger',
      accept: () => this.run(async () => {
        await this.api.setActive(p.id, activate);
        this.setProduct(await this.api.getOne(p.id));
        this.feedback.success(activate ? 'Product reactivated.' : 'Product deactivated.');
      }),
    });
  }

  // ---- Pack units ----

  protected addUnit(): void {
    const p = this.product();
    if (!p || this.unitForm.invalid) {
      this.unitForm.markAllAsTouched();
      return;
    }
    const { unitId, factorToBase } = this.unitForm.getRawValue();
    void this.run(async () => {
      this.setProduct(await this.api.addUnit(p.id, unitId, factorToBase!, p.version));
      this.unitForm.reset();
      this.feedback.success('Pack size added.');
    }, this.unitForm);
  }

  /** Edited pack sizes, saved when the field loses focus. */
  protected readonly pendingFactor: Record<string, number | null> = {};

  protected commitFactor(productUnitId: string, current: number): void {
    const p = this.product();
    const value = this.pendingFactor[productUnitId];
    delete this.pendingFactor[productUnitId];
    if (!p || value === undefined || value === null || value === current) return;
    void this.run(async () => {
      this.setProduct(await this.api.changeUnit(p.id, productUnitId, value, p.version));
      this.feedback.success('Pack size updated.');
    });
  }

  protected removeUnit(productUnitId: string, code: string): void {
    const p = this.product();
    if (!p) return;
    this.confirm.confirm({
      header: 'Remove pack size',
      message: `Remove the ${code} pack size from ${p.sku}?`,
      acceptLabel: 'Remove',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.run(async () => this.setProduct(await this.api.removeUnit(p.id, productUnitId, p.version))),
    });
  }

  // ---- Barcodes ----

  protected addBarcode(): void {
    const p = this.product();
    if (!p || this.barcodeForm.invalid) {
      this.barcodeForm.markAllAsTouched();
      return;
    }
    const { value, unitId } = this.barcodeForm.getRawValue();
    void this.run(async () => {
      this.setProduct(await this.api.addBarcode(p.id, value.trim(), unitId, p.version));
      this.barcodeForm.reset({ value: '', unitId: null });
      this.feedback.success('Barcode added.');
    }, this.barcodeForm);
  }

  protected setPrimary(barcodeId: string): void {
    const p = this.product();
    if (!p) return;
    void this.run(async () => this.setProduct(await this.api.setPrimaryBarcode(p.id, barcodeId, p.version)));
  }

  protected removeBarcode(barcodeId: string, value: string): void {
    const p = this.product();
    if (!p) return;
    this.confirm.confirm({
      header: 'Remove barcode',
      message: `Remove barcode ${value}? Scanning it will no longer find this product.`,
      acceptLabel: 'Remove',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.run(async () => this.setProduct(await this.api.removeBarcode(p.id, barcodeId, p.version))),
    });
  }

  // ---- Image ----

  protected async onImageSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const p = this.product();
    if (!file || !p) return;
    if (file.size > 2 * 1024 * 1024) {
      this.feedback.error(new Error('The image must be smaller than 2 MB.'));
      return;
    }
    await this.run(async () => {
      await this.api.uploadImage(p.id, file);
      this.setProduct(await this.api.getOne(p.id));
      await this.loadImage();
      this.feedback.success('Image saved.');
    });
  }

  protected removeImage(): void {
    const p = this.product();
    if (!p) return;
    void this.run(async () => {
      await this.api.removeImage(p.id);
      this.setProduct(await this.api.getOne(p.id));
      this.revokeImage();
    });
  }

  private async loadImage(): Promise<void> {
    this.revokeImage();
    const p = this.product();
    if (!p?.hasImage) return;
    try {
      this.imageUrl.set(URL.createObjectURL(await this.api.image(p.id)));
    } catch {
      /* image is optional */
    }
  }

  private revokeImage(): void {
    const url = this.imageUrl();
    if (url) URL.revokeObjectURL(url);
    this.imageUrl.set(null);
  }

  private setProduct(p: ProductDto): void {
    this.product.set(p);
    this.form.reset({
      sku: p.sku,
      name: p.name,
      description: p.description ?? '',
      categoryId: p.categoryId,
      baseUnitId: p.baseUnitId,
      minStockQty: p.minStockQty,
      isLotTracked: p.isLotTracked,
      hasExpiry: p.hasExpiry,
      shelfLifeDays: p.shelfLifeDays,
      barcode: '',
    });
    this.form.controls.sku.disable();
  }

  private async run(action: () => Promise<void>, form?: Parameters<ApiFeedback['error']>[1]): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      await action();
    } catch (e) {
      this.feedback.error(e, form);
    } finally {
      this.busy.set(false);
    }
  }
}
