import { Component, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Drawer } from 'primeng/drawer';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Select } from 'primeng/select';
import { InputText } from 'primeng/inputtext';
import { Tag } from 'primeng/tag';
import { ApiError, AuthService, InventoryApi, LotDto, LotStatus, Permissions, ProductStockDto } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { qty } from '../../ui/format';

const LOT_STATUSES: { value: LotStatus; label: string }[] = [
  { value: 'released', label: 'Released (usable)' },
  { value: 'quarantine', label: 'Quarantine (on hold)' },
  { value: 'blocked', label: 'Blocked (do not use)' },
];

/** Side panel with one product's stock by bin and lot, and its lots (status can be changed by approvers). */
@Component({
  selector: 'wh-product-stock-drawer',
  imports: [DatePipe, FormsModule, RouterLink, Drawer, Button, Dialog, Select, InputText, Tag],
  template: `
    <p-drawer [visible]="!!productId()" (visibleChange)="!$event && closed.emit()" position="right" [style]="{ width: 'min(44rem, 100vw)' }" [header]="stock()?.sku ?? 'Stock'">
      @if (error(); as e) {
        <p class="field-error">{{ e }}</p>
      } @else if (stock(); as s) {
        <p class="title">{{ s.productName }} <a [routerLink]="['/products', s.productId]">Product details</a></p>
        <div class="figures">
          <div><span class="n">{{ qty(s.onHand) }}</span><span>on hand</span></div>
          <div><span class="n">{{ qty(s.reserved) }}</span><span>reserved</span></div>
          <div><span class="n">{{ qty(s.available) }}</span><span>available {{ s.baseUnitCode }}</span></div>
        </div>
        @for (w of s.warehouses; track w.warehouseId) {
          <h3>{{ w.warehouseName }}</h3>
          @if (w.balances.length === 0) {
            <p class="muted">No stock in this warehouse.</p>
          } @else {
            <table class="simple">
              <thead><tr><th>Bin</th><th>Lot</th><th>Expiry</th><th class="num">On hand</th><th class="num">Reserved</th><th class="num">Available</th></tr></thead>
              <tbody>
                @for (b of w.balances; track b.id) {
                  <tr>
                    <td>{{ b.locationCode }}@if (!b.isPickable) { <span class="muted"> (not pickable)</span> }</td>
                    <td>{{ b.lotNo ?? '—' }}</td>
                    <td>{{ b.expiryDate ? (b.expiryDate | date: 'dd.MM.yyyy') : '—' }}</td>
                    <td class="num">{{ qty(b.onHand) }}</td>
                    <td class="num">{{ qty(b.reserved) }}</td>
                    <td class="num">{{ qty(b.available) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        }
        @if (s.isLotTracked) {
          <h3>Lots</h3>
          <table class="simple">
            <thead><tr><th>Lot</th><th>Expiry</th><th>Status</th><th class="num">On hand</th><th></th></tr></thead>
            <tbody>
              @for (l of lots(); track l.id) {
                <tr>
                  <td>{{ l.lotNo }}</td>
                  <td>{{ l.expiryDate ? (l.expiryDate | date: 'dd.MM.yyyy') : '—' }}</td>
                  <td><p-tag [value]="statusLabel(l.status)" [severity]="l.status === 'released' ? 'success' : l.status === 'quarantine' ? 'warn' : 'danger'" /></td>
                  <td class="num">{{ qty(l.onHand) }}</td>
                  <td>@if (canChangeLots) { <p-button label="Change" size="small" [text]="true" (onClick)="editLot(l)" /> }</td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="muted">No lots yet.</td></tr>
              }
            </tbody>
          </table>
        }
        <p class="links"><a routerLink="/inventory" [queryParams]="{ tab: 'movements' }" (click)="closed.emit()">Movement history</a></p>
      } @else {
        <p class="muted">Loading…</p>
      }
    </p-drawer>

    <p-dialog [visible]="!!editing()" (visibleChange)="!$event && editing.set(null)" header="Lot status" [modal]="true" [style]="{ width: 'min(28rem, 95vw)' }">
      @if (editing(); as l) {
        <p>Lot <strong>{{ l.lotNo }}</strong>. Quarantined and blocked lots are not used for new dispatches.</p>
        <div class="field">
          <label for="lotStatus">Status</label>
          <p-select inputId="lotStatus" [options]="statuses" optionLabel="label" optionValue="value" [(ngModel)]="newStatus" appendTo="body" />
        </div>
        <div class="field">
          <label for="lotReason">Reason</label>
          <input pInputText id="lotReason" [(ngModel)]="reason" placeholder="Required for quarantine or block" />
        </div>
      }
      <ng-template #footer>
        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="editing.set(null)" />
        <p-button label="Save" (onClick)="saveLot()" [loading]="saving()" />
      </ng-template>
    </p-dialog>
  `,
  styles: `
    .title { margin: 0 0 1rem; display: flex; justify-content: space-between; gap: 1rem; }
    .figures { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; margin-bottom: 1rem; }
    .figures div { display: flex; flex-direction: column; }
    .n { font-size: 1.5rem; font-weight: 600; }
    .figures span:last-child { color: var(--wh-muted); font-size: 0.875rem; }
    h3 { font-size: 1rem; margin: 1.25rem 0 0.5rem; }
    .simple { width: 100%; border-collapse: collapse; font-size: 0.9375rem; }
    .simple th { text-align: left; font-weight: 500; color: var(--wh-muted); font-size: 0.8125rem; }
    .simple td, .simple th { padding: 0.375rem 0.5rem; border-bottom: 1px solid var(--wh-line); }
    .simple .num { text-align: right; }
    .links { margin-top: 1.5rem; }
    .field { margin-top: 1rem; }
  `,
})
export class ProductStockDrawer {
  private readonly api = inject(InventoryApi);
  private readonly feedback = inject(ApiFeedback);
  protected readonly canChangeLots = inject(AuthService).hasPermission(Permissions.InventoryAdjustApprove);
  readonly productId = input<string | null>(null);
  readonly warehouseId = input<string | null>(null);
  readonly closed = output<void>();

  protected readonly stock = signal<ProductStockDto | null>(null);
  protected readonly lots = signal<LotDto[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly editing = signal<LotDto | null>(null);
  protected readonly saving = signal(false);
  protected readonly statuses = LOT_STATUSES;
  protected statusLabel(s: LotStatus): string {
    return s.charAt(0).toUpperCase() + s.slice(1);
  }
  protected newStatus: LotStatus = 'released';
  protected reason = '';
  protected readonly qty = qty;

  constructor() {
    effect(() => {
      const id = this.productId();
      this.stock.set(null);
      this.lots.set([]);
      this.error.set(null);
      if (id) void this.load(id);
    });
  }

  protected editLot(l: LotDto): void {
    this.newStatus = l.status;
    this.reason = '';
    this.editing.set(l);
  }

  protected async saveLot(): Promise<void> {
    const l = this.editing();
    if (!l) return;
    this.saving.set(true);
    try {
      await this.api.setLotStatus(l.id, this.newStatus, this.reason || null, l.version);
      this.feedback.success(`Lot ${l.lotNo} updated.`);
      this.editing.set(null);
      const id = this.productId();
      if (id) await this.load(id);
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.saving.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    try {
      const s = await this.api.productStock(id, this.warehouseId() ?? undefined);
      this.stock.set(s);
      if (s.isLotTracked) this.lots.set(await this.api.lots(id));
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    }
  }
}
