import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormArray, FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { Textarea } from 'primeng/textarea';
import { Dialog } from 'primeng/dialog';
import { Skeleton } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import {
  ApiError, AuthService, LocationDto, newIdempotencyKey, Permissions, ProductDto, ReceiptDto, ReceiptInput, ReceiptsApi, WarehouseContextService,
} from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { DocStatus } from '../../ui/doc-status';
import { FieldError } from '../../ui/field-error';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { ProductPicker } from '../../ui/product-picker';
import { Lookups } from '../../ui/lookups';
import { fromIsoDate, qty, toIsoDate } from '../../ui/format';

type LineForm = FormGroup<{
  productId: import('@angular/forms').FormControl<string | null>;
  productUnitId: import('@angular/forms').FormControl<string | null>;
  quantity: import('@angular/forms').FormControl<number | null>;
  expectedQuantity: import('@angular/forms').FormControl<number | null>;
  locationId: import('@angular/forms').FormControl<string | null>;
  lotNo: import('@angular/forms').FormControl<string | null>;
  expiryDate: import('@angular/forms').FormControl<Date | null>;
  notes: import('@angular/forms').FormControl<string | null>;
}>;

/** Create, edit, post and reverse a goods receipt. Posted receipts are read-only. */
@Component({
  selector: 'wh-receipt-edit',
  imports: [
    DatePipe, FormsModule, ReactiveFormsModule, RouterLink, Button, InputText, InputNumber, Select, DatePicker, Textarea, Dialog, Skeleton, TableModule,
    DocStatus, FieldError, LoadError, NeedWarehouse, ProductPicker,
  ],
  templateUrl: './receipt-edit.html',
  styleUrl: '../../ui/line-editor.scss',
})
export class ReceiptEdit {
  private readonly api = inject(ReceiptsApi);
  private readonly lookups = inject(Lookups);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canPost = this.auth.hasPermission(Permissions.InventoryReceivePost);
  protected readonly canEdit = this.auth.hasPermission(Permissions.InventoryReceive);
  protected readonly qty = qty;

  /** Route parameter: an id, or undefined for /receipts/new. */
  readonly id = input<string>();

  protected readonly receipt = signal<ReceiptDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly bins = signal<LocationDto[]>([]);
  /** Product details per product id (units, lot/expiry tracking). */
  protected readonly products = signal<Record<string, ProductDto>>({});
  protected readonly reverseDialog = signal(false);
  protected reverseReason = '';
  private postKey = newIdempotencyKey();
  private reverseKey = newIdempotencyKey();

  private readonly fb = inject(FormBuilder);
  protected readonly form = this.fb.group({
    receiptDate: this.fb.control<Date | null>(new Date(), Validators.required),
    supplierName: this.fb.control<string | null>(null, Validators.maxLength(200)),
    supplierReference: this.fb.control<string | null>(null, Validators.maxLength(60)),
    notes: this.fb.control<string | null>(null, Validators.maxLength(2000)),
    lines: this.fb.array<LineForm>([]),
  });

  protected readonly isNew = computed(() => !this.id() || this.id() === 'new');
  protected readonly editable = computed(() => this.canEdit && (this.isNew() || this.receipt()?.status === 'draft'));

  protected get lines(): FormArray<LineForm> {
    return this.form.controls.lines;
  }

  constructor() {
    effect(() => {
      const wh = this.warehouses.current();
      if (wh) void this.lookups.binsOf(wh.id).then((b) => this.bins.set(b.filter((x) => x.type !== 'dispatch')));
    });
    effect(() => {
      const id = this.id();
      if (id && id !== 'new') void this.load(id);
      else if (this.lines.length === 0) this.addLine();
    });
  }

  protected binLabel(b: LocationDto): string {
    return b.isSystem ? `${b.code} (${b.type})` : b.code;
  }

  protected product(line: LineForm): ProductDto | null {
    const id = line.controls.productId.value;
    return id ? (this.products()[id] ?? null) : null;
  }

  protected unitOptions(line: LineForm): { id: string | null; label: string }[] {
    const p = this.product(line);
    if (!p) return [];
    return [{ id: null, label: p.baseUnitCode }, ...p.units.map((u) => ({ id: u.id, label: `${u.unitCode} (×${u.factorToBase})` }))];
  }

  protected onProduct(line: LineForm, p: ProductDto | null): void {
    if (p) this.products.update((m) => ({ ...m, [p.id]: p }));
    if (!p || !p.units.some((u) => u.id === line.controls.productUnitId.value)) line.controls.productUnitId.setValue(null);
    if (p && !p.isLotTracked) {
      line.controls.lotNo.setValue(null);
      line.controls.expiryDate.setValue(null);
    }
  }

  protected addLine(): void {
    this.lines.push(this.newLine());
  }

  protected removeLine(i: number): void {
    this.lines.removeAt(i);
    if (this.lines.length === 0) this.addLine();
  }

