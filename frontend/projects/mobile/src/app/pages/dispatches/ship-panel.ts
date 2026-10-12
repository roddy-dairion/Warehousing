import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonButton, IonInput, IonItem, IonLabel, IonList, IonNote, IonRadio, IonRadioGroup } from '@ionic/angular';
import { ApiError, DispatchDto, DispatchesApi, newIdempotencyKey, qty, ShortShipDecision } from '@wh/shared';
import { Feedback } from '../../core/feedback';

/** Shipment confirmation: deducts the picked quantities from stock (once: the request carries an idempotency key). */
@Component({
  selector: 'wh-ship-panel',
  imports: [FormsModule, IonButton, IonInput, IonItem, IonLabel, IonList, IonNote, IonRadio, IonRadioGroup],
  template: `
    <div class="wh-section"><h2>What goes on the truck</h2></div>
    <ion-list lines="full">
      @for (l of dispatch().lines; track l.id) {
        <ion-item>
          <ion-label>
            <h3>{{ l.sku }}</h3>
            <p>{{ l.productName }}</p>
          </ion-label>
          <ion-note slot="end" class="wh-num" [class.short]="l.quantityPicked < l.quantity">
            {{ qty(l.quantityPicked) }} / {{ qty(l.quantity) }} {{ l.baseUnitCode }}
          </ion-note>
        </ion-item>
      }
    </ion-list>
    <div class="wh-section fields">
      <ion-input label="Carrier" labelPlacement="stacked" fill="outline" [(ngModel)]="carrier" placeholder="Own van, courier…" data-testid="ship-carrier" />
      <ion-input label="Tracking number" labelPlacement="stacked" fill="outline" [(ngModel)]="tracking" data-testid="ship-tracking" />
    </div>
    @if (short()) {
      <div class="wh-section"><h2>Not everything was picked. The rest…</h2></div>
      <ion-radio-group [(ngModel)]="decision">
        <ion-item><ion-radio value="backorder" labelPlacement="end" data-testid="ship-backorder">Backorder: ship later on a new dispatch</ion-radio></ion-item>
        <ion-item><ion-radio value="closeShort" labelPlacement="end">Close short: the customer will not get it</ion-radio></ion-item>
      </ion-radio-group>
    }
    <div class="wh-actions">
      <ion-button expand="block" size="large" (click)="ship()" [disabled]="busy() || (short() && !decision)" data-testid="ship-confirm">Confirm shipment</ion-button>
    </div>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 10px; margin-top: 12px; }
    ion-note { font-size: 1rem; color: var(--ion-text-color); }
    ion-note.short { color: var(--ion-color-warning-shade); font-weight: 600; }
  `,
})
export class ShipPanel {
  private readonly api = inject(DispatchesApi);
  private readonly feedback = inject(Feedback);
  protected readonly qty = qty;

  readonly dispatch = input.required<DispatchDto>();
  readonly changed = output<void>();

  protected readonly busy = signal(false);
  protected carrier = '';
  protected tracking = '';
  protected decision: ShortShipDecision | null = null;
  protected readonly short = computed(() => this.dispatch().lines.some((l) => l.quantityPicked < l.quantity));
  /** Kept until the shipment is confirmed, so a retry after a lost connection cannot deduct the stock twice. */
  private key = newIdempotencyKey();

  protected async ship(): Promise<void> {
    const d = this.dispatch();
    const ok = await this.feedback.confirm('Confirm shipment', `Ship ${d.number} to ${d.customerName}? The picked quantities leave the stock now.`, 'Ship');
    if (!ok) return;
    this.busy.set(true);
    try {
      await this.api.ship(d.id, { carrier: this.carrier.trim() || null, trackingNumber: this.tracking.trim() || null, shortDecision: this.short() ? this.decision : null }, { key: this.key });
      this.key = newIdempotencyKey();
      await this.feedback.success('Shipped. Stock deducted, packing slip created.');
      this.changed.emit();
    } catch (e) {
      await this.feedback.error(e, { retrySafe: ApiError.from(e).kind === 'network' });
    } finally {
      this.busy.set(false);
    }
  }
}
