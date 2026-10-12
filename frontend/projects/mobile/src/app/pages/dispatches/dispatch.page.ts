import { Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  IonBackButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonNote, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar,
  ViewWillEnter,
} from '@ionic/angular';
import { ApiError, DispatchDto, DispatchesApi, qty } from '@wh/shared';
import { StatusBadge } from '../../ui/status-badge';
import { DeliverPanel } from './deliver-panel';
import { PickPanel } from './pick-panel';
import { ShipPanel } from './ship-panel';

type Mode = 'pick' | 'ship' | 'deliver' | 'view';

/** One dispatch. Shows the step this user can do now: picking, shipment confirmation or proof of delivery. */
@Component({
  selector: 'wh-dispatch',
  imports: [DatePipe, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonContent, IonList, IonItem, IonLabel, IonNote, IonSpinner, IonRefresher,
    IonRefresherContent, StatusBadge, PickPanel, ShipPanel, DeliverPanel],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-buttons slot="start"><ion-back-button defaultHref="/dispatches" /></ion-buttons>
        <ion-title>{{ dispatch()?.number ?? 'Dispatch' }}</ion-title>
      </ion-toolbar>
    </ion-header>
    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="load($event)"><ion-refresher-content /></ion-refresher>
      @if (error(); as e) {
        <p class="wh-empty">{{ e }}</p>
      } @else if (dispatch(); as d) {
        <div class="head wh-section">
          <div>
            <strong>{{ d.customerName }}</strong>
            <div class="wh-muted">for {{ d.requestedDate | date: 'dd.MM.yyyy' }}@if (d.customerOrderReference) { · ref. {{ d.customerOrderReference }} }</div>
            @if (d.shipTo; as a) { <div class="wh-muted">{{ a.line1 }}, {{ a.city }}</div> }
          </div>
          <wh-status-badge [status]="d.status" />
        </div>
        @if (d.notes) { <p class="wh-section note">{{ d.notes }}</p> }

        @switch (mode()) {
          @case ('pick') { <wh-pick-panel [dispatch]="d" (changed)="load()" /> }
          @case ('ship') { <wh-ship-panel [dispatch]="d" (changed)="load()" /> }
          @case ('deliver') { <wh-deliver-panel [dispatch]="d" (changed)="load()" /> }
          @default {
            <p class="wh-section wh-muted">{{ viewHint(d) }}</p>
            <ion-list lines="full">
              @for (l of d.lines; track l.id) {
                <ion-item>
                  <ion-label><h3>{{ l.sku }}</h3><p>{{ l.productName }}</p></ion-label>
                  <ion-note slot="end" class="wh-num qty-note">{{ qty(l.quantityShipped || l.quantityPicked || l.quantity) }} {{ l.baseUnitCode }}</ion-note>
                </ion-item>
              }
            </ion-list>
            @if (d.proofOfDelivery; as pod) {
              <p class="wh-section">Received by <strong>{{ pod.receiverName }}</strong> on {{ pod.deliveredAt | date: 'dd.MM.yyyy HH:mm' }}.</p>
            }
          }
        }
      } @else {
        <div class="wh-empty"><ion-spinner /></div>
      }
    </ion-content>
  `,
  styles: `
    .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; margin-top: 14px; }
    .note { background: #fff8e1; padding: 8px 12px; border-radius: 6px; margin: 10px 16px; }
  `,
})
export class DispatchPage implements ViewWillEnter {
  private readonly api = inject(DispatchesApi);
  protected readonly qty = qty;

  readonly id = input.required<string>();
  protected readonly dispatch = signal<DispatchDto | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly mode = computed<Mode>(() => {
    const a = this.dispatch()?.actions;
    if (!a) return 'view';
    if (a.canPick || a.canCompletePicking) return 'pick';
    if (a.canShip) return 'ship';
    if (a.canDeliver) return 'deliver';
    return 'view';
  });

  ionViewWillEnter(): void {
    void this.load();
  }

  protected async load(event?: CustomEvent): Promise<void> {
    try {
      this.dispatch.set(await this.api.getOne(this.id()));
      this.error.set(null);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
    }
  }

  protected viewHint(d: DispatchDto): string {
    switch (d.status) {
      case 'draft':
      case 'submitted':
        return 'Not approved yet: nothing to pick.';
      case 'readyForDispatch':
        return 'Picked and ready. Someone with shipping rights confirms the shipment.';
      case 'dispatched':
        return 'On its way. Someone with delivery rights records the proof of delivery.';
      case 'delivered':
        return 'Delivered.';
      case 'cancelled':
        return 'Cancelled.';
      default:
        return 'You cannot pick this dispatch.';
    }
  }
}
