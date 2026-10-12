import { Component, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActionSheetController, AlertController, IonButton, IonIcon, IonInput, IonItem, IonLabel, IonList, IonNote, IonProgressBar } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { arrowUndoOutline, checkmarkCircle, flagOutline, swapHorizontalOutline } from 'ionicons/icons';
import {
  ApiError, AuthService, BarcodeLookupDto, DISCREPANCY_TYPES, DiscrepancyType, DispatchDto, DispatchesApi, InventoryApi, newId, PickListDto, PickResultDto,
  PickTaskDto, ProductsApi, qty,
} from '@wh/shared';
import { Feedback } from '../../core/feedback';
import { QtyStepper } from '../../ui/qty-stepper';
import { ScanField } from '../../ui/scan-field';

/** The product scanned for the current pick, resolved by the server's barcode lookup. */
interface ScannedProduct {
  code: string;
  lookup: BarcodeLookupDto;
  /** Kept across a network failure so the retry cannot be counted twice. */
  scanId: string;
}

/**
 * Guided picking: scan the bin label, scan the product or carton barcode, confirm the quantity. The app checks the
 * scans against the pick list for quick feedback; the server validates again and records the pick (idempotent per scan).
 */
@Component({
  selector: 'wh-pick-panel',
  imports: [DatePipe, FormsModule, IonButton, IonIcon, IonInput, IonItem, IonLabel, IonList, IonNote, IonProgressBar, QtyStepper, ScanField],
  templateUrl: './pick-panel.html',
  styleUrl: './pick-panel.scss',
})
export class PickPanel {
  private readonly api = inject(DispatchesApi);
  private readonly products = inject(ProductsApi);
  private readonly inventory = inject(InventoryApi);
  private readonly feedback = inject(Feedback);
  private readonly alerts = inject(AlertController);
  private readonly sheets = inject(ActionSheetController);
  private readonly auth = inject(AuthService);
  protected readonly qty = qty;

  readonly dispatch = input.required<DispatchDto>();
  /** The dispatch changed on the server (status, quantities): the page reloads it. */
  readonly changed = output<void>();

  private readonly binField = viewChild<ScanField>('binField');
  private readonly productField = viewChild<ScanField>('productField');

  protected readonly pickList = signal<PickListDto | null>(null);
  protected readonly busy = signal(false);
  protected readonly bin = signal('');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly scanned = signal<ScannedProduct | null>(null);
  protected readonly quantity = signal<number | null>(1);
  protected lotNo = '';
  protected readonly recent = signal<PickResultDto[]>([]);

  protected readonly openTasks = computed(() => (this.pickList()?.tasks ?? []).filter((t) => t.remaining > 0));
  protected readonly current = computed<PickTaskDto | null>(() =>
    this.openTasks().find((t) => t.reservationId === this.selectedId()) ?? this.openTasks()[0] ?? null);
  protected readonly progress = computed(() => {
    const lines = this.pickList()?.lines ?? this.dispatch().lines;
    // Lines rather than quantities: products can have different units (EA, kg…).
    const total = lines.filter((l) => l.quantityReserved > 0).length;
    const done = lines.filter((l) => l.quantityReserved > 0 && l.quantityPicked >= l.quantityReserved).length;
    const ratio = lines.reduce((s, l) => s + (l.quantityReserved > 0 ? Math.min(1, l.quantityPicked / l.quantityReserved) : 0), 0) / Math.max(1, total);
    return { done, total, ratio, anyPicked: lines.some((l) => l.quantityPicked > 0) };
  });
  /** Base-unit quantity the confirm button will pick. */
  protected readonly baseQty = computed(() => (this.quantity() ?? 0) * (this.scanned()?.lookup.factorToBase ?? 1));
  protected readonly tooMuch = computed(() => {
    const t = this.current();
    return !!t && !!this.scanned() && this.baseQty() > t.remaining + 1e-9;
  });

  constructor() {
    addIcons({ arrowUndoOutline, checkmarkCircle, flagOutline, swapHorizontalOutline });
    effect(() => {
      const d = this.dispatch();
      untracked(() => void this.loadPickList(d.id));
    });
  }

  private async loadPickList(id: string): Promise<void> {
    try {
      this.pickList.set(await this.api.pickList(id));
    } catch (e) {
      await this.feedback.error(e);
    }
  }

  // ---- Scans ---------------------------------------------------------------------------------

  protected onBin(code: string): void {
    const upper = code.toUpperCase();
    const matches = (t: PickTaskDto) => t.locationCode.toUpperCase() === upper || t.locationBarcode?.toUpperCase() === upper;
    const here = this.openTasks().filter(matches);
    this.bin.set(here[0]?.locationCode ?? code);
    this.clearScan();
    const current = this.current();
    if (current && matches(current)) {
      // Right bin.
    } else if (here.length > 0) {
      this.selectedId.set(here[0]!.reservationId);
    } else {
      void this.feedback.warn(`Nothing left to pick in bin ${code}${current ? `. Next bin: ${current.locationCode}` : ''}.`);
    }
    this.productField()?.focus();
  }

