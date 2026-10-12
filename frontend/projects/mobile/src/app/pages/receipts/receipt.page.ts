import { Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NavController } from '@ionic/angular';
import {
  IonBackButton, IonButton, IonButtons, IonContent, IonHeader, IonIcon, IonInput, IonSpinner, IonTitle, IonToolbar,
} from '@ionic/angular';
import { addIcons } from 'ionicons';
import { trashOutline } from 'ionicons/icons';
import {
  ApiError, AuthService, LocationDto, newIdempotencyKey, Permissions, ProductDto, ProductsApi, qty, ReceiptDto, ReceiptInput, ReceiptsApi, toIsoDate,
  WarehouseContextService, WarehousesApi,
} from '@wh/shared';
import { Feedback } from '../../core/feedback';
import { QtyStepper } from '../../ui/qty-stepper';
import { ScanField } from '../../ui/scan-field';

/** A receipt line while it is edited on the phone. */
export interface ReceiptLineForm {
  key: string;
  product: Pick<ProductDto, 'id' | 'sku' | 'name' | 'baseUnitCode' | 'isLotTracked' | 'hasExpiry' | 'units'>;
  productUnitId: string | null;
  quantity: number | null;
  expected: number | null;
  locationId: string | null;
  lotNo: string;
  expiryDate: string;
  notes: string | null;
}

/**
 * Count a delivery: scan each product (or carton) to add it or add one more, set bin, lot and expiry, save the draft,
 * and post it (adds the stock) when allowed. Posting is idempotent: a retry after a lost connection cannot add the stock twice.
 */
@Component({
  selector: 'wh-receipt',
  imports: [FormsModule, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonButton, IonIcon, IonContent, IonInput, IonSpinner, QtyStepper, ScanField],
  templateUrl: './receipt.page.html',
  styleUrl: './receipt.page.scss',
})
export class ReceiptPage {
  private readonly api = inject(ReceiptsApi);
  private readonly products = inject(ProductsApi);
  private readonly warehouses = inject(WarehousesApi);
  private readonly ctx = inject(WarehouseContextService);
  private readonly feedback = inject(Feedback);
  private readonly nav = inject(NavController);
  private readonly location = inject(Location);
  private readonly auth = inject(AuthService);
  protected readonly qty = qty;
  private readonly scanField = viewChild<ScanField>('scan');

  readonly id = input.required<string>();

  protected readonly receipt = signal<ReceiptDto | null>(null);
  protected readonly lines = signal<ReceiptLineForm[]>([]);
  protected readonly bins = signal<LocationDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly dirty = signal(false);
  protected readonly lastKey = signal<string | null>(null);
  protected supplierName = '';
  protected supplierReference = '';

  protected readonly isNew = computed(() => this.id() === 'new');
  protected readonly editable = computed(() => this.isNew() || this.receipt()?.status === 'draft');
  protected readonly canPost = this.auth.hasPermission(Permissions.InventoryReceivePost);
  protected readonly canEdit = this.auth.hasPermission(Permissions.InventoryReceive);
  private readonly productCache = new Map<string, ProductDto>();
  private postKey = newIdempotencyKey();

  constructor() {
    addIcons({ trashOutline });
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    const wh = this.ctx.current();
    if (!wh) return;
    this.loading.set(true);
    try {
      const [bins, receipt] = await Promise.all([
        this.warehouses.locations(wh.id, { pageSize: 200, sortBy: 'code', isActive: true }),
        id === 'new' ? Promise.resolve(null) : this.api.getOne(id),
      ]);
      this.bins.set(bins.items);
      if (receipt) await this.apply(receipt);
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.loading.set(false);
    }
  }

  private async apply(r: ReceiptDto): Promise<void> {
    this.receipt.set(r);
    this.supplierName = r.supplierName ?? '';
    this.supplierReference = r.supplierReference ?? '';
    const products = await Promise.all([...new Set(r.lines.map((l) => l.productId))].map((pid) => this.product(pid)));
    const byId = new Map(products.map((p) => [p.id, p]));
    this.lines.set(r.lines.map((l) => ({
      key: l.id, product: byId.get(l.productId)!, productUnitId: l.productUnitId, quantity: l.enteredQuantity, expected: l.expectedQuantity,
      locationId: l.locationId, lotNo: l.lotNo ?? '', expiryDate: l.expiryDate ?? '', notes: l.notes,
    })));
    this.dirty.set(false);
  }

  private async product(id: string): Promise<ProductDto> {
    let p = this.productCache.get(id);
    if (!p) {
      p = await this.products.getOne(id);
      this.productCache.set(id, p);
    }
    return p;
  }

  // ---- Scanning ------------------------------------------------------------------------------

  /** Scans are handled one after another, so quick successive scans (two cartons in a row) are all counted. */
  private queue: Promise<void> = Promise.resolve();

  protected onScan(code: string): Promise<void> {
    this.queue = this.queue.then(() => this.handleScan(code));
    return this.queue;
  }

