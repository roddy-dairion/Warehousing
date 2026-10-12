import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormArray, FormBuilder, FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AutoComplete, AutoCompleteCompleteEvent, AutoCompleteSelectEvent } from 'primeng/autocomplete';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { Textarea } from 'primeng/textarea';
import { Skeleton } from 'primeng/skeleton';
import {
  ApiError, CustomerDto, CustomersApi, DispatchDto, DispatchesApi, DispatchInput, InventoryApi, ProductDto, WarehouseContextService,
} from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
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

interface CustomerOption {
  id: string;
  label: string;
}

/** Create or edit a draft dispatch. Workflow actions happen on the detail page. */
@Component({
  selector: 'wh-dispatch-edit',
  imports: [FormsModule, ReactiveFormsModule, RouterLink, AutoComplete, Button, InputText, InputNumber, Select, DatePicker, Textarea, Skeleton, FieldError, LoadError, NeedWarehouse, ProductPicker],
  templateUrl: './dispatch-edit.html',
  styleUrl: '../../ui/line-editor.scss',
})
export class DispatchEdit {
  private readonly api = inject(DispatchesApi);
  private readonly customers = inject(CustomersApi);
  private readonly inventory = inject(InventoryApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly qty = qty;

  /** Route parameter: "new" or the id of a draft to edit (…/:id/edit). */
  readonly id = input<string>();

  protected readonly dispatch = signal<DispatchDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly customer = signal<CustomerDto | null>(null);
  protected readonly customerOption = signal<CustomerOption | null>(null);
  protected readonly customerSuggestions = signal<CustomerOption[]>([]);
  protected readonly products = signal<Record<string, ProductDto>>({});
  /** Available stock in the current warehouse per product id. */
  protected readonly available = signal<Record<string, number>>({});

  private readonly fb = inject(FormBuilder);
  protected readonly form = this.fb.group({
    customerId: this.fb.control<string | null>(null, Validators.required),
    deliveryAddressId: this.fb.control<string | null>(null),
    requestedDate: this.fb.control<Date | null>(new Date(), Validators.required),
    customerOrderReference: this.fb.control<string | null>(null, Validators.maxLength(60)),
    notes: this.fb.control<string | null>(null, Validators.maxLength(2000)),
    lines: this.fb.array<LineForm>([]),
  });

  protected readonly isNew = computed(() => !this.id() || this.id() === 'new');
  protected readonly addresses = computed(() => (this.customer()?.addresses ?? []).filter((a) => a.isActive)
    .map((a) => ({ id: a.id, label: `${a.label}${a.isDefault ? ' (default)' : ''} — ${a.line1}${a.city ? ', ' + a.city : ''}` })));

  protected get lines(): FormArray<LineForm> {
    return this.form.controls.lines;
  }

  constructor() {
    effect(() => {
      const id = this.id();
      if (id && id !== 'new') void this.load(id);
      else if (this.lines.length === 0) this.addLine();
    });
  }

  protected async searchCustomers(e: AutoCompleteCompleteEvent): Promise<void> {
    const page = await this.customers.list({ search: e.query, pageSize: 15, isActive: true, sortBy: 'companyName' });
    this.customerSuggestions.set(page.items.map((c) => ({ id: c.id, label: `${c.companyName} (${c.reference})` })));
  }

  protected async pickCustomer(e: AutoCompleteSelectEvent): Promise<void> {
    const option = e.value as CustomerOption;
    this.customerOption.set(option);
    this.form.controls.customerId.setValue(option.id);
    const c = await this.customers.getOne(option.id);
    this.customer.set(c);
    this.form.controls.deliveryAddressId.setValue(c.addresses.find((a) => a.isDefault && a.isActive)?.id ?? null);
  }

  protected clearCustomer(): void {
    this.customerOption.set(null);
    this.customer.set(null);
    this.form.patchValue({ customerId: null, deliveryAddressId: null });
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

  protected async onProduct(line: LineForm, p: ProductDto | null): Promise<void> {
    if (!p) return;
    this.products.update((m) => ({ ...m, [p.id]: p }));
    if (!p.units.some((u) => u.id === line.controls.productUnitId.value)) line.controls.productUnitId.setValue(null);
    const wh = this.warehouses.current();
    if (wh && this.available()[p.id] === undefined) {
      try {
        const s = await this.inventory.productStock(p.id, wh.id);
        this.available.update((m) => ({ ...m, [p.id]: s.available }));
      } catch {
        /* stock hint is optional (needs inventory.read) */
      }
    }
  }

  protected addLine(): void {
    this.lines.push(this.newLine());
  }

  protected removeLine(i: number): void {
    this.lines.removeAt(i);
    if (this.lines.length === 0) this.addLine();
  }

  protected async save(submit: boolean): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.feedback.warn('Check the highlighted fields.');
      return;
    }
    const wh = this.warehouses.current();
    const current = this.dispatch();
    if (!wh && !current) return;
    this.busy.set(true);
    try {
      const body = this.toInput();
      let saved = current ? await this.api.update(current.id, { ...body, version: current.version }) : await this.api.create(wh!.id, body);
      if (submit) saved = await this.api.submit(saved.id);
      this.feedback.success(submit ? `Dispatch ${saved.number} submitted for approval.` : `Dispatch ${saved.number} saved.`);
      await this.router.navigate(['/dispatches', saved.id]);
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(id: string): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const d = await this.api.getOne(id);
      if (d.status !== 'draft') {
        await this.router.navigate(['/dispatches', id], { replaceUrl: true });
        return;
      }
      this.dispatch.set(d);
      this.customerOption.set({ id: d.customerId, label: `${d.customerName} (${d.customerReference})` });
      this.customers.getOne(d.customerId).then((c) => this.customer.set(c)).catch(() => undefined);
      this.lines.clear();
      for (const l of d.lines) this.lines.push(this.newLine({ productId: l.productId, productUnitId: l.productUnitId, quantity: l.enteredQuantity, notes: l.notes }));
      if (this.lines.length === 0) this.addLine();
      this.form.patchValue({
        customerId: d.customerId, deliveryAddressId: d.deliveryAddressId, requestedDate: fromIsoDate(d.requestedDate),
        customerOrderReference: d.customerOrderReference, notes: d.notes,
      });
      this.form.markAsPristine();
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  private newLine(v: Partial<LineForm['value']> = {}): LineForm {
    return this.fb.group({
      productId: this.fb.control<string | null>(v.productId ?? null, Validators.required),
      productUnitId: this.fb.control<string | null>(v.productUnitId ?? null),
      quantity: this.fb.control<number | null>(v.quantity ?? null, [Validators.required, Validators.min(0.001)]),
      notes: this.fb.control<string | null>(v.notes ?? null, Validators.maxLength(500)),
    });
  }

  private toInput(): DispatchInput {
    const v = this.form.getRawValue();
    return {
      customerId: v.customerId!,
      deliveryAddressId: v.deliveryAddressId,
      requestedDate: toIsoDate(v.requestedDate)!,
      customerOrderReference: v.customerOrderReference?.trim() || null,
      notes: v.notes?.trim() || null,
      lines: v.lines.map((l) => ({ productId: l.productId!, productUnitId: l.productUnitId, quantity: l.quantity!, notes: l.notes?.trim() || null })),
    };
  }
}