  protected async onProduct(code: string): Promise<void> {
    if (this.busy()) {
      await this.feedback.warn('Still working on the previous scan. Scan again in a moment.');
      return;
    }
    if (!this.bin()) {
      await this.feedback.warn('Scan the bin label first.');
      this.binField()?.focus();
      return;
    }
    this.busy.set(true);
    try {
      const lookup = await this.products.lookupBarcode(code);
      this.selectFor(lookup, code);
    } catch (e) {
      if (ApiError.from(e).kind === 'not-found') await this.feedback.warn(`Unknown barcode ${code}. Check the label or report the problem.`);
      else await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  /** Matches a scanned product to a task: same bin first, else wherever the product is still to be picked. */
  private selectFor(lookup: BarcodeLookupDto, code: string): void {
    const d = this.pickList() ?? { lines: this.dispatch().lines };
    if (!lookup.isActive) {
      void this.feedback.warn(`${lookup.sku} is inactive.`);
    }
    if (!d.lines.some((l) => l.productId === lookup.productId)) {
      void this.feedback.warn(`${lookup.sku} is not on this dispatch. Put it back.`);
      return;
    }
    const tasks = this.openTasks().filter((t) => t.productId === lookup.productId);
    if (tasks.length === 0) {
      void this.feedback.warn(`${lookup.sku} is already fully picked.`);
      return;
    }
    const bin = this.bin().toUpperCase();
    const atBin = tasks.find((t) => t.locationCode.toUpperCase() === bin);
    const task = atBin ?? tasks[0]!;
    this.selectedId.set(task.reservationId);
    if (!atBin) void this.feedback.warn(`${lookup.sku} is allocated to bin ${task.locationCode}, not ${this.bin()}. Pick it there, or use "Other bin".`);
    this.scanned.set({ code, lookup, scanId: newId() });
    this.lotNo = task.lotNo ?? '';
    this.quantity.set(Math.max(1, Math.floor(task.remaining / lookup.factorToBase + 1e-9)));
  }

  protected clearScan(): void {
    this.scanned.set(null);
    this.quantity.set(1);
    this.lotNo = '';
  }

  protected selectTask(t: PickTaskDto): void {
    if (t.remaining <= 0) return;
    this.selectedId.set(t.reservationId);
    this.clearScan();
    this.binField()?.focus();
  }

  // ---- Pick ----------------------------------------------------------------------------------

  protected async pick(): Promise<void> {
    const s = this.scanned();
    const q = this.quantity();
    if (!s || !q || q <= 0 || this.busy() || this.tooMuch()) return;
    this.busy.set(true);
    try {
      const result = await this.api.pick(this.dispatch().id, {
        scanId: s.scanId, location: this.bin(), barcode: s.code, lotNo: s.lookup.isLotTracked ? this.lotNo.trim() || null : null, quantity: q,
      });
      this.recent.update((r) => [result, ...r.filter((x) => x.pickId !== result.pickId)].slice(0, 10));
      this.clearScan();
      await this.feedback.success(result.replayed ? `Already recorded: ${result.sku} ${qty(result.quantity)}.` : `${result.sku}: ${qty(result.quantity)} picked from ${result.locationCode}.`);
      this.changed.emit(); // the page reloads the dispatch, which reloads the pick list
      this.productField()?.focus();
    } catch (e) {
      const network = ApiError.from(e).kind === 'network';
      // On a network error keep the scan id: the retry is recognised by the server if the first try did arrive.
      if (!network) this.scanned.update((x) => (x ? { ...x, scanId: newId() } : x));
      await this.feedback.error(e, { retrySafe: network });
    } finally {
      this.busy.set(false);
    }
  }

  protected async undo(p: PickResultDto): Promise<void> {
    const ok = await this.feedback.confirm('Undo pick', `Put back ${qty(p.quantity)} ${p.sku} to bin ${p.locationCode} and undo the pick?`, 'Undo pick', 'Keep');
    if (!ok) return;
    this.busy.set(true);
    try {
      await this.api.undoPick(this.dispatch().id, p.pickId, newId());
      this.recent.update((r) => r.filter((x) => x.pickId !== p.pickId));
      await this.feedback.success('Pick undone.');
      this.changed.emit();
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  // ---- Other bin (reallocate), problems, done ---------------------------------------------------

  protected async otherBin(t: PickTaskDto): Promise<void> {
    let options;
    try {
      const page = await this.inventory.balances({ warehouseId: this.dispatch().warehouseId, productId: t.productId, pageSize: 100, sortBy: 'locationCode' });
      options = page.items.filter((b) => b.available > 0 && b.isPickable && (b.lotStatus === null || b.lotStatus === 'released')
        && !(b.locationId === t.locationId && b.lotId === t.lotId));
    } catch (e) {
      await this.feedback.error(e);
      return;
    }
    if (options.length === 0) {
      await this.feedback.warn(`No other bin has free stock of ${t.sku}. Report the problem instead.`);
      return;
    }
    const sheet = await this.sheets.create({
      header: `Take ${t.sku} from another bin`,
      buttons: [
        ...options.map((b) => ({
          text: `${b.locationCode}${b.lotNo ? ` · lot ${b.lotNo}` : ''}${b.expiryDate ? ` · exp. ${b.expiryDate}` : ''} — ${qty(b.available)} free`,
          data: b,
        })),
        { text: 'Cancel', role: 'cancel' },
      ],
    });
    await sheet.present();
    const { data } = await sheet.onDidDismiss();
    const b = data as (typeof options)[number] | undefined;
    if (!b) return;
    const max = Math.min(t.remaining, b.available);
    const alert = await this.alerts.create({
      header: `From ${b.locationCode}`,
      message: `How much of the ${qty(t.remaining)} ${t.baseUnitCode} still to pick will you take from ${b.locationCode}?`,
      inputs: [{ name: 'qty', type: 'number', value: max, min: 0, max, attributes: { inputmode: 'decimal' } }],
      buttons: [{ text: 'Cancel', role: 'cancel' }, { text: 'Change bin', role: 'confirm' }],
    });
    await alert.present();
    const result = await alert.onDidDismiss<{ values: { qty: string } }>();
    const quantity = Number(result.data?.values.qty);
    if (result.role !== 'confirm' || !(quantity > 0)) return;
    this.busy.set(true);
    try {
      const list = await this.api.reallocate(this.dispatch().id, { reservationId: t.reservationId, location: b.locationCode, lotNo: b.lotNo, quantity });
      this.pickList.set(list);
      this.bin.set('');
      this.clearScan();
      await this.feedback.success(`Now taking ${qty(quantity)} ${t.sku} from ${b.locationCode}.`);
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async reportProblem(): Promise<void> {
    const t = this.current();
    const typeAlert = await this.alerts.create({
      header: 'Report a problem',
      subHeader: t ? `${t.sku} · bin ${t.locationCode}` : undefined,
      inputs: DISCREPANCY_TYPES.map((d, i) => ({ type: 'radio' as const, label: d.label, value: d.value, checked: i === 0 })),
      buttons: [{ text: 'Cancel', role: 'cancel' }, { text: 'Next', role: 'confirm' }],
    });
    await typeAlert.present();
    const typed = await typeAlert.onDidDismiss<{ values: DiscrepancyType }>();
    if (typed.role !== 'confirm' || !typed.data) return;
    const type = typed.data.values;

    const detail = await this.alerts.create({
      header: DISCREPANCY_TYPES.find((d) => d.value === type)?.label ?? 'Problem',
      inputs: [
        { name: 'quantity', type: 'number', placeholder: `Quantity${t ? ` (${t.baseUnitCode})` : ''}`, attributes: { inputmode: 'decimal' } },
        { name: 'note', type: 'textarea', placeholder: 'What happened?' },
      ],
      buttons: [{ text: 'Cancel', role: 'cancel' }, { text: 'Report', role: 'confirm' }],
    });
    await detail.present();
    const res = await detail.onDidDismiss<{ values: { quantity: string; note: string } }>();
    if (res.role !== 'confirm') return;
    const quantity = res.data?.values.quantity ? Number(res.data.values.quantity) : null;
    this.busy.set(true);
    try {
      await this.api.reportDiscrepancy(this.dispatch().id, {
        type, lineId: t?.lineId ?? null, location: this.bin() || t?.locationCode || null, barcode: this.scanned()?.code ?? null,
        quantity: quantity && quantity > 0 ? quantity : null, note: res.data?.values.note?.trim() || null,
      });
      await this.feedback.success('Problem reported to the office.');
      this.changed.emit();
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async takeJob(): Promise<void> {
    const me = this.auth.user()?.id ?? null;
    this.busy.set(true);
    try {
      await this.api.startPicking(this.dispatch().id, me);
      await this.feedback.success('Assigned to you. Start picking.');
      this.changed.emit();
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async done(): Promise<void> {
    const lines = this.pickList()?.lines ?? this.dispatch().lines;
    const short = lines.filter((l) => l.quantityPicked < l.quantityReserved);
    if (short.length > 0) {
      const ok = await this.feedback.confirm('Not everything is picked',
        `${short.map((l) => `${l.sku}: ${qty(l.quantityPicked)} of ${qty(l.quantityReserved)}`).join(', ')}. Finish as a partial dispatch?`,
        'Finish partial', 'Keep picking');
      if (!ok) return;
    }
    this.busy.set(true);
    try {
      await this.api.ready(this.dispatch().id, short.length > 0);
      await this.feedback.success(short.length > 0 ? 'Ready to ship (partial).' : 'Picking complete. Ready to ship.');
      this.changed.emit();
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
