import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Tag } from 'primeng/tag';
import {
  ApiError, AuthService, Permissions, PurchaseOrdersApi, PurchasingApi, ReorderSuggestionDto, SupplierListItemDto, SuppliersApi, WarehouseContextService,
} from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { qty } from '../../ui/format';

interface Row {
  s: ReorderSuggestionDto;
  selected: boolean;
  /** In the purchase unit. */
  quantity: number | null;
  supplierId: string | null;
}

/**
 * Products below their minimum stock, with the quantity to order (up to the order-up-to level, minus what is already on
 * order, in whole purchase units). Selected lines become one draft purchase order per supplier.
 */
@Component({
  selector: 'wh-reorder-page',
  imports: [FormsModule, RouterLink, TableModule, Button, Checkbox, InputNumber, InputText, Select, Tag, LoadError, NeedWarehouse],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Reorder</h1>
          <p>Products whose available stock (on hand − reserved) is below their minimum. Set the minimum, order-up-to level, usual supplier and
            order unit on each product.</p>
        </div>
        @if (warehouses.current()) {
          <div class="page-actions">
            <p-button label="Refresh" icon="pi pi-refresh" severity="secondary" [outlined]="true" (onClick)="load()" [loading]="loading()" />
            @if (canManage) {
              <p-button [label]="'Create ' + (orderCount() === 1 ? '1 draft order' : orderCount() + ' draft orders')" icon="pi pi-file-plus"
                (onClick)="createOrders()" [disabled]="orderCount() === 0 || busy()" [loading]="busy()" data-testid="create-orders" />
            }
          </div>
        }
      </header>

      @if (!warehouses.current()) {
        <wh-need-warehouse />
      } @else {
        @if (error(); as m) { <wh-load-error [message]="m" (retry)="load()" /> }
        <p-table [value]="rows()" [loading]="loading()" dataKey="s.productId" [tableStyle]="{ 'min-width': '64rem' }" rowGroupMode="subheader" groupRowsBy="s.supplierName">
          <ng-template #header>
            <tr>
              @if (canManage) { <th style="width: 3rem"><p-checkbox [binary]="true" [ngModel]="allSelected()" (ngModelChange)="selectAll($event)" ariaLabel="Select all" /></th> }
              <th>Product</th>
              <th class="num">Available</th>
              <th class="num">Min. / up to</th>
              <th class="num">On order</th>
              <th style="width: 11rem">Order</th>
              <th style="width: 16rem">Supplier</th>
            </tr>
          </ng-template>
          <ng-template #groupheader let-r>
            <tr class="group"><td [attr.colspan]="canManage ? 7 : 6"><strong>{{ r.s.supplierName ?? 'No usual supplier' }}</strong>
              @if (r.s.supplierLeadTimeDays !== null) { <span class="muted"> · {{ r.s.supplierLeadTimeDays }} days lead time</span> }</td></tr>
          </ng-template>
          <ng-template #body let-r>
            <tr [class.covered]="r.s.suggestedQuantity === 0">
              @if (canManage) { <td><p-checkbox [binary]="true" [(ngModel)]="r.selected" (ngModelChange)="touch()" [ariaLabel]="'Order ' + r.s.sku" /></td> }
              <td><a [routerLink]="['/products', r.s.productId]"><span class="code">{{ r.s.sku }}</span></a> {{ r.s.productName }}</td>
              <td class="num strong">{{ qty(r.s.available) }} {{ r.s.baseUnitCode }}<div class="muted small">{{ qty(r.s.onHand) }} on hand</div></td>
              <td class="num">{{ qty(r.s.minStockQty) }} / {{ qty(r.s.maxStockQty ?? r.s.minStockQty) }}</td>
              <td class="num">
                {{ r.s.onOrder ? qty(r.s.onOrder) : '—' }}
                @if (r.s.openOrderNumbers.length) { <div class="muted small">{{ r.s.openOrderNumbers.join(', ') }}</div> }
              </td>
              <td>
                @if (canManage) {
                  <div class="qty">
                    <p-inputnumber [(ngModel)]="r.quantity" (ngModelChange)="touch()" [min]="0" [maxFractionDigits]="3" [useGrouping]="false" [inputStyle]="{ width: '5rem' }"
                      [ariaLabel]="'Quantity of ' + r.s.sku" />
                    <span>{{ r.s.purchaseUnitCode }}</span>
                  </div>
                  @if (r.s.purchaseFactor !== 1 && r.quantity) { <div class="muted small">= {{ qty(r.quantity * r.s.purchaseFactor) }} {{ r.s.baseUnitCode }}</div> }
                } @else {
                  {{ qty(r.s.suggestedPurchaseQuantity) }} {{ r.s.purchaseUnitCode }}
                }
                @if (r.s.suggestedQuantity === 0) { <p-tag value="Covered by open orders" severity="info" /> }
              </td>
              <td>
                @if (canManage) {
                  <p-select [options]="suppliers()" [(ngModel)]="r.supplierId" (ngModelChange)="touch()" optionLabel="name" optionValue="id" [filter]="true" filterBy="name,code"
                    placeholder="Choose a supplier" appendTo="body" [style]="{ width: '100%' }" [ariaLabel]="'Supplier for ' + r.s.sku" />
                } @else {
                  {{ r.s.supplierName ?? '—' }}
                }
              </td>
            </tr>
          </ng-template>
          <ng-template #emptymessage>
            <tr><td [attr.colspan]="canManage ? 7 : 6" class="empty">Nothing to reorder: every product with a minimum stock is above it.</td></tr>
          </ng-template>
        </p-table>

        @if (canManage) {
          <div class="panel alert">
            <h2>Daily low-stock email</h2>
            <p class="muted">Sent once a day from 07:00 (company time) when products need ordering. Leave empty for no email.</p>
            <div class="row">
              <input pInputText [(ngModel)]="alertEmails" placeholder="buyer@example.com, manager@example.com" aria-label="Low-stock email recipients" class="grow" />
              <p-button label="Save" severity="secondary" [outlined]="true" (onClick)="saveAlert()" [loading]="savingAlert()" />
            </div>
          </div>
        }
      }
    </section>
  `,
  styles: `
    .qty { display: flex; align-items: center; gap: 0.5rem; }
    .small { font-size: 0.8rem; }
    tr.covered td { color: var(--wh-muted); }
    tr.group td { background: var(--wh-panel); }
    .alert { margin-top: 1.5rem; }
    .alert .row { display: flex; gap: 0.75rem; flex-wrap: wrap; }
    .grow { flex: 1 1 20rem; }
  `,
})
export class ReorderPage {
  private readonly api = inject(PurchasingApi);
  private readonly orders = inject(PurchaseOrdersApi);
  private readonly suppliersApi = inject(SuppliersApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.PurchasingManage);
  protected readonly qty = qty;

  protected readonly rows = signal<Row[]>([]);
  protected readonly suppliers = signal<SupplierListItemDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly savingAlert = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Bumped when a row changes (rows are mutated in place by ngModel). */
  private readonly version = signal(0);
  protected alertEmails = '';

  protected readonly allSelected = computed(() => {
    this.version();
    return this.rows().length > 0 && this.rows().every((r) => r.selected);
  });
  protected readonly orderCount = computed(() => {
    this.version();
    return new Set(this.ready().map((r) => r.supplierId)).size;
  });

  constructor() {
    effect(() => {
      if (this.warehouses.current()) void this.load();
    });
    if (this.canManage) {
      void this.suppliersApi.list({ page: 1, pageSize: 200, isActive: true, sortBy: 'name' }).then((p) => this.suppliers.set(p.items)).catch((e) => this.feedback.error(e));
      void this.api.settings().then((s) => (this.alertEmails = s.lowStockAlertEmails ?? '')).catch(() => undefined);
    }
  }

  protected touch(): void {
    this.version.update((v) => v + 1);
  }

  protected selectAll(value: boolean): void {
    for (const r of this.rows()) r.selected = value && (r.quantity ?? 0) > 0;
    this.touch();
  }

  private ready(): Row[] {
    return this.rows().filter((r) => r.selected && (r.quantity ?? 0) > 0 && r.supplierId);
  }

  protected async load(): Promise<void> {
    const wh = this.warehouses.current();
    if (!wh) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const list = await this.api.suggestions(wh.id);
      this.rows.set(list.map((s) => ({ s, selected: s.suggestedQuantity > 0 && !!s.supplierId, quantity: s.suggestedPurchaseQuantity || null, supplierId: s.supplierId })));
      this.touch();
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  /** One draft order per supplier with the selected lines; opens the order when there is only one. */
  protected async createOrders(): Promise<void> {
    const wh = this.warehouses.current();
    const selected = this.rows().filter((r) => r.selected && (r.quantity ?? 0) > 0);
    if (!wh || selected.length === 0) return;
    if (selected.some((r) => !r.supplierId)) {
      this.feedback.warn('Choose a supplier for every selected product.');
      return;
    }
    const bySupplier = new Map<string, Row[]>();
    for (const r of selected) bySupplier.set(r.supplierId!, [...(bySupplier.get(r.supplierId!) ?? []), r]);
    this.busy.set(true);
    const created: { id: string; number: string }[] = [];
    try {
      for (const [supplierId, rows] of bySupplier) {
        const order = await this.orders.create(wh.id, {
          supplierId, orderDate: null, expectedDate: null, supplierReference: null, notes: null,
          lines: rows.map((r) => ({ productId: r.s.productId, productUnitId: r.s.purchaseProductUnitId, quantity: r.quantity!, notes: null })),
        });
        created.push(order);
      }
      this.feedback.success(`Draft order${created.length === 1 ? '' : 's'} ${created.map((c) => c.number).join(', ')} created: check and place ${created.length === 1 ? 'it' : 'them'}.`);
      if (created.length === 1) await this.router.navigate(['/purchasing/orders', created[0]!.id]);
      else await this.router.navigate(['/purchasing/orders'], { queryParams: { status: 'draft' } });
    } catch (e) {
      this.feedback.error(e);
      if (created.length > 0) this.feedback.warn(`Already created: ${created.map((c) => c.number).join(', ')}.`);
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveAlert(): Promise<void> {
    this.savingAlert.set(true);
    try {
      const s = await this.api.updateSettings(this.alertEmails.trim() || null);
      this.alertEmails = s.lowStockAlertEmails ?? '';
      this.feedback.success(s.lowStockAlertEmails ? 'Low-stock email turned on.' : 'Low-stock email turned off.');
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.savingAlert.set(false);
    }
  }
}
