import { Component, forwardRef, inject, input, output, signal } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { AutoComplete, AutoCompleteCompleteEvent, AutoCompleteSelectEvent } from 'primeng/autocomplete';
import { ProductDto, ProductListItemDto, ProductsApi } from '@wh/shared';
import { Lookups } from './lookups';

interface Option {
  id: string;
  label: string;
}

/**
 * Product search (SKU, name or barcode) bound to a product id.
 * Emits the full product (units, lot tracking) whenever the selection changes.
 */
@Component({
  selector: 'wh-product-picker',
  imports: [FormsModule, AutoComplete],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => ProductPicker), multi: true }],
  template: `
    <p-autocomplete
      [inputId]="inputId()"
      [ngModel]="selected()"
      [suggestions]="suggestions()"
      (completeMethod)="search($event)"
      (onSelect)="pick($event)"
      (onClear)="clear()"
      optionLabel="label"
      [forceSelection]="true"
      [disabled]="disabled()"
      [placeholder]="placeholder()"
      [minLength]="1"
      [delay]="250"
      appendTo="body"
      [fluid]="true"
      [showClear]="true"
      (onBlur)="onTouched()"
    />
  `,
})
export class ProductPicker implements ControlValueAccessor {
  private readonly api = inject(ProductsApi);
  private readonly lookups = inject(Lookups);

  readonly inputId = input<string>();
  readonly placeholder = input('SKU, name or barcode');
  /** Only active products can be chosen for new lines. */
  readonly activeOnly = input(true);
  readonly productChange = output<ProductDto | null>();

  protected readonly selected = signal<Option | null>(null);
  protected readonly suggestions = signal<Option[]>([]);
  protected readonly disabled = signal(false);
  private onChange: (v: string | null) => void = () => {};
  protected onTouched: () => void = () => {};

  writeValue(id: string | null): void {
    if (!id) {
      this.selected.set(null);
      return;
    }
    if (this.selected()?.id === id) return;
    this.selected.set({ id, label: '…' });
    void this.lookups.product(id).then((p) => {
      this.selected.set({ id: p.id, label: `${p.sku} · ${p.name}` });
      this.productChange.emit(p);
    });
  }
  registerOnChange(fn: (v: string | null) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  protected async search(event: AutoCompleteCompleteEvent): Promise<void> {
    const page = await this.api.list({ search: event.query, pageSize: 15, sortBy: 'sku', isActive: this.activeOnly() ? true : undefined });
    this.suggestions.set(page.items.map((p: ProductListItemDto) => ({ id: p.id, label: `${p.sku} · ${p.name}` })));
  }

  protected async pick(event: AutoCompleteSelectEvent): Promise<void> {
    const option = event.value as Option;
    this.selected.set(option);
    this.onChange(option.id);
    this.productChange.emit(await this.lookups.product(option.id));
  }

  protected clear(): void {
    this.selected.set(null);
    this.onChange(null);
    this.productChange.emit(null);
  }
}