  protected async save(): Promise<ReceiptDto | null> {
    // Drafts may be saved without lot/expiry: drop the "required to post" flags before validating.
    this.lines.controls.forEach((l) => {
      l.controls.lotNo.updateValueAndValidity();
      l.controls.expiryDate.updateValueAndValidity();
    });
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.feedback.warn('Check the highlighted fields.');
      return null;
    }
    const wh = this.warehouses.current();
    if (!wh) return null;
    this.busy.set(true);
    try {
      const body = this.toInput();
      const current = this.receipt();
      const saved = current
        ? await this.api.update(current.id, { ...body, receiptDate: body.receiptDate!, version: current.version })
        : await this.api.create(wh.id, body);
      this.feedback.success(current ? 'Receipt saved.' : `Receipt ${saved.number} created.`);
      if (!current) {
        await this.router.navigate(['/receipts', saved.id], { replaceUrl: true });
      }
      this.apply(saved);
      return saved;
    } catch (e) {
      this.feedback.error(e, this.form);
      return null;
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Lot and expiry may stay empty on a draft but are required to post. Flags the missing fields and returns false
   * so the user sees what to fill instead of a server error after confirming.
   */
  private readyToPost(): boolean {
    let ok = true;
    this.lines.controls.forEach((line) => {
      const p = this.product(line);
      if (!p?.isLotTracked) return;
      const flag = (c: import('@angular/forms').AbstractControl, missing: boolean) => {
        if (!missing) return;
        ok = false;
        c.setErrors({ ...(c.errors ?? {}), requiredToPost: true });
        c.markAsTouched();
      };
      flag(line.controls.lotNo, !line.controls.lotNo.value?.trim());
      flag(line.controls.expiryDate, p.hasExpiry && !line.controls.expiryDate.value);
    });
    if (!ok) this.feedback.warn('Enter the lot and expiry date for the highlighted lines before posting.');
    return ok;
  }

  protected post(): void {
    if (!this.readyToPost()) return;
    this.confirm.confirm({
      header: 'Post receipt',
      message: 'Posting adds these quantities to stock. It cannot be edited afterwards (only reversed).',
      acceptLabel: 'Post',
      rejectLabel: 'Not yet',
      accept: async () => {
        const saved = this.form.dirty || !this.receipt() ? await this.save() : this.receipt();
        if (!saved) return;
        await this.run(async () => {
          this.apply(await this.api.postReceipt(saved.id, { key: this.postKey }));
          this.postKey = newIdempotencyKey();
          this.feedback.success('Receipt posted. Stock updated.');
        });
      },
    });
  }

  protected async reverse(): Promise<void> {
    const r = this.receipt();
    if (!r || !this.reverseReason.trim()) return;
    await this.run(async () => {
      this.apply(await this.api.reverse(r.id, this.reverseReason.trim(), { key: this.reverseKey }));
      this.reverseKey = newIdempotencyKey();
      this.reverseDialog.set(false);
      this.feedback.success('Receipt reversed. The stock was removed again.');
    });
  }

  protected cancelDraft(): void {
    const r = this.receipt();
    if (!r) return;
    this.confirm.confirm({
      header: 'Cancel draft',
      message: `Cancel receipt ${r.number}? It will be kept for reference but can no longer be posted.`,
      acceptLabel: 'Cancel receipt',
      rejectLabel: 'Keep',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.run(async () => this.apply(await this.api.cancel(r.id))),
    });
  }

  protected async load(id: string): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.apply(await this.api.getOne(id));
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  private apply(r: ReceiptDto): void {
    this.receipt.set(r);
    this.lines.clear();
    for (const l of r.lines) {
      this.lines.push(this.newLine({
        productId: l.productId, productUnitId: l.productUnitId, quantity: l.enteredQuantity, expectedQuantity: l.expectedQuantity,
        locationId: l.locationId, lotNo: l.lotNo, expiryDate: fromIsoDate(l.expiryDate), notes: l.notes,
      }));
    }
    if (this.lines.length === 0 && r.status === 'draft') this.addLine();
    this.form.patchValue({ receiptDate: fromIsoDate(r.receiptDate), supplierName: r.supplierName, supplierReference: r.supplierReference, notes: r.notes });
    this.form.markAsPristine();
    if (!this.editable()) this.form.disable();
    else this.form.enable();
  }

  private newLine(v: Partial<LineForm['value']> = {}): LineForm {
    return this.fb.group({
      productId: this.fb.control<string | null>(v.productId ?? null, Validators.required),
      productUnitId: this.fb.control<string | null>(v.productUnitId ?? null),
      quantity: this.fb.control<number | null>(v.quantity ?? null, [Validators.required, Validators.min(0.001)]),
      expectedQuantity: this.fb.control<number | null>(v.expectedQuantity ?? null, Validators.min(0)),
      locationId: this.fb.control<string | null>(v.locationId ?? null),
      lotNo: this.fb.control<string | null>(v.lotNo ?? null, Validators.maxLength(40)),
      expiryDate: this.fb.control<Date | null>(v.expiryDate ?? null),
      notes: this.fb.control<string | null>(v.notes ?? null, Validators.maxLength(500)),
    });
  }

  private toInput(): ReceiptInput {
    const v = this.form.getRawValue();
    return {
      receiptDate: toIsoDate(v.receiptDate),
      supplierName: v.supplierName?.trim() || null,
      supplierReference: v.supplierReference?.trim() || null,
      notes: v.notes?.trim() || null,
      lines: v.lines.map((l) => ({
        productId: l.productId!,
        productUnitId: l.productUnitId,
        quantity: l.quantity!,
        expectedQuantity: l.expectedQuantity,
        locationId: l.locationId,
        lotNo: l.lotNo?.trim() || null,
        expiryDate: toIsoDate(l.expiryDate),
        notes: l.notes?.trim() || null,
      })),
    };
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
