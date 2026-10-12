import { Injectable, inject } from '@angular/core';
import { AlertController, ToastController } from '@ionic/angular';
import { ApiError } from '@wh/shared';

export interface ErrorOptions {
  /**
   * The failed call was a stock action sent with an idempotency key (or scan id) that is kept for the retry,
   * so tapping again cannot count it twice.
   */
  retrySafe?: boolean;
}

/**
 * Toasts, vibration and confirmation dialogs for the mobile app.
 * Errors show the server's message (ProblemDetails) so staff know exactly what to fix.
 */
@Injectable({ providedIn: 'root' })
export class Feedback {
  private readonly toasts = inject(ToastController);
  private readonly alerts = inject(AlertController);
  private current: HTMLIonToastElement | null = null;

  success(message: string): Promise<void> {
    vibrate(40);
    return this.toast(message, 'success', 2200);
  }

  warn(message: string): Promise<void> {
    vibrate([60, 40, 60]);
    return this.toast(message, 'warning', 4000);
  }

  error(error: unknown, options: ErrorOptions = {}): Promise<void> {
    vibrate([120, 60, 120]);
    return this.toast(errorMessage(error, options), 'danger', 6000);
  }

  /** Yes/no question. Resolves true when the user confirms. */
  async confirm(header: string, message: string, confirmText = 'OK', cancelText = 'Cancel'): Promise<boolean> {
    const alert = await this.alerts.create({
      header,
      message,
      buttons: [
        { text: cancelText, role: 'cancel' },
        { text: confirmText, role: 'confirm' },
      ],
    });
    await alert.present();
    const { role } = await alert.onDidDismiss();
    return role === 'confirm';
  }

  private async toast(message: string, color: string, duration: number): Promise<void> {
    await this.current?.dismiss().catch(() => undefined);
    const toast = await this.toasts.create({
      message,
      color,
      duration,
      position: 'top',
      buttons: [{ icon: 'close', role: 'cancel', side: 'end' }],
      swipeGesture: 'vertical',
    });
    this.current = toast;
    await toast.present();
  }
}

/** The text shown for a failed call. */
export function errorMessage(error: unknown, options: ErrorOptions = {}): string {
  const err = ApiError.from(error);
  if (err.kind === 'network') {
    return options.retrySafe
      ? 'No answer from the server. Check Wi-Fi or mobile data and tap again: it will not be counted twice.'
      : 'Cannot reach the server. Check Wi-Fi or mobile data and try again.';
  }
  return err.userMessage;
}

function vibrate(pattern: number | number[]): void {
  try {
    navigator.vibrate?.(pattern);
  } catch {
    /* not supported */
  }
}
