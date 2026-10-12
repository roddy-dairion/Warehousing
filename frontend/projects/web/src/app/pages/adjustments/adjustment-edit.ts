import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormArray, FormBuilder, FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
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
  ADJUSTMENT_REASONS, AdjustmentDto, AdjustmentReason, AdjustmentsApi, ApiError, AuthService, LocationDto, newIdempotencyKey, Permissions, ProductDto,
  WarehouseContextService,
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
  productId: FormControl<string | null>;
  locationId: FormControl<string | null>;
  lotNo: FormControl<string | null>;
  expiryDate: FormControl<Date | null>;
  quantityDelta: FormControl<number | null>;
  notes: FormControl<string | null>;
}>;

/** Request, review and approve a stock adjustment. */
@Component({
  selector: 'wh-adjustment-edit',
  imports: [
    DatePipe, FormsModule, ReactiveFormsModule, RouterLink, Button, InputText, InputNumber, Select, DatePicker, Textarea, Dialog, Skeleton, TableModule,
    DocStatus, FieldError, LoadError, NeedWarehouse, ProductPicker,
  ],
  templateUrl: './adjustment-edit.html',
  styleUrl: '../../ui/line-editor.scss',
})
export class AdjustmentEdit {
  private readonly api = inject(AdjustmentsApi);
  private readonly lookups = inject(Lookups);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canRequest = this.auth.hasPermission(Permissions.InventoryAdjust);
  protected readonly reasons = ADJUSTMENT_REASONS;
  protected readonly qty = qty;

  readonly id = input<string>();

  protected readonly adjustment = signal<AdjustmentDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly bins = signal<LocationDto[]>([]);
  protected readonly products = signal<Record<string, ProductDto>>({});
  protected readonly decision = signal<'approve' | 'reject' | null>(null);
  protected decisionText = '';
  private approveKey = newIdempotencyKey();

  private readonly fb = inject(FormBuilder);
  protected readonly form = this.fb.group({
    reason: this.fb.control<AdjustmentReason | null>(null, Validators.required),
    notes: this.fb.control<string | null>(null, Validators.maxLength(2000)),
    lines: this.fb.array<LineForm>([]),
  });

  protected readonly isNew = computed(() => !this.id() || this.id() === 'new');
  protected readonly editable = computed(() => this.canRequest && (this.isNew() || this.adjustment()?.status === 'draft'));

  protected get lines(): FormArray<LineForm> {
    return this.form.controls.lines;
  }

  constructor() {
    effect(() => {
      const wh = this.warehouses.current();
      if (wh) void this.lookups.binsOf(wh.id).then((b) => this.bins.set(b));
    });
    effect(() => {
      const id = this.id();
      if (id && id !== 'new') void this.load(id);
      else if (this.lines.length === 0) this.addLine();
    });
  }

  protected product(line: LineForm): ProductDto | null {
    const id = line.controls.productId.value;
    return id ? (this.products()[id] ?? null) : null;
  }

  protected onProduct(line: LineForm, p: ProductDto | null): void {
    if (p) this.products.update((m) => ({ ...m, [p.id]: p }));
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

  protected async save(): Promise<AdjustmentDto | null> {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.feedback.warn('Check the highlighted fields.');
      return null;
    }
    const wh = this.warehouses.current();
    if (!wh) return null;
    this.busy.set(true);
    try {
      const v = this.form.getRawValue();
      const lines = v.lines.map((l) => ({
        productId: l.productId!, locationId: l.locationId!, lotNo: l.lotNo?.trim() || null,
        expiryDate: (l.quantityDelta ?? 0) > 0 ? toIsoDate(l.expiryDate) : null, quantityDelta: l.quantityDelta!, notes: l.notes?.trim() || null,
      }));
      const current = this.adjustment();
      const saved = current
        ? await this.api.update(current.id, { reason: v.reason!, notes: v.notes?.trim() || null, lines, version: current.version })
        : await this.api.create({ warehouseId: wh.id, reason: v.reason!, notes: v.notes?.trim() || null, lines });
      this.feedback.success(current ? 'Adjustment saved.' : `Adjustment ${saved.number} created.`);
      if (!current) await this.router.navigate(['/adjustments', saved.id], { replaceUrl: true });
      this.apply(saved);
      return saved;
    } catch (e) {
      this.feedback.error(e, this.form);
      return null;
    } finally {
      this.busy.set(false);
    }
  }

  protected async submit(): Promise<void> {
    const saved = this.form.dirty || !this.adjustment() ? await this.save() : this.adjustment();
    if (!saved) return;
    await this.run(async () => {
      this.apply(await this.api.submit(saved.id));
      this.feedback.success('Sent for approval.');
    });
  }

  protected async decide(): Promise<void> {
    const a = this.adjustment();
    const d = this.decision();
    if (!a || !d) return;
    await this.run(async () => {
      if (d === 'approve') {
        this.apply(await this.api.approve(a.id, this.decisionText.trim() || null, { key: this.approveKey }));
        this.approveKey = newIdempotencyKey();
        this.feedback.success('Approved. Stock updated.');
      } else {
        this.apply(await this.api.reject(a.id, this.decisionText.trim()));
        this.feedback.success('Adjustment rejected.');
      }
      this.decision.set(null);
    });
  }

  protected reopen(): void {
    const a = this.adjustment();
    if (a) void this.run(async () => this.apply(await this.api.reopen(a.id)));
  }

  protected cancel(): void {
    const a = this.adjustment();
    if (!a) return;
    this.confirm.confirm({
      header: 'Cancel adjustment',
      message: `Cancel ${a.number}? Stock is not changed.`,
      acceptLabel: 'Cancel adjustment',
      rejectLabel: 'Keep',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.run(async () => this.apply(await this.api.cancel(a.id))),
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

  private apply(a: AdjustmentDto): void {
    this.adjustment.set(a);
    this.lines.clear();
    for (const l of a.lines) {
      this.lines.push(this.newLine({ productId: l.productId, locationId: l.locationId, lotNo: l.lotNo, expiryDate: fromIsoDate(l.expiryDate), quantityDelta: l.quantityDelta, notes: l.notes }));
    }
    if (this.lines.length === 0 && a.status === 'draft') this.addLine();
    this.form.patchValue({ reason: a.reason, notes: a.notes });
    this.form.markAsPristine();
    if (!this.editable()) this.form.disable();
    else this.form.enable();
  }

  private newLine(v: Partial<LineForm['value']> = {}): LineForm {
    return this.fb.group({
      productId: this.fb.control<string | null>(v.productId ?? null, Validators.required),
      locationId: this.fb.control<string | null>(v.locationId ?? null, Validators.required),
      lotNo: this.fb.control<string | null>(v.lotNo ?? null, Validators.maxLength(40)),
      expiryDate: this.fb.control<Date | null>(v.expiryDate ?? null),
      quantityDelta: this.fb.control<number | null>(v.quantityDelta ?? null, Validators.required),
      notes: this.fb.control<string | null>(v.notes ?? null, Validators.maxLength(500)),
    });
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
