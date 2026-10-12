import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  IonBadge, IonButton, IonButtons, IonContent, IonHeader, IonIcon, IonItem, IonLabel, IonList, IonRefresher, IonRefresherContent, IonTitle, IonToolbar,
  ViewWillEnter,
} from '@ionic/angular';
import { addIcons } from 'ionicons';
import { barcodeOutline, cubeOutline, downloadOutline, listOutline, logOutOutline, swapHorizontalOutline, timeOutline } from 'ionicons/icons';
import { AuthService, Permissions, ReportingApi, WarehouseContextService, WarehouseDashboardDto } from '@wh/shared';

interface Action {
  label: string;
  hint: string;
  icon: string;
  link: string;
  badge?: number | null;
  visible: boolean;
}

@Component({
  selector: 'wh-home',
  imports: [IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonContent, IonList, IonItem, IonIcon, IonLabel, IonBadge, IonRefresher, IonRefresherContent, RouterLink],
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss',
})
export class HomePage implements ViewWillEnter {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly reporting = inject(ReportingApi);
  protected readonly ctx = inject(WarehouseContextService);
  protected readonly user = this.auth.user;
  protected readonly dashboard = signal<WarehouseDashboardDto | null>(null);

  protected readonly actions = computed<Action[]>(() => {
    this.auth.user();
    const can = (...p: string[]) => this.auth.hasAnyPermission(...p);
    const d = this.dashboard()?.dispatches;
    const toDo = d ? d.toPick + d.picking + d.readyToShip + d.awaitingDelivery : null;
    return [
      { label: 'Scan a product or bin', hint: 'Identify it and see the stock', icon: 'barcode-outline', link: '/scan', visible: can(Permissions.ProductsRead) },
      {
        label: 'Dispatches', hint: 'Pick, ship and record deliveries', icon: 'list-outline', link: '/dispatches', badge: toDo,
        visible: can(Permissions.DispatchesRead) && can(Permissions.DispatchesPick, Permissions.DispatchesShip, Permissions.DispatchesDeliver),
      },
      {
        label: 'Receive goods', hint: 'Count deliveries into stock', icon: 'download-outline', link: '/receipts', badge: this.dashboard()?.pendingReceipts,
        visible: can(Permissions.InventoryReceive, Permissions.InventoryReceivePost),
      },
      { label: 'Recent activity', hint: 'Latest stock movements', icon: 'time-outline', link: '/activity', visible: can(Permissions.InventoryRead) },
    ].filter((a) => a.visible);
  });

  constructor() {
    addIcons({ barcodeOutline, listOutline, downloadOutline, cubeOutline, logOutOutline, swapHorizontalOutline, timeOutline });
  }

  ionViewWillEnter(): void {
    void this.refresh();
  }

  protected async refresh(event?: CustomEvent): Promise<void> {
    const wh = this.ctx.current();
    try {
      if (wh) this.dashboard.set(await this.reporting.dashboard(wh.id));
    } catch {
      // Counts are a convenience; the actions still work without them.
    } finally {
      await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.ctx.clear();
    await this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
