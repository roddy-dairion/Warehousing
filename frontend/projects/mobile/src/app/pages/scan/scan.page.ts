import { Component, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  IonBackButton, IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonNote, IonSpinner, IonTitle, IonToolbar, ViewDidEnter,
} from '@ionic/angular';
import {
  ApiError, AuthService, BalanceDto, BarcodeLookupDto, InventoryApi, LocationDto, Permissions, ProductListItemDto, ProductsApi, ProductStockDto, qty,
  WarehouseContextService, WarehousesApi,
} from '@wh/shared';
import { Feedback } from '../../core/feedback';
import { BalanceList } from '../../ui/balance-list';
import { ScanField } from '../../ui/scan-field';

type Result =
  | { kind: 'product'; code: string; lookup: BarcodeLookupDto | null; stock: ProductStockDto | null; productId: string; sku: string; name: string; active: boolean }
  | { kind: 'bin'; code: string; bin: LocationDto; balances: BalanceDto[] }
  | { kind: 'search'; code: string; products: ProductListItemDto[] }
  | { kind: 'unknown'; code: string };

/**
 * Scan anything: a product or pack barcode shows the product and its stock in this warehouse; a bin label shows what
 * is in the bin; other text searches products by SKU or name.
 */
@Component({
  selector: 'wh-scan',
  imports: [RouterLink, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonButton, IonContent, IonList, IonItem, IonLabel, IonNote, IonSpinner,
    BalanceList, ScanField],
  templateUrl: './scan.page.html',
})
export class ScanPage implements ViewDidEnter {
  private readonly products = inject(ProductsApi);
  private readonly inventory = inject(InventoryApi);
  private readonly warehouses = inject(WarehousesApi);
  private readonly auth = inject(AuthService);
  private readonly feedback = inject(Feedback);
  protected readonly ctx = inject(WarehouseContextService);
  protected readonly qty = qty;
  private readonly field = viewChild.required(ScanField);

  protected readonly busy = signal(false);
  protected readonly result = signal<Result | null>(null);
  protected readonly canSeeStock = this.auth.hasPermission(Permissions.InventoryRead);

  ionViewDidEnter(): void {
    this.field().focus();
  }

  protected async lookup(code: string): Promise<void> {
    const wh = this.ctx.current();
    if (!wh || this.busy()) return;
    this.busy.set(true);
    try {
      this.result.set(await this.resolve(code, wh.id));
      const r = this.result();
      if (r?.kind === 'unknown') await this.feedback.warn(`Nothing found for "${code}".`);
      else if (r?.kind === 'product' && !r.active) await this.feedback.warn(`${r.sku} is inactive: it cannot be received or dispatched.`);
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
      this.field().focus();
    }
  }

  private async resolve(code: string, warehouseId: string): Promise<Result> {
    // 1. Product or pack barcode.
    const lookup = await this.products.lookupBarcode(code).catch((e: unknown) => (ApiError.from(e).kind === 'not-found' ? null : Promise.reject(e)));
    if (lookup) {
      const stock = this.canSeeStock ? await this.inventory.productStock(lookup.productId, warehouseId) : null;
      return { kind: 'product', code, lookup, stock, productId: lookup.productId, sku: lookup.sku, name: lookup.name, active: lookup.isActive };
    }
    // 2. Bin label or bin code.
    const upper = code.toUpperCase();
    const bins = await this.warehouses.locations(warehouseId, { search: code, pageSize: 10 }).catch(() => null);
    const bin = bins?.items.find((b) => b.code.toUpperCase() === upper || b.barcode?.toUpperCase() === upper);
    if (bin) {
      const balances = this.canSeeStock
        ? (await this.inventory.balances({ warehouseId, locationId: bin.id, onlyWithStock: true, pageSize: 200, sortBy: 'sku' })).items
        : [];
      return { kind: 'bin', code, bin, balances };
    }
    // 3. SKU or name search.
    const found = await this.products.list({ search: code, pageSize: 25 });
    if (found.items.length === 1) {
      const p = found.items[0]!;
      const stock = this.canSeeStock ? await this.inventory.productStock(p.id, warehouseId) : null;
      return { kind: 'product', code, lookup: null, stock, productId: p.id, sku: p.sku, name: p.name, active: p.isActive };
    }
    return found.items.length > 0 ? { kind: 'search', code, products: found.items } : { kind: 'unknown', code };
  }

  /** Balances of the product in the selected warehouse. */
  protected balancesHere(stock: ProductStockDto): BalanceDto[] {
    return stock.warehouses.find((w) => w.warehouseId === this.ctx.current()?.id)?.balances ?? [];
  }

  protected figuresHere(stock: ProductStockDto): { onHand: number; reserved: number; available: number } {
    return stock.warehouses.find((w) => w.warehouseId === this.ctx.current()?.id) ?? { onHand: 0, reserved: 0, available: 0 };
  }

  protected async open(p: ProductListItemDto): Promise<void> {
    await this.lookupProduct(p);
  }

  private async lookupProduct(p: ProductListItemDto): Promise<void> {
    const wh = this.ctx.current();
    if (!wh) return;
    this.busy.set(true);
    try {
      const stock = this.canSeeStock ? await this.inventory.productStock(p.id, wh.id) : null;
      this.result.set({ kind: 'product', code: p.sku, lookup: null, stock, productId: p.id, sku: p.sku, name: p.name, active: p.isActive });
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
