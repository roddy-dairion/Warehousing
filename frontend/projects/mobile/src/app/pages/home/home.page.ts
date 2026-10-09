import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  IonButton,
  IonButtons,
  IonContent,
  IonHeader,
  IonIcon,
  IonItem,
  IonLabel,
  IonList,
  IonNote,
  IonTitle,
  IonToolbar,
} from '@ionic/angular';
import { addIcons } from 'ionicons';
import { barcodeOutline, cubeOutline, downloadOutline, listOutline, logOutOutline, swapHorizontalOutline } from 'ionicons/icons';
import { AuthService, Permissions, WarehouseContextService } from '@wh/shared';

interface Action {
  label: string;
  hint: string;
  icon: string;
  visible: boolean;
}

@Component({
  selector: 'wh-home',
  imports: [IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonContent, IonList, IonItem, IonIcon, IonLabel, IonNote, RouterLink],
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss',
})
export class HomePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly ctx = inject(WarehouseContextService);
  protected readonly user = this.auth.user;

  protected readonly actions = computed<Action[]>(() => {
    this.auth.user();
    const can = (p: string) => this.auth.hasPermission(p);
    return [
      { label: 'Scan a product', hint: 'Identify a product and see its stock', icon: 'barcode-outline', visible: can(Permissions.ProductsRead) },
      { label: 'My picking tasks', hint: 'Pick approved dispatches', icon: 'list-outline', visible: can(Permissions.DispatchesPick) },
      { label: 'Receive goods', hint: 'Record quantities on a goods receipt', icon: 'download-outline', visible: can(Permissions.InventoryReceive) },
      { label: 'Stock lookup', hint: 'On hand, reserved and available by bin', icon: 'cube-outline', visible: can(Permissions.InventoryRead) },
    ].filter((a) => a.visible);
  });

  constructor() {
    addIcons({ barcodeOutline, listOutline, downloadOutline, cubeOutline, logOutOutline, swapHorizontalOutline });
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.ctx.clear();
    await this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
