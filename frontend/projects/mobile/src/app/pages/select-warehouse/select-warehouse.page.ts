import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonNote, IonTitle, IonToolbar } from '@ionic/angular';
import { AuthService, WarehouseContextService } from '@wh/shared';

@Component({
  selector: 'wh-select-warehouse',
  imports: [IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonContent, IonList, IonItem, IonLabel, IonNote],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-title>Choose warehouse</ion-title>
        <ion-buttons slot="end"><ion-button (click)="signOut()">Sign out</ion-button></ion-buttons>
      </ion-toolbar>
    </ion-header>
    <ion-content>
      @if (ctx.available().length === 0) {
        <p class="ion-padding">No warehouse is assigned to your account. Ask a manager to grant access, then sign in again.</p>
      } @else {
        <ion-list>
          @for (wh of ctx.available(); track wh.id) {
            <ion-item button detail (click)="choose(wh.id)" [class.current]="wh.id === ctx.current()?.id">
              <ion-label>
                <h2>{{ wh.name }}</h2>
                <ion-note>{{ wh.code }}</ion-note>
              </ion-label>
            </ion-item>
          }
        </ion-list>
      }
    </ion-content>
  `,
})
export class SelectWarehousePage {
  protected readonly ctx = inject(WarehouseContextService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected async choose(id: string): Promise<void> {
    if (this.ctx.select(id)) {
      await this.router.navigateByUrl('/', { replaceUrl: true });
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.ctx.clear();
    await this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