  private async handleScan(code: string): Promise<void> {
    if (!this.editable()) return;
    this.busy.set(true);
    try {
      const lookup = await this.products.lookupBarcode(code);
      if (!lookup.isActive) {
        await this.feedback.warn(`${lookup.sku} is inactive and cannot be received.`);
        return;
      }
      const product = await this.product(lookup.productId);
      const unit = lookup.factorToBase === 1 ? null : (product.units.find((u) => u.unitCode === lookup.unitCode)?.id ?? null);
      const existing = this.lines().find((l) => l.product.id === product.id && l.productUnitId === unit);
      if (existing) {
        this.update(existing.key, { quantity: (existing.quantity ?? 0) + 1 });
        this.lastKey.set(existing.key);
        await this.feedback.success(`${product.sku}: ${qty((existing.quantity ?? 0) + 1)} ${lookup.unitCode}.`);
      } else {
        const key = newIdempotencyKey();
        this.lines.update((ls) => [...ls, {
          key, product, productUnitId: unit, quantity: 1, expected: null, locationId: null, lotNo: '', expiryDate: '', notes: null,
        }]);
        this.dirty.set(true);
        this.lastKey.set(key);
        await this.feedback.success(`${product.sku} added.`);
      }
    } catch (e) {
      if (ApiError.from(e).kind === 'not-found') await this.feedback.warn(`Unknown barcode ${code}. Ask the office to add it to the product.`);
      else await this.feedback.error(e);
    } finally {
      this.busy.set(false);
      this.scanField()?.focus();
    }
  }

  protected onBin(line: ReceiptLineForm, code: string): void {
    const upper = code.toUpperCase();
    const bin = this.bins().find((b) => b.code.toUpperCase() === upper || b.barcode?.toUpperCase() === upper);
    if (!bin) {
      void this.feedback.warn(`Unknown bin ${code}.`);
      return;
    }
    this.update(line.key, { locationId: bin.id });
  }

  protected binCode(line: ReceiptLineForm): string {
    return this.bins().find((b) => b.id === line.locationId)?.code ?? '';
  }

  protected unitLabel(line: ReceiptLineForm): string {
    const u = line.product.units.find((x) => x.id === line.productUnitId);
    return u ? `${u.unitCode} (×${qty(u.factorToBase)})` : line.product.baseUnitCode;
  }

  protected update(key: string, change: Partial<ReceiptLineForm>): void {
    this.lines.update((ls) => ls.map((l) => (l.key === key ? { ...l, ...change } : l)));
    this.dirty.set(true);
  }

  protected async remove(line: ReceiptLineForm): Promise<void> {
    const ok = await this.feedback.confirm('Remove line', `Remove ${line.product.sku} from this receipt?`, 'Remove');
    if (!ok) return;
    this.lines.update((ls) => ls.filter((l) => l.key !== line.key));
    this.dirty.set(true);
  }

  // ---- Save and post -------------------------------------------------------------------------

  private problems(forPost: boolean): string[] {
    const out: string[] = [];
    if (this.lines().length === 0) out.push('Scan at least one product.');
    for (const l of this.lines()) {
      if (!l.quantity || l.quantity <= 0) out.push(`${l.product.sku}: enter the quantity or remove the line.`);
      if (forPost && l.product.isLotTracked && !l.lotNo.trim()) out.push(`${l.product.sku}: enter the lot number.`);
      if (forPost && l.product.hasExpiry && !l.expiryDate) out.push(`${l.product.sku}: enter the expiry date.`);
    }
    return out;
  }

  private toInput(): ReceiptInput {
    return {
      receiptDate: this.receipt()?.receiptDate ?? toIsoDate(new Date()),
      supplierName: this.supplierName.trim() || null,
      supplierReference: this.supplierReference.trim() || null,
      notes: this.receipt()?.notes ?? null,
      lines: this.lines().map((l) => ({
        productId: l.product.id, productUnitId: l.productUnitId, quantity: l.quantity ?? 0, expectedQuantity: l.expected, locationId: l.locationId,
        lotNo: l.product.isLotTracked ? l.lotNo.trim() || null : null, expiryDate: l.product.hasExpiry ? l.expiryDate || null : null, notes: l.notes,
      })),
    };
  }

  protected async save(): Promise<ReceiptDto | null> {
    const problems = this.problems(false);
    if (problems.length > 0) {
      await this.feedback.warn(problems[0]!);
      return null;
    }
    const wh = this.ctx.current();
    if (!wh) return null;
    this.busy.set(true);
    try {
      const current = this.receipt();
      const body = this.toInput();
      const saved = current
        ? await this.api.update(current.id, { ...body, receiptDate: body.receiptDate!, version: current.version })
        : await this.api.create(wh.id, body);
      await this.apply(saved);
      // Show the real address without reloading the page (a reload of /receipts/new would start a new receipt).
      if (!current) this.location.replaceState(`/receipts/${saved.id}`);
      await this.feedback.success(current ? 'Saved.' : `Receipt ${saved.number} saved as draft.`);
      return saved;
    } catch (e) {
      await this.feedback.error(e);
      return null;
    } finally {
      this.busy.set(false);
    }
  }

  protected async post(): Promise<void> {
    const problems = this.problems(true);
    if (problems.length > 0) {
      await this.feedback.warn(problems[0]!);
      return;
    }
    const total = this.lines().length;
    const ok = await this.feedback.confirm('Post receipt', `Add these ${total} line(s) to stock now? A posted receipt can only be reversed by the office.`, 'Post');
    if (!ok) return;
    const saved = this.dirty() || !this.receipt() ? await this.save() : this.receipt();
    if (!saved) return;
    this.busy.set(true);
    try {
      const posted = await this.api.postReceipt(saved.id, { key: this.postKey });
      this.postKey = newIdempotencyKey();
      this.receipt.set(posted);
      await this.feedback.success(`${posted.number} posted. Stock updated.`);
      await this.nav.navigateBack('/receipts');
    } catch (e) {
      await this.feedback.error(e, { retrySafe: ApiError.from(e).kind === 'network' });
    } finally {
      this.busy.set(false);
    }
  }
}
