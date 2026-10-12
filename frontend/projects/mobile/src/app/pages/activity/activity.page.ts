import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  IonBackButton, IonButtons, IonContent, IonHeader, IonInfiniteScroll, IonInfiniteScrollContent, IonItem, IonLabel, IonList, IonNote, IonRefresher,
  IonRefresherContent, IonTitle, IonToolbar, ViewWillEnter,
} from '@ionic/angular';
import { ApiError, InventoryApi, MOVEMENT_TYPE_LABELS, MovementDto, qty, WarehouseContextService } from '@wh/shared';

const PAGE = 30;

/** Latest stock movements in the selected warehouse (newest first). */
@Component({
  selector: 'wh-activity',
  imports: [DatePipe, RouterLink, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonContent, IonList, IonItem, IonLabel, IonNote, IonRefresher,
    IonRefresherContent, IonInfiniteScroll, IonInfiniteScrollContent],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-buttons slot="start"><ion-back-button defaultHref="/" /></ion-buttons>
        <ion-title>Recent activity</ion-title>
      </ion-toolbar>
    </ion-header>
    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="reload($event)"><ion-refresher-content /></ion-refresher>
      @if (error(); as e) { <p class="wh-empty">{{ e }}</p> }
      <ion-list lines="full">
        @for (m of items(); track m.id) {
          <ion-item button detail="false" [routerLink]="['/products', m.productId]">
            <ion-label>
              <h3>{{ m.sku }} · {{ label(m) }}</h3>
              <p>{{ m.productName }}</p>
              <p>{{ m.occurredAt | date: 'dd.MM HH:mm' }} · {{ m.locationCode }}@if (m.lotNo) { · lot {{ m.lotNo }} }@if (m.sourceNumber) { · {{ m.sourceNumber }} }@if (m.createdByName) { · {{ m.createdByName }} }</p>
            </ion-label>
            <ion-note slot="end" class="wh-num" [class.out]="m.quantity < 0">{{ m.quantity > 0 ? '+' : '' }}{{ qty(m.quantity) }}</ion-note>
          </ion-item>
        } @empty {
          @if (!loading()) { <p class="wh-empty">No stock movements yet.</p> }
        }
      </ion-list>
      <ion-infinite-scroll [disabled]="done()" (ionInfinite)="more($event)"><ion-infinite-scroll-content /></ion-infinite-scroll>
    </ion-content>
  `,
  styles: `
    ion-note { font-size: 1rem; color: var(--ion-color-success-shade); }
    ion-note.out { color: var(--ion-color-danger); }
  `,
})
export class ActivityPage implements ViewWillEnter {
  private readonly inventory = inject(InventoryApi);
  private readonly ctx = inject(WarehouseContextService);
  protected readonly qty = qty;
  protected readonly items = signal<MovementDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);
  private page = 0;

  ionViewWillEnter(): void {
    void this.reload();
  }

  protected label(m: MovementDto): string {
    return MOVEMENT_TYPE_LABELS[m.type] ?? m.type;
  }

  protected async reload(event?: CustomEvent): Promise<void> {
    this.page = 0;
    this.done.set(false);
    this.items.set([]);
    await this.fetch();
    await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
  }

  protected async more(event: CustomEvent): Promise<void> {
    await this.fetch();
    await (event.target as HTMLIonInfiniteScrollElement).complete();
  }

  private async fetch(): Promise<void> {
    const wh = this.ctx.current();
    if (!wh) return;
    this.loading.set(true);
    try {
      const result = await this.inventory.movements({ warehouseId: wh.id, page: this.page + 1, pageSize: PAGE, sortBy: 'occurredAt', sortDir: 'desc' });
      this.page++;
      this.items.update((x) => [...x, ...result.items]);
      this.done.set(this.items().length >= result.totalCount || result.items.length === 0);
      this.error.set(null);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
      this.done.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
