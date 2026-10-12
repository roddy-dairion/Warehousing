import { Component, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonButton, IonIcon, IonInput } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { addOutline, removeOutline } from 'ionicons/icons';

/** Quantity with large − / + buttons (gloves, one hand). Decimals are allowed for products sold by weight. */
@Component({
  selector: 'wh-qty-stepper',
  imports: [FormsModule, IonButton, IonIcon, IonInput],
  template: `
    <div class="stepper">
      <ion-button fill="outline" size="large" (click)="step(-1)" [disabled]="disabled() || (value() ?? 0) <= min()" aria-label="Less">
        <ion-icon slot="icon-only" name="remove-outline" />
      </ion-button>
      <ion-input type="number" inputmode="decimal" fill="outline" [label]="label()" labelPlacement="stacked" [ngModel]="value()"
        (ngModelChange)="set($event)" [min]="min()" [max]="max()" [disabled]="disabled()" [attr.data-testid]="testId()" />
      <ion-button fill="outline" size="large" (click)="step(1)" [disabled]="disabled() || (max() !== null && (value() ?? 0) >= max()!)" aria-label="More">
        <ion-icon slot="icon-only" name="add-outline" />
      </ion-button>
    </div>
  `,
  styles: `
    .stepper { display: flex; gap: 0.5rem; align-items: flex-end; }
    ion-input { flex: 1; text-align: center; font-size: 1.25rem; }
    ion-button { margin: 0; height: 56px; }
  `,
})
export class QtyStepper {
  readonly value = model<number | null>(1);
  readonly label = input('Quantity');
  readonly min = input(0);
  readonly max = input<number | null>(null);
  readonly disabled = input(false);
  readonly testId = input<string | null>(null);

  constructor() {
    addIcons({ addOutline, removeOutline });
  }

  protected set(v: string | number | null): void {
    const n = v === null || v === '' ? null : Number(v);
    this.value.set(n === null || Number.isNaN(n) ? null : n);
  }

  protected step(delta: number): void {
    let next = Math.max(this.min(), Math.round(((this.value() ?? 0) + delta) * 1000) / 1000);
    const max = this.max();
    if (max !== null) next = Math.min(max, next);
    this.value.set(next);
  }
}
