import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  IonBackButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonNote, IonRefresher, IonRefresherContent, IonSegment, IonSegmentButton,
  IonSpinner, IonTitle, IonToggle, IonToolbar, ViewWillEnter,
} from '@ionic/angular';
import { ApiError, AuthService, DispatchListItemDto, DispatchesApi, DispatchStatus, Permissions, qty, WarehouseContextService } from '@wh/shared';
import { StatusBadge } from '../../ui/status-badge';

type Stage = 'pick' | 'ship' | 'deliver';

const STAGES: { value: Stage; label: string; statuses: DispatchStatus[]; permission: string }[] = [
  { value: 'pick', label: 'To pick', statuses: ['approved', 'picking'], permission: Permissions.DispatchesPick },
  { value: 'ship', label: 'To ship', statuses: ['readyForDispatch'], permission: Permissions.DispatchesShip },
  { value: 'deliver', label: 'To deliver', statuses: ['dispatched'], permission: Permissions.DispatchesDeliver },
];

/** Dispatches waiting for this user's part of the work, oldest requested date first. */
@Component({
  selector: 'wh-dispatch-list',
  imports: [DatePipe, FormsModule, RouterLink, IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonContent, IonSegment, IonSegmentButton, IonLabel,
    IonList, IonItem, IonToggle, IonSpinner, IonRefresher, IonRefresherContent, StatusBadge],
  template: `
    <ion-header>
      <ion-toolbar>
        <ion-buttons slot="start"><ion-back-button defaultHref="/" /></ion-buttons>
        <ion-title>Dispatches</ion-title>
      </ion-toolbar>
      @if (stages().length > 1) {
        <ion-toolbar>
          <ion-segment [value]="stage()" (ionChange)="setStage($any($event.detail.value))">
            @for (s of stages(); track s.value) { <ion-segment-button [value]="s.value"><ion-label>{{ s.label }}</ion-label></ion-segment-button> }
          </ion-segment>
        </ion-toolbar>
      }
    </ion-header>
    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="load($event)"><ion-refresher-content /></ion-refresher>
      @if (stage() === 'pick') {
        <ion-item lines="full">
          <ion-toggle [ngModel]="mineOnly()" (ngModelChange)="mineOnly.set($event); load()">Only assigned to me</ion-toggle>
        </ion-item>
      }
      @if (error(); as e) {
        <p class="wh-empty">{{ e }}</p>
      } @else if (loading() && items().length === 0) {
        <div class="wh-empty"><ion-spinner /></div>
      } @else {
        <ion-list lines="full">
          @for (d of items(); track d.id) {
            <ion-item button detail [routerLink]="['/dispatches', d.id]">
              <ion-label>
                <h2>{{ d.number }}</h2>
                <p>{{ d.customerName }}@if (d.customerOrderReference) { · {{ d.customerOrderReference }} }</p>
                <p>{{ d.lineCount }} line(s) · for {{ d.requestedDate | date: 'dd.MM.yyyy' }}@if (d.assignedToName) { · {{ d.assignedToName }} }
                  @if (d.backorderOfNumber) { · backorder }</p>
              </ion-label>
              <wh-status-badge slot="end" [status]="d.status" />
            </ion-item>
          } @empty {
            <p class="wh-empty">Nothing waiting here.</p>
          }
        </ion-list>
      }
    </ion-content>
  `,
})
export class DispatchListPage implements ViewWillEnter {
  private readonly api = inject(DispatchesApi);
  private readonly auth = inject(AuthService);
  private readonly ctx = inject(WarehouseContextService);
  protected readonly qty = qty;

  protected readonly stages = computed(() => STAGES.filter((s) => this.auth.hasPermission(s.permission)));
  protected readonly stage = signal<Stage>(this.stages()[0]?.value ?? 'pick');
  protected readonly mineOnly = signal(false);
  protected readonly items = signal<DispatchListItemDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  ionViewWillEnter(): void {
    void this.load();
  }

  protected setStage(stage: Stage): void {
    this.stage.set(stage);
    this.items.set([]);
    void this.load();
  }

  protected async load(event?: CustomEvent): Promise<void> {
    const wh = this.ctx.current();
    const stage = STAGES.find((s) => s.value === this.stage());
    if (!wh || !stage) return;
    this.loading.set(true);
    try {
      const pages = await Promise.all(stage.statuses.map((status) =>
        this.api.list({ warehouseId: wh.id, status, assignedToMe: stage.value === 'pick' && this.mineOnly() ? true : undefined, pageSize: 100, sortBy: 'requestedDate' })));
      this.items.set(pages.flatMap((p) => p.items).sort((a, b) => a.requestedDate.localeCompare(b.requestedDate) || a.number.localeCompare(b.number)));
      this.error.set(null);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
      await (event?.target as HTMLIonRefresherElement | undefined)?.complete();
    }
  }
}
