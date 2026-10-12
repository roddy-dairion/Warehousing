import { Component, inject, signal } from '@angular/core';
import { NavController } from '@ionic/angular';
import { IonBackButton, IonButtons, IonContent, IonHeader, IonTitle, IonToolbar } from '@ionic/angular';
import { ApiError, ProductsApi } from '@wh/shared';
import { Feedback } from '../../core/feedback';
import { ScanField } from '../../ui/scan-field';
import { UnknownCode } from './unknown-code';

/** "Add a product" for managers (`products.manage`): optional barcode (scanned or typed), then SKU, name, unit, lot/expiry. */
@Component({
  selector: 'wh-new-product',
  imports: [IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonContent, ScanField, UnknownCode],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-buttons slot="start"><ion-back-button defaultHref="/" /></ion-buttons>
        <ion-title>Add a product</ion-title>
      </ion-toolbar>
    </ion-header>
    <ion-content class="ion-padding-vertical">
      <div class="wh-section">
        <wh-scan-field label="Barcode (optional)" placeholder="Scan the product barcode" prompt="Scan the product barcode" [keep]="true"
          [value]="barcode()" testId="new-barcode" (scanned)="onBarcode($event)" />
      </div>
      <wh-unknown-code [code]="barcode()" [createOnly]="true" (done)="created($event.productId)" />
    </ion-content>
  `,
})
export class NewProductPage {
  private readonly products = inject(ProductsApi);
  private readonly feedback = inject(Feedback);
  private readonly nav = inject(NavController);
  protected readonly barcode = signal('');

  protected async onBarcode(code: string): Promise<void> {
    try {
      const existing = await this.products.lookupBarcode(code);
      await this.feedback.warn(`Barcode ${code} already belongs to ${existing.sku} ${existing.name}.`);
      this.barcode.set('');
    } catch (e) {
      if (ApiError.from(e).kind === 'not-found') this.barcode.set(code);
      else await this.feedback.error(e);
    }
  }

  protected created(productId: string): void {
    void this.nav.navigateForward(['/products', productId], { replaceUrl: true });
  }
}
