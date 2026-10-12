import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  IonBackButton, IonButton, IonButtons, IonContent, IonHeader, IonIcon, IonItem, IonLabel, IonList, IonNote, IonRefresher, IonRefresherContent, IonSpinner,
  IonTitle, IonToolbar, ViewWillEnter,
} from '@ionic/angular';
import { addIcons } from 'ionicons';
import { addOutline } from 'ionicons/icons';
import {
  ApiError, AuthService, Permissions, PurchaseOrderListItemDto, PurchaseOrdersApi, ReceiptListItemDto, ReceiptsApi, WarehouseContextService,
} from '@wh/shared';
import { NavController } from '@ionic/angular';
import { Feedback } from '../../core/feedback';

/** Draft goods receipts waiting to be counted, and open purchase orders a delivery can be received against. */
@Component({
  selector: 'wh-receipt-list',
  imports: [DatePipe, RouterLink, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonButton, IonIcon, IonContent, IonList, IonItem, IonLabel,
    IonNote, IonSpinner, IonRefresher, IonRefresherContent],
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
                @if (r.purchaseOrderNumber) { <p>Order {{ r.purchaseOrderNumber }}</p> }
                <p>{{ r.receiptDate | date: 'dd.MM.yyyy' }} · {{ r.lineCount }} line(s)</p>
              </ion-label>
            </ion-item>
          } @empty {
            <p class="wh-empty">No draft receipts.@if (canCreate) { Tap + to receive a delivery. }</p>
          }
        </ion-list>
        @if (canSeeOrders) {
          <div class="wh-section"><h2>Expected deliveries</h2></div>
          <ion-list lines="full">
            @for (o of orders(); track o.id) {
              <ion-item button [detail]="false" (click)="receive(o)" [disabled]="busy()" [attr.data-testid]="'po-' + o.number">
                <ion-label>
                  <h2>{{ o.supplierName }}</h2>
                  <p>{{ o.number }}@if (o.supplierReference) { · {{ o.supplierReference }} } · {{ o.lineCount }} line(s)</p>
                  <p>@if (o.expectedDate) { Expected {{ o.expectedDate | date: 'dd.MM.yyyy' }} } @else { No date }
                    @if (o.receivedPercent > 0) { · {{ o.receivedPercent }}% received }</p>
                </ion-label>
                @if (o.isOverdue) { <ion-note slot="end" color="danger">Overdue</ion-note> }
                @else if (canCreate) { <ion-note slot="end" color="primary">Receive</ion-note> }
              </ion-item>
            } @empty {
              <p class="wh-empty">No open purchase orders.</p>
            }
          </ion-list>
        }
        @if (canCreate) {
          <div class="wh-actions"><ion-button expand="block" [fill]="orders().length ? 'outline' : 'solid'" routerLink="/receipts/new" data-testid="receipt-new">
            Delivery without an order</ion-button></div>
        }
      }
    </ion-content>
  `,
})
export class ReceiptListPage implements ViewWillEnter {
  private readonly api = inject(ReceiptsApi);
  private readonly purchaseOrders = inject(PurchaseOrdersApi);
  private readonly ctx = inject(WarehouseContextService);
  private readonly feedback = inject(Feedback);
  private readonly nav = inject(NavController);
  private readonly auth = inject(AuthService);
  protected readonly canCreate = this.auth.hasPermission(Permissions.InventoryReceive);
  protected readonly canSeeOrders = this.auth.hasPermission(Permissions.PurchasingRead) || this.auth.hasPermission(Permissions.PurchasingManage);
  protected readonly drafts = signal<ReceiptListItemDto[]>([]);
  protected readonly orders = signal<PurchaseOrderListItemDto[]>([]);
  protected readonly busy = signal(false);
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
      const [drafts, orders] = await Promise.all([
        this.api.list({ warehouseId: wh.id, status: 'draft', pageSize: 100, sortBy: 'receiptDate', sortDir: 'desc' }),
        this.canSeeOrders ? this.purchaseOrders.list({ warehouseId: wh.id, openOnly: true, pageSize: 50, sortBy: 'expectedDate' }) : Promise.resolve(null),
      ]);
      this.drafts.set(drafts.items);
      this.orders.set(orders?.items ?? []);
      this.error.set(null);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
      await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
    }
  }

  /** Opens the draft receipt already started for the order, or starts one pre-filled with what is still expected. */
  protected async receive(o: PurchaseOrderListItemDto): Promise<void> {
    if (!this.canCreate || this.busy()) return;
    const draft = this.drafts().find((d) => d.purchaseOrderId === o.id);
    if (draft) {
      await this.nav.navigateForward(['/receipts', draft.id]);
      return;
    }
    const ok = await this.feedback.confirm('Receive delivery', `Start counting ${o.number} from ${o.supplierName}? The lines are pre-filled with what is still expected.`, 'Start');
    if (!ok) return;
    this.busy.set(true);
    try {
      const receipt = await this.purchaseOrders.createReceipt(o.id);
      await this.nav.navigateForward(['/receipts', receipt.id]);
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
