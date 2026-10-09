import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Select } from 'primeng/select';
import { Message } from 'primeng/message';
import { AuthService, ImportKind, ImportResultDto, ImportsApi, Permissions } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';

interface KindOption {
  value: ImportKind;
  label: string;
  permission: string;
  help: string;
}

const KINDS: KindOption[] = [
  { value: 'products', label: 'Products', permission: Permissions.ProductsManage, help: 'One row per product. Existing SKUs are updated; new SKUs are created. Add a pack size and barcodes in the same row.' },
  { value: 'customers', label: 'Customers', permission: Permissions.CustomersManage, help: 'One row per delivery address. Repeat the reference on several rows to add several addresses.' },
  { value: 'locations', label: 'Bins', permission: Permissions.WarehousesManage, help: 'One row per bin. Existing bin codes in the same warehouse are updated.' },
];

@Component({
  selector: 'wh-import-page',
  imports: [FormsModule, TableModule, Button, Select, Message],
  templateUrl: './import-page.html',
})
export class ImportPage implements OnInit {
  private readonly api = inject(ImportsApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly auth = inject(AuthService);

  /** Pre-selected kind from the query string (?kind=products). */
  readonly kind = input<ImportKind>();

  protected readonly kinds = KINDS.filter((k) => this.auth.hasPermission(k.permission));
  protected selected = signal<ImportKind>('products');
  protected readonly option = computed(() => KINDS.find((k) => k.value === this.selected())!);
  protected readonly file = signal<File | null>(null);
  protected readonly result = signal<ImportResultDto | null>(null);
  protected readonly working = signal(false);

  ngOnInit(): void {
    const k = this.kind();
    const first = this.kinds[0];
    this.selected.set(k && this.kinds.some((x) => x.value === k) ? k : (first?.value ?? 'products'));
  }

  protected changeKind(kind: ImportKind): void {
    this.selected.set(kind);
    this.reset();
  }

  protected async downloadTemplate(): Promise<void> {
    try {
      const blob = await this.api.template(this.selected());
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${this.selected()}-template.csv`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
    this.result.set(null);
    input.value = '';
  }

  /** Step 1: validate without saving. Step 2 (only after a clean check): import. */
  protected async run(dryRun: boolean): Promise<void> {
    const file = this.file();
    if (!file) return;
    this.working.set(true);
    try {
      const result = await this.api.run(this.selected(), file, dryRun);
      this.result.set(result);
      if (result.applied) this.feedback.success(`Imported: ${result.created} created, ${result.updated} updated.`);
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.working.set(false);
    }
  }

  protected reset(): void {
    this.file.set(null);
    this.result.set(null);
  }
}
