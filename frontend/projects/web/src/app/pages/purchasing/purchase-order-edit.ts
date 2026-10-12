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
import { Tag } from 'primeng/tag';
import { TableModule } from 'primeng/table';
import {
  ApiError, AuthService, Permissions, ProductDto, PurchaseOrderDto, PurchaseOrderInput, PurchaseOrdersApi, SupplierListItemDto, SuppliersApi,
  WarehouseContextService,
} from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { DocStatus } from '../../ui/doc-status';
import { FieldError } from '../../ui/field-error';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { ProductPicker } from '../../ui/product-picker';
import { fromIsoDate, qty, toIsoDate } from '../../ui/format';

type LineForm = FormGroup<{
  productId: FormControl<string | null>;
  productUnitId: FormControl<string | null>;
  quantity: FormControl<number | null>;
  notes: FormControl<string | null>;
}>;

/**
 * Create and edit a purchase order (draft), place it, and follow it: lines with ordered / received / outstanding quantities,
 * the receipts made against it, receiving the rest, closing short or cancelling.
 */
@Component({
  selector: 'wh-purchase-order-edit',
  imports: [
    DatePipe, FormsModule, ReactiveFormsModule, RouterLink, Button, InputText, InputNumber, Select, DatePicker, Textarea, Dialog, Skeleton, Tag, TableModule,
    DocStatus, FieldError, LoadError, NeedWarehouse, ProductPicker,
  ],
  templateUrl: './purchase-order-edit.html',
  styleUrl: '../../ui/line-editor.scss',
  styles: `
    .receipts { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.5rem; }
    .strong { font-weight: 600; }
    .dialog-fields { display: grid; gap: 1rem; }
    h1 p-tag { vertical-align: middle; margin-left: 0.25rem; }
  `,
})
export class PurchaseOrderEdit {
  private readonly api = inject(PurchaseOrdersApi);
  private readonly suppliersApi = inject(SuppliersApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canManage = this.auth.hasPermission(Permissions.PurchasingManage);
  protected readonly canReceive = this.auth.hasPermission(Permissions.InventoryReceive);
  protected readonly qty = qty;

  /** Route parameter: an id, or undefined / "new". */
  readonly id = input<string>();

  protected readonly order = signal<PurchaseOrderDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly suppliers = signal<SupplierListItemDto[]>([]);
  protected readonly products = signal<Record<string, ProductDto>>({});
  protected readonly dialog = signal<'close' | 'cancel' | 'details' | null>(null);
  protected reason = '';
  protected details: { expectedDate: Date | null; supplierReference: string; notes: string } = { expectedDate: null, supplierReference: '', notes: '' };

  private readonly fb = inject(FormBuilder);
  protected readonly form = this.fb.group({
    supplierId: this.fb.control<string | null>(null, Validators.required),
    orderDate: this.fb.control<Date | null>(new Date(), Validators.required),
    expectedDate: this.fb.control<Date | null>(null),
    supplierReference: this.fb.control<string | null>(null, Validators.maxLength(60)),
    notes: this.fb.control<string | null>(null, Validators.maxLength(2000)),
    lines: this.fb.array<LineForm>([]),
  });

  protected readonly isNew = computed(() => !this.id() || this.id() === 'new');
  protected readonly editable = computed(() => this.canManage && (this.isNew() || this.order()?.status === 'draft'));
  protected readonly isOpen = computed(() => ['ordered', 'partiallyReceived'].includes(this.order()?.status ?? ''));
  protected readonly hasOutstanding = computed(() => this.order()?.lines.some((l) => l.outstanding > 0) ?? false);
  protected readonly hasDraftReceipt = computed(() => this.order()?.receipts.some((r) => r.status === 'draft') ?? false);

  protected get lines(): FormArray<LineForm> {
    return this.form.controls.lines;
  }

  constructor() {
    void this.loadSuppliers();
    effect(() => {
      const id = this.id();
      if (id && id !== 'new') void this.load(id);
      else if (this.lines.length === 0) this.addLine();
    });
  }

  private async loadSuppliers(): Promise<void> {
    try {
      this.suppliers.set((await this.suppliersApi.list({ page: 1, pageSize: 200, isActive: true, sortBy: 'name' })).items);
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected supplierLabel(s: SupplierListItemDto): string {
    return `${s.name} (${s.code})`;
  }

  /** Proposes the expected date from the supplier's lead time when none is entered yet. */
  protected onSupplier(): void {
    const s = this.suppliers().find((x) => x.id === this.form.controls.supplierId.value);
    const orderDate = this.form.controls.orderDate.value;
    if (s?.leadTimeDays != null && orderDate && !this.form.controls.expectedDate.value) {
      const d = new Date(orderDate);
      d.setDate(d.getDate() + s.leadTimeDays);
      this.form.controls.expectedDate.setValue(d);
    }
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

  /** Base quantity of a line, for the "= 120 PK" hint. */
  protected baseQty(line: LineForm): number | null {
    const p = this.product(line);
    const q = line.controls.quantity.value;
    if (!p || q == null) return null;
    const unit = p.units.find((u) => u.id === line.controls.productUnitId.value);
    return unit ? q * unit.factorToBase : null;
  }

  protected onProduct(line: LineForm, p: ProductDto | null): void {
    if (p) this.products.update((m) => ({ ...m, [p.id]: p }));
    if (!p) {
      line.controls.productUnitId.setValue(null);
      return;
    }
    if (!p.units.some((u) => u.id === line.controls.productUnitId.value)) {
      // Default to the product's purchase unit (e.g. cartons).
      line.controls.productUnitId.setValue(p.units.find((u) => u.unitId === p.purchaseUnitId)?.id ?? null);
    }
    if (!this.form.controls.supplierId.value && p.defaultSupplierId && this.suppliers().some((s) => s.id === p.defaultSupplierId)) {
      this.form.controls.supplierId.setValue(p.defaultSupplierId);
      this.onSupplier();
    }
  }

  protected addLine(): void {
    this.lines.push(this.newLine());
  }

  protected removeLine(i: number): void {
    this.lines.removeAt(i);
    if (this.lines.length === 0) this.addLine();
  }

  protected async save(): Promise<PurchaseOrderDto | null> {
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
      const current = this.order();
      const saved = current
        ? await this.api.update(current.id, { ...body, orderDate: body.orderDate!, version: current.version })
        : await this.api.create(wh.id, body);
      this.feedback.success(current ? 'Order saved.' : `Purchase order ${saved.number} created.`);
      if (!current) await this.router.navigate(['/purchasing/orders', saved.id], { replaceUrl: true });
      this.apply(saved);
      return saved;
    } catch (e) {
      this.feedback.error(e, this.form);
      return null;
    } finally {
      this.busy.set(false);
    }
  }

  protected place(): void {
    this.confirm.confirm({
      header: 'Place order',
      message: 'The order is sent: its quantities count as "on order" and goods can be received against it. Lines can no longer be changed.',
      acceptLabel: 'Place order',
      rejectLabel: 'Not yet',
      accept: async () => {
        const saved = this.form.dirty || !this.order() ? await this.save() : this.order();
        if (!saved) return;
        await this.run(async () => {
          this.apply(await this.api.place(saved.id));
          this.feedback.success(`Order ${saved.number} placed.`);
        });
      },
    });
  }

  /** Creates a draft receipt for everything still outstanding and opens it. */
  protected async receive(): Promise<void> {
    const o = this.order();
    if (!o) return;
    await this.run(async () => {
      const receipt = await this.api.createReceipt(o.id);
      this.feedback.success(`Receipt ${receipt.number} created: check the quantities, bins and lots, then post it.`);
      await this.router.navigate(['/receipts', receipt.id]);
    });
  }

  protected openDialog(kind: 'close' | 'cancel' | 'details'): void {
    const o = this.order();
    this.reason = '';
    if (kind === 'details' && o) {
      this.details = { expectedDate: fromIsoDate(o.expectedDate), supplierReference: o.supplierReference ?? '', notes: o.notes ?? '' };
    }
    this.dialog.set(kind);
  }

  protected async confirmDialog(): Promise<void> {
    const o = this.order();
    const kind = this.dialog();
    if (!o || !kind) return;
    await this.run(async () => {
      if (kind === 'close') {
        this.apply(await this.api.close(o.id, this.reason.trim()));
        this.feedback.success('Order closed: the rest is no longer expected.');
      } else if (kind === 'cancel') {
        this.apply(await this.api.cancel(o.id, this.reason.trim() || null));
        this.feedback.success('Order cancelled.');
      } else {
        this.apply(await this.api.update(o.id, {
          supplierId: o.supplierId, orderDate: o.orderDate, expectedDate: toIsoDate(this.details.expectedDate),
          supplierReference: this.details.supplierReference.trim() || null, notes: this.details.notes.trim() || null, lines: null, version: o.version,
        }));
        this.feedback.success('Order updated.');
      }
      this.dialog.set(null);
    });
  }

  protected cancelDraft(): void {
    const o = this.order();
    if (!o) return;
    this.confirm.confirm({
      header: 'Cancel draft',
      message: `Cancel purchase order ${o.number}? It will be kept for reference.`,
      acceptLabel: 'Cancel order',
      rejectLabel: 'Keep',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.run(async () => this.apply(await this.api.cancel(o.id, null))),
    });
  }

  /** Plain-text summary to paste into an email to the supplier. */
  protected async copyText(): Promise<void> {
    const o = this.order();
    if (!o) return;
    const lines = o.lines.map((l) => `- ${l.sku} ${l.productName}: ${qty(l.enteredQuantity)} ${l.unitCode}`).join('\n');
    const text = `Purchase order ${o.number} (${o.orderDate})\n${o.expectedDate ? `Requested delivery: ${o.expectedDate}\n` : ''}Deliver to: ${o.warehouseCode}\n\n${lines}\n${o.notes ? `\n${o.notes}\n` : ''}`;
    try {
      await navigator.clipboard.writeText(text);
      this.feedback.success('Order copied: paste it into an email to the supplier.');
    } catch {
      this.feedback.warn('Copying is not allowed in this browser.');
    }
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

  private apply(o: PurchaseOrderDto): void {
    this.order.set(o);
    this.lines.clear();
    for (const l of o.lines) {
      this.lines.push(this.newLine({ productId: l.productId, productUnitId: l.productUnitId, quantity: l.enteredQuantity, notes: l.notes }));
    }
    if (this.lines.length === 0 && o.status === 'draft') this.addLine();
    this.form.patchValue({
      supplierId: o.supplierId, orderDate: fromIsoDate(o.orderDate), expectedDate: fromIsoDate(o.expectedDate), supplierReference: o.supplierReference, notes: o.notes,
    });
    // An inactive supplier of an existing order still shows in the list.
    if (!this.suppliers().some((s) => s.id === o.supplierId)) {
      this.suppliers.update((list) => [...list, { id: o.supplierId, code: o.supplierCode, name: o.supplierName } as SupplierListItemDto]);
    }
    this.form.markAsPristine();
    if (!this.editable()) this.form.disable();
    else this.form.enable();
  }

  private newLine(v: Partial<LineForm['value']> = {}): LineForm {
    return this.fb.group({
      productId: this.fb.control<string | null>(v.productId ?? null, Validators.required),
      productUnitId: this.fb.control<string | null>(v.productUnitId ?? null),
      quantity: this.fb.control<number | null>(v.quantity ?? null, [Validators.required, Validators.min(0.001)]),
      notes: this.fb.control<string | null>(v.notes ?? null, Validators.maxLength(500)),
    });
  }

  private toInput(): PurchaseOrderInput {
    const v = this.form.getRawValue();
    return {
      supplierId: v.supplierId!,
      orderDate: toIsoDate(v.orderDate),
      expectedDate: toIsoDate(v.expectedDate),
      supplierReference: v.supplierReference?.trim() || null,
      notes: v.notes?.trim() || null,
      lines: v.lines.map((l) => ({ productId: l.productId!, productUnitId: l.productUnitId, quantity: l.quantity!, notes: l.notes?.trim() || null })),
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
