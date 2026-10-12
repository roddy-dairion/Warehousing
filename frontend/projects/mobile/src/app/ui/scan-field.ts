import { Component, inject, input, model, output, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonButton, IonIcon, IonInput } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { scanOutline } from 'ionicons/icons';
import { ScannerService } from '../core/scanner.service';

/**
 * One field for a barcode: type it, use a scanner gun (keyboard wedge: types the code and presses Enter),
 * or tap the camera button. Emits the code once; the same code arriving twice within a second is ignored
 * (scanner guns and double taps can repeat it).
 */
@Component({
  selector: 'wh-scan-field',
  imports: [FormsModule, IonInput, IonButton, IonIcon],
  template: `
    <div class="scan-field">
      <ion-input #field [label]="label()" labelPlacement="stacked" fill="outline" [placeholder]="placeholder()" [(ngModel)]="value"
        [disabled]="disabled()" (keyup.enter)="submit()" enterkeyhint="go" autocomplete="off" autocapitalize="off" spellcheck="false"
        [clearInput]="true" [attr.data-testid]="testId()" />
      @if (scanner.cameraAvailable) {
        <ion-button class="camera" size="large" (click)="camera()" [disabled]="disabled()" [attr.aria-label]="'Scan ' + label() + ' with the camera'">
          <ion-icon slot="icon-only" name="scan-outline" />
        </ion-button>
      } @else {
        <ion-button class="go" size="large" fill="outline" (click)="submit()" [disabled]="disabled() || !value()">OK</ion-button>
      }
    </div>
  `,
  styles: `
    .scan-field { display: flex; gap: 0.5rem; align-items: flex-end; }
    ion-input { flex: 1; --highlight-color-focused: var(--ion-color-primary); }
    ion-button { margin: 0; height: 56px; }
  `,
})
export class ScanField {
  protected readonly scanner = inject(ScannerService);
  private readonly field = viewChild.required<IonInput>('field');

  readonly label = input('Barcode');
  readonly placeholder = input('Scan or type');
  /** Shown above the camera preview. */
  readonly prompt = input('Scan the barcode');
  readonly disabled = input(false);
  readonly testId = input<string | null>(null);
  /** Keep the code in the field after submitting (e.g. a bin that stays the same for several picks). */
  readonly keep = input(false);
  readonly value = model('');
  readonly scanned = output<string>();

  private last: { code: string; at: number } | null = null;

  constructor() {
    addIcons({ scanOutline });
  }

  /** Puts the cursor in the field (scanner guns type into the focused field). */
  focus(): void {
    void this.field().setFocus();
  }

  protected submit(): void {
    const code = (this.value() ?? '').trim();
    if (!code || this.disabled()) return;
    const now = Date.now();
    if (this.last && this.last.code === code && now - this.last.at < 1000) return;
    this.last = { code, at: now };
    if (!this.keep()) this.value.set('');
    this.scanned.emit(code);
  }

  protected async camera(): Promise<void> {
    const code = await this.scanner.scan(this.prompt());
    if (code) {
      this.value.set(code);
      this.last = null;
      this.submit();
    }
  }
}
