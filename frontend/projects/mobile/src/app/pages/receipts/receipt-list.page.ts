import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  IonBackButton, IonButton, IonButtons, IonContent, IonHeader, IonIcon, IonItem, IonLabel, IonList, IonNote, IonRefresher, IonRefresherContent, IonSpinner,
  IonTitle, IonToolbar, ViewWillEnter,
} from '@ionic/angular';
import { addIcons } from 'ionicons';
import { addOutline } from 'ionicons/icons';
import { ApiError, AuthService, Permissions, ReceiptListItemDto, ReceiptsApi, WarehouseContextService } from '@wh/shared';

/** Draft goods receipts waiting to be counted, plus the ones posted today. */
@Component({
  selector: 'wh-receipt-list',
  imports: [DatePipe, RouterLink, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonButton, IonIcon, IonContent, IonList, IonItem, IonLabel,
    IonSpinner, IonRefresher, IonRefresherContent],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-buttons slot="start"><ion-back-button defaultHref="/" /></ion-buttons>
        <ion-title>Receive goods</ion-title>
        @if (canCreate) {
          <ion-buttons slot="end"><ion-button routerLink="/receipts/new" aria-label="New receipt"><ion-icon slot="icon-only" name="add-outline" /></ion-button></ion-buttons>
        }
      </ion-toolbar>
    </ion-header>
    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="load($event)"><ion-refresher-content /></ion-refresher>
      @if (error(); as e) {
        <p class="wh-empty">{{ e }}</p>
      } @else if (loading() && drafts().length === 0) {
        <div class="wh-empty"><ion-spinner /></div>
      } @else {
        <div class="wh-section"><h2>To count (drafts)</h2></div>
        <ion-list lines="full">
          @for (r of drafts(); track r.id) {
            <ion-item button detail [routerLink]="['/receipts', r.id]">
              <ion-label>
                <h2>{{ r.number }}</h2>
                <p>{{ r.supplierName ?? 'No supplier' }}@if (r.supplierReference) { · {{ r.supplierReference }} }</p>
                <p>{{ r.receiptDate | date: 'dd.MM.yyyy' }} · {{ r.lineCount }} line(s)</p>
              </ion-label>
            </ion-item>
          } @empty {
            <p class="wh-empty">No draft receipts.@if (canCreate) { Tap + to receive a delivery. }</p>
          }
        </ion-list>
        @if (canCreate) {
          <div class="wh-actions"><ion-button expand="block" routerLink="/receipts/new" data-testid="receipt-new">New receipt</ion-button></div>
        }
      }
    </ion-content>
  `,
})
export class ReceiptListPage implements ViewWillEnter {
  private readonly api = inject(ReceiptsApi);
  private readonly ctx = inject(WarehouseContextService);
  protected readonly canCreate = inject(AuthService).hasPermission(Permissions.InventoryReceive);
  protected readonly drafts = signal<ReceiptListItemDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    addIcons({ addOutline });
  }

  ionViewWillEnter(): void {
    void this.load();
  }

  protected async load(event?: CustomEvent): Promise<void> {
    const wh = this.ctx.current();
    if (!wh) return;
    this.loading.set(true);
    try {
      this.drafts.set((await this.api.list({ warehouseId: wh.id, status: 'draft', pageSize: 100, sortBy: 'receiptDate', sortDir: 'desc' })).items);
      this.error.set(null);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
      await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
    }
  }
}
