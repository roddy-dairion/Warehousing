import { Component, input } from '@angular/core';
import { formatDate } from '@angular/common';
import { IonItem, IonLabel, IonList, IonNote } from '@ionic/angular';
import { BalanceDto, qty } from '@wh/shared';

/** Stock rows by bin and lot: on hand, and how much is still free (not reserved for dispatches). */
@Component({
  selector: 'wh-balance-list',
  imports: [IonList, IonItem, IonLabel, IonNote],
  template: `
    <ion-list lines="full">
      @for (b of balances(); track b.id) {
        <ion-item>
          <ion-label>
            <h3>{{ showProduct() ? b.sku + ' · ' + b.productName : b.locationCode }}</h3>
            <p>
              {{ details(b) }}
              @if (b.lotStatus && b.lotStatus !== 'released') { <strong class="hold">{{ b.lotStatus }}</strong> }
            </p>
          </ion-label>
          <ion-note slot="end" class="wh-num">
            <strong>{{ qty(b.onHand) }}</strong> {{ b.baseUnitCode }}
            @if (b.reserved) { <br /><small>{{ qty(b.available) }} free</small> }
          </ion-note>
        </ion-item>
      } @empty {
        <p class="wh-empty">No stock here.</p>
      }
    </ion-list>
  `,
  styles: `
    ion-note { text-align: right; font-size: 1rem; color: var(--ion-text-color); }
    small { color: var(--ion-color-medium-shade); }
    .hold { color: var(--ion-color-danger); text-transform: capitalize; }
  `,
})
export class BalanceList {
  readonly balances = input.required<BalanceDto[]>();
  /** List by product (a bin's content) instead of by bin (a product's stock). */
  readonly showProduct = input(false);
  protected readonly qty = qty;

  /** "A-01 · lot L2401 · exp. 31.12.2027 · not pickable" (bin only when listing a bin's products). */
  protected details(b: BalanceDto): string {
    return [
      this.showProduct() ? b.locationCode : null,
      b.lotNo ? `lot ${b.lotNo}` : null,
      b.expiryDate ? `exp. ${formatDate(b.expiryDate, 'dd.MM.yyyy', 'en-US')}` : null,
      b.isPickable ? null : 'not pickable',
    ].filter((x) => x).join(' · ');
  }
}
