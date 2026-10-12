import { Component, DestroyRef, inject } from '@angular/core';
import { IonIcon, Platform } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { closeOutline, flashlightOutline, flashOffOutline } from 'ionicons/icons';
import { ScannerService } from './scanner.service';

/**
 * Shown above the camera preview while scanning. It sits outside <ion-app> so it stays visible while the rest of the
 * web view is hidden (see styles.scss, `.wh-scanning`).
 */
@Component({
  selector: 'wh-scanner-overlay',
  imports: [IonIcon],
  template: `
    @if (scanner.prompt(); as prompt) {
      <div class="overlay" role="dialog" aria-modal="true" [attr.aria-label]="prompt">
        <p class="prompt">{{ prompt }}</p>
        <div class="frame" aria-hidden="true"></div>
        <div class="actions">
          @if (scanner.torchAvailable()) {
            <button type="button" (click)="scanner.toggleTorch()" [attr.aria-pressed]="scanner.torchOn()">
              <ion-icon [name]="scanner.torchOn() ? 'flash-off-outline' : 'flashlight-outline'" aria-hidden="true" />
              {{ scanner.torchOn() ? 'Light off' : 'Light' }}
            </button>
          }
          <button type="button" (click)="scanner.cancel()">
            <ion-icon name="close-outline" aria-hidden="true" /> Cancel
          </button>
        </div>
      </div>
    }
  `,
  styles: `
    .overlay {
      position: fixed; inset: 0; z-index: 99999; display: flex; flex-direction: column; align-items: center; justify-content: space-between;
      padding: calc(env(safe-area-inset-top) + 1.5rem) 1rem calc(env(safe-area-inset-bottom) + 1.5rem); color: #fff;
      font-family: var(--ion-font-family);
    }
    .prompt { margin: 0; padding: 0.6rem 1rem; border-radius: 6px; background: rgba(30, 42, 51, 0.85); font-size: 1.05rem; text-align: center; }
    .frame {
      width: min(78vw, 22rem); aspect-ratio: 3 / 2; border: 3px solid var(--wh-hazard, #f2b705); border-radius: 12px;
      box-shadow: 0 0 0 100vmax rgba(0, 0, 0, 0.45);
    }
    .actions { display: flex; gap: 0.75rem; }
    button {
      display: inline-flex; align-items: center; gap: 0.4rem; min-height: 48px; padding: 0 1.25rem; border: 0; border-radius: 24px;
      background: rgba(30, 42, 51, 0.9); color: #fff; font: inherit; font-size: 1rem;
    }
    ion-icon { font-size: 1.3rem; }
  `,
})
export class ScannerOverlay {
  protected readonly scanner = inject(ScannerService);

  constructor() {
    addIcons({ closeOutline, flashlightOutline, flashOffOutline });
    // Android back button closes the camera instead of navigating away.
    const sub = inject(Platform).backButton.subscribeWithPriority(1000, (next) => {
      if (this.scanner.prompt()) void this.scanner.cancel();
      else next();
    });
    inject(DestroyRef).onDestroy(() => sub.unsubscribe());
  }
}
