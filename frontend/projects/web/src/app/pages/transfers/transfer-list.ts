import { Component, effect, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Drawer } from 'primeng/drawer';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import {
  ApiError, BalanceDto, InventoryApi, LocationDto, newIdempotencyKey, PageQuery, ProductDto, TransferDto, TransferListItemDto, TransfersApi, WarehouseContextService,
} from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { LoadError } from '../../ui/load-error';
import { NeedWarehouse } from '../../ui/need-warehouse';
import { ProductPicker } from '../../ui/product-picker';
import { Lookups } from '../../ui/lookups';
import { debounce, fromLazyEvent } from '../../ui/table-query';
import { qty } from '../../ui/format';

interface MoveLine {
  productId: string | null;
  product: ProductDto | null;
  sources: BalanceDto[];
  sourceId: string | null;
  toLocationId: string | null;
  quantity: number | null;
}

/** Bin-to-bin moves (put-away from RECEIVING, re-slotting). Only unreserved stock can move. */
@Component({
  selector: 'wh-transfer-list',
  imports: [DatePipe, FormsModule, TableModule, Button, Dialog, Drawer, InputText, InputNumber, IconField, InputIcon, Select, LoadError, NeedWarehouse, ProductPicker],
  templateUrl: './transfer-list.html',
  styleUrl: '../../ui/line-editor.scss',
})
export class TransferList {
  private readonly api = inject(TransfersApi);
  private readonly inventory = inject(InventoryApi);
  private readonly lookups = inject(Lookups);
  private readonly feedback = inject(ApiFeedback);
  protected readonly warehouses = inject(WarehouseContextService);
  private readonly table = viewChild<Table>('table');
  protected readonly qty = qty;

  protected readonly rows = signal<TransferListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected search = '';
  private query: PageQuery = { page: 1, pageSize: 25 };

  protected readonly detail = signal<TransferDto | null>(null);
  protected readonly dialog = signal(false);
  protected readonly saving = signal(false);
  protected readonly bins = signal<LocationDto[]>([]);
  protected readonly moveLines = signal<MoveLine[]>([]);
  protected notes = '';
  private key = newIdempotencyKey();

  constructor() {
    effect(() => {
      const wh = this.warehouses.current();
      if (wh) {
        this.apply();
        void this.lookups.binsOf(wh.id).then((b) => this.bins.set(b));
      }
    });
  }

  protected onLazy(e: TableLazyLoadEvent): void {
    this.query = fromLazyEvent(e, this.query);
    void this.load();
  }
  protected readonly onSearch = debounce(() => this.apply());
  protected apply(): void {
    this.query = { ...this.query, page: 1 };
    const t = this.table();
    if (t) t.first = 0;
    void this.load();
  }

  protected async open(row: TransferListItemDto): Promise<void> {
    try {
      this.detail.set(await this.api.getOne(row.id));
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected newMove(): void {
    this.moveLines.set([this.emptyLine()]);
    this.notes = '';
    this.key = newIdempotencyKey();
    this.dialog.set(true);
  }

  protected addLine(): void {
    this.moveLines.update((l) => [...l, this.emptyLine()]);
  }

  protected removeLine(i: number): void {
    this.moveLines.update((l) => (l.length === 1 ? [this.emptyLine()] : l.filter((_, x) => x !== i)));
  }

  protected async onProduct(i: number, p: ProductDto | null): Promise<void> {
    const wh = this.warehouses.current();
    let sources: BalanceDto[] = [];
    if (p && wh) {
      const page = await this.inventory.balances({ warehouseId: wh.id, productId: p.id, pageSize: 200, sortBy: 'locationCode' });
      sources = page.items.filter((b) => b.available > 0);
    }
    this.moveLines.update((lines) => lines.map((l, x) => (x === i ? { ...l, productId: p?.id ?? null, product: p, sources, sourceId: sources.length === 1 ? sources[0]!.id : null } : l)));
  }

  protected sourceLabel(b: BalanceDto): string {
    return `${b.locationCode}${b.lotNo ? ` · lot ${b.lotNo}` : ''} — ${qty(b.available)} ${b.baseUnitCode} free`;
  }

  protected source(l: MoveLine): BalanceDto | undefined {
    return l.sources.find((s) => s.id === l.sourceId);
  }

  protected touch(): void {
    this.moveLines.update((l) => [...l]);
  }

  protected valid(): boolean {
    return this.moveLines().every((l) => l.productId && l.sourceId && l.toLocationId && (l.quantity ?? 0) > 0 && this.source(l)?.locationId !== l.toLocationId);
  }

  protected async save(): Promise<void> {
    const wh = this.warehouses.current();
    if (!wh || !this.valid()) return;
    this.saving.set(true);
    try {
      const created = await this.api.createBinMove({
        warehouseId: wh.id,
        notes: this.notes.trim() || null,
        lines: this.moveLines().map((l) => {
          const s = this.source(l)!;
          return { productId: l.productId!, lotNo: s.lotNo, fromLocationId: s.locationId, toLocationId: l.toLocationId!, quantity: l.quantity!, productUnitId: null };
        }),
      }, { key: this.key });
      this.feedback.success(`Move ${created.number} done.`);
      this.dialog.set(false);
      this.apply();
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.saving.set(false);
    }
  }

  protected async load(): Promise<void> {
    const wh = this.warehouses.current();
    if (!wh) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list({ ...this.query, warehouseId: wh.id, search: this.search || undefined });
      this.rows.set(page.items);
      this.total.set(page.totalCount);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  private emptyLine(): MoveLine {
    return { productId: null, product: null, sources: [], sourceId: null, toLocationId: null, quantity: null };
  }
}
