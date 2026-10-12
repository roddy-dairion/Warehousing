import { Injectable, NgZone, inject, signal } from '@angular/core';
import { Capacitor, PluginListenerHandle } from '@capacitor/core';
import { BarcodeScanner } from '@capacitor-mlkit/barcode-scanning';
import { AlertController } from '@ionic/angular';

/** Body class while the camera is open: the web view is hidden so the camera preview (rendered behind it) shows. */
export const SCANNING_CLASS = 'wh-scanning';

/**
 * Camera barcode scanning with ML Kit (`@capacitor-mlkit/barcode-scanning`, custom-overlay mode so the torch can be
 * switched and the screen keeps our branding). One scan per call: resolves with the code, or null when cancelled.
 *
 * Scanning only identifies a product, bin or lot; the API validates every stock action.
 * In a desktop browser there is no camera support: pages fall back to typed or scanner-gun (keyboard wedge) input.
 */
@Injectable({ providedIn: 'root' })
export class ScannerService {
  private readonly zone = inject(NgZone);
  private readonly alerts = inject(AlertController);

  /** True on Android/iOS, where the camera scanner is available. */
  readonly cameraAvailable = Capacitor.isNativePlatform();
  /** What the overlay asks for while the camera is open, e.g. "Scan the bin label". Null when closed. */
  readonly prompt = signal<string | null>(null);
  readonly torchAvailable = signal(false);
  readonly torchOn = signal(false);

  private resolver: ((code: string | null) => void) | null = null;
  private listeners: PluginListenerHandle[] = [];

  /** Opens the camera and returns the first code read, or null if the user cancels (or the camera is unavailable). */
  async scan(prompt: string): Promise<string | null> {
    if (!this.cameraAvailable || this.resolver) return null;
    if (!(await this.ensurePermission())) return null;

    const result = new Promise<string | null>((resolve) => (this.resolver = resolve));
    this.prompt.set(prompt);
    document.body.classList.add(SCANNING_CLASS);
    try {
      this.listeners.push(
        await BarcodeScanner.addListener('barcodesScanned', (event) => {
          const code = event.barcodes.map((b) => (b.rawValue ?? b.displayValue ?? '').trim()).find((v) => v.length > 0);
          if (code) this.zone.run(() => void this.finish(code));
        }),
        await BarcodeScanner.addListener('scanError', (event) => {
          this.zone.run(() => {
            void this.finish(null);
            void this.showError(event.message);
          });
        }),
      );
      await BarcodeScanner.startScan();
      this.torchAvailable.set((await BarcodeScanner.isTorchAvailable().catch(() => ({ available: false }))).available);
    } catch (e) {
      await this.finish(null);
      await this.showError(e instanceof Error ? e.message : String(e));
    }
    return result;
  }

  /** Closes the camera without a result. */
  cancel(): Promise<void> {
    return this.finish(null);
  }

  async toggleTorch(): Promise<void> {
    try {
      if (this.torchOn()) await BarcodeScanner.disableTorch();
      else await BarcodeScanner.enableTorch();
      this.torchOn.update((on) => !on);
    } catch {
      this.torchAvailable.set(false);
    }
  }

  private async finish(code: string | null): Promise<void> {
    const resolve = this.resolver;
    if (!resolve) return;
    this.resolver = null;
    const listeners = this.listeners;
    this.listeners = [];
    await Promise.all(listeners.map((l) => l.remove().catch(() => undefined)));
    if (this.torchOn()) await BarcodeScanner.disableTorch().catch(() => undefined);
    await BarcodeScanner.stopScan().catch(() => undefined);
    document.body.classList.remove(SCANNING_CLASS);
    this.prompt.set(null);
    this.torchOn.set(false);
    resolve(code);
  }

  private async ensurePermission(): Promise<boolean> {
    let status = await BarcodeScanner.checkPermissions();
    if (status.camera === 'granted' || status.camera === 'limited') return true;
    if (status.camera !== 'denied') status = await BarcodeScanner.requestPermissions();
    if (status.camera === 'granted' || status.camera === 'limited') return true;

    const alert = await this.alerts.create({
      header: 'Camera access needed',
      message: 'Allow camera access in the phone settings to scan barcodes. You can also type the code or use a scanner gun.',
      buttons: [
        { text: 'Not now', role: 'cancel' },
        { text: 'Open settings', handler: () => void BarcodeScanner.openSettings() },
      ],
    });
    await alert.present();
    return false;
  }

  private async showError(message: string): Promise<void> {
    const alert = await this.alerts.create({
      header: 'Scanner problem',
      message: `${message || 'The camera could not be started.'} Type the code instead.`,
      buttons: ['OK'],
    });
    await alert.present();
  }
}
