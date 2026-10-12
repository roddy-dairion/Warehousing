import { Component, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  IonBackButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonNote, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar,
  ViewWillEnter,
} from '@ionic/angular';
import { ApiError, BalanceDto, InventoryApi, MOVEMENT_TYPE_LABELS, MovementDto, ProductStockDto, qty, WarehouseContextService } from '@wh/shared';
import { BalanceList } from '../../ui/balance-list';

/** One product: stock in the selected warehouse by bin and lot, and its latest movements. */
@Component({
  selector: 'wh-product-stock',
  imports: [DatePipe, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonContent, IonList, IonItem, IonLabel, IonNote, IonSpinner, IonRefresher,
    IonRefresherContent, BalanceList],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-buttons slot="start"><ion-back-button defaultHref="/scan" /></ion-buttons>
        <ion-title>{{ stock()?.sku ?? 'Product' }}</ion-title>
      </ion-toolbar>
    </ion-header>
    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="load($event)"><ion-refresher-content /></ion-refresher>
      @if (error(); as e) {
        <p class="wh-empty">{{ e }}</p>
      } @else if (stock(); as s) {
        <div class="wh-card target">
          <div class="wh-big">{{ s.sku }}</div>
          <div>{{ s.productName }}</div>
          @if (!s.isActive) { <p class="inactive"><strong>Inactive product</strong></p> }
          @if (s.minStockQty !== null) { <p class="wh-muted">Minimum stock {{ qty(s.minStockQty) }} {{ s.baseUnitCode }}</p> }
        </div>
        <div class="wh-figures">
          <div><strong class="wh-num">{{ qty(here().onHand) }}</strong><span>on hand</span></div>
          <div><strong class="wh-num">{{ qty(here().reserved) }}</strong><span>reserved</span></div>
          <div><strong class="wh-num">{{ qty(here().available) }}</strong><span>available {{ s.baseUnitCode }}</span></div>
        </div>
        <div class="wh-section"><h2>By bin and lot</h2></div>
        <wh-balance-list [balances]="here().balances" />
        <div class="wh-section"><h2>Latest movements</h2></div>
        <ion-list lines="full">
          @for (m of movements(); track m.id) {
            <ion-item>
              <ion-label>
                <h3>{{ label(m) }} · {{ m.locationCode }}@if (m.lotNo) { · lot {{ m.lotNo }} }</h3>
                <p>{{ m.occurredAt | date: 'dd.MM.yyyy HH:mm' }}@if (m.sourceNumber) { · {{ m.sourceNumber }} }@if (m.createdByName) { · {{ m.createdByName }} }</p>
              </ion-label>
              <ion-note slot="end" class="wh-num" [class.out]="m.quantity < 0">{{ m.quantity > 0 ? '+' : '' }}{{ qty(m.quantity) }}</ion-note>
            </ion-item>
          } @empty {
            <p class="wh-empty">No movements yet.</p>
          }
        </ion-list>
      } @else {
        <div class="wh-empty"><ion-spinner /></div>
      }
    </ion-content>
  `,
  styles: `
    ion-note { font-size: 1rem; color: var(--ion-color-success-shade); }
    ion-note.out { color: var(--ion-color-danger); }
  `,
})
export class ProductStockPage implements ViewWillEnter {
  private readonly inventory = inject(InventoryApi);
  private readonly ctx = inject(WarehouseContextService);
  protected readonly qty = qty;

  readonly id = input.required<string>();
  protected readonly stock = signal<ProductStockDto | null>(null);
  protected readonly movements = signal<MovementDto[]>([]);
  protected readonly error = signal<string | null>(null);

  ionViewWillEnter(): void {
    void this.load();
  }

  protected here(): { onHand: number; reserved: number; available: number; balances: BalanceDto[] } {
    return this.stock()?.warehouses.find((w) => w.warehouseId === this.ctx.current()?.id) ?? { onHand: 0, reserved: 0, available: 0, balances: [] };
  }

  protected label(m: MovementDto): string {
    return MOVEMENT_TYPE_LABELS[m.type] ?? m.type;
  }

  protected async load(event?: CustomEvent): Promise<void> {
    const wh = this.ctx.current();
    if (!wh) return;
    try {
      const [stock, moves] = await Promise.all([
        this.inventory.productStock(this.id(), wh.id),
        this.inventory.movements({ warehouseId: wh.id, productId: this.id(), pageSize: 25, sortBy: 'occurredAt', sortDir: 'desc' }),
      ]);
      this.stock.set(stock);
      this.movements.set(moves.items);
      this.error.set(null);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
    }
  }
}
