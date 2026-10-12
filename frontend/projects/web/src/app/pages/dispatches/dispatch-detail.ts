import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { RadioButton } from 'primeng/radiobutton';
import { Textarea } from 'primeng/textarea';
import { DatePicker } from 'primeng/datepicker';
import { Skeleton } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import {
  ApiError, AuthService, BalanceDto, DISCREPANCY_TYPES, DiscrepancyType, dispatchStatusInfo, DispatchDocumentDto, DispatchDto, DispatchesApi, DocumentType,
  InventoryApi, newIdempotencyKey, Permissions, PickListDto, PickResultDto, PickTaskDto, ReservationShortage, ShortShipDecision,
} from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { AuthImage } from '../../ui/auth-image';
import { DocStatus } from '../../ui/doc-status';
import { LoadError } from '../../ui/load-error';
import { SignaturePad } from '../../ui/signature-pad';
import { newId, openBlob, qty, saveBlob } from '../../ui/format';

type ReasonAction = 'return' | 'cancel';

/** Dispatch detail: everything about one dispatch and the workflow buttons the user is allowed to use. */
@Component({
  selector: 'wh-dispatch-detail',
  imports: [
    DatePipe, FormsModule, RouterLink, Button, Dialog, InputText, InputNumber, Select, RadioButton, Textarea, DatePicker, Skeleton, TableModule, Tag,
    AuthImage, DocStatus, LoadError, SignaturePad,
  ],
  templateUrl: './dispatch-detail.html',
  styleUrl: './dispatch-detail.scss',
})
export class DispatchDetail {
  private readonly api = inject(DispatchesApi);
  private readonly inventory = inject(InventoryApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly qty = qty;
  protected readonly discrepancyTypes = DISCREPANCY_TYPES;
  protected readonly statusInfo = dispatchStatusInfo;

  readonly id = input.required<string>();

  protected readonly dispatch = signal<DispatchDto | null>(null);
  protected readonly pickList = signal<PickListDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly shortages = signal<ReservationShortage[]>([]);
  protected readonly recentPicks = signal<PickResultDto[]>([]);

  protected readonly isShort = computed(() => (this.dispatch()?.lines ?? []).some((l) => l.quantityPicked < l.quantity));
  protected readonly showPicking = computed(() => {
    const d = this.dispatch();
    return !!d && (d.status === 'approved' || d.status === 'picking' || d.status === 'readyForDispatch') && this.auth.hasPermission(Permissions.DispatchesPick);
  });

  // Dialog state ------------------------------------------------------------------------------
  protected readonly approveDialog = signal(false);
  protected approveNote = '';
  private approveKey = newIdempotencyKey();

  protected readonly reasonDialog = signal<ReasonAction | null>(null);
  protected reasonText = '';
  private cancelKey = newIdempotencyKey();

  protected readonly shipDialog = signal(false);
  protected carrier = '';
  protected trackingNumber = '';
  protected shortDecision: ShortShipDecision | null = null;
  private shipKey = newIdempotencyKey();

  protected readonly deliverDialog = signal(false);
  protected receiverName = '';
  protected deliveredAt: Date = new Date();
  protected deliveryNotes = '';
  protected deliveredAtMax = new Date();
  protected readonly photos = signal<File[]>([]);
  protected readonly signed = signal(false);

  protected readonly pickTask = signal<PickTaskDto | null>(null);
  protected pickQty: number | null = null;
  protected scanBin = '';
  protected scanCode = '';
  protected scanQty: number | null = 1;

  protected readonly reallocTask = signal<PickTaskDto | null>(null);
  protected readonly reallocOptions = signal<BalanceDto[]>([]);
  protected reallocBalanceId: string | null = null;
  protected reallocQty: number | null = null;

  protected readonly discrepancyDialog = signal(false);
  protected discrepancyType: DiscrepancyType = 'missing';
  protected discrepancyLineId: string | null = null;
  protected discrepancyQty: number | null = null;
  protected discrepancyNote = '';

  protected readonly sendDialog = signal(false);
  protected sendTo = '';

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
  }

  // Loading -----------------------------------------------------------------------------------

  protected async load(id: string): Promise<void> {
    this.loadError.set(null);
    try {
      this.apply(await this.api.getOne(id));
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  private apply(d: DispatchDto): void {
    this.dispatch.set(d);
    if (this.showPicking()) void this.loadPickList();
    else this.pickList.set(null);
  }

  private async loadPickList(): Promise<void> {
    try {
      this.pickList.set(await this.api.pickList(this.id()));
    } catch (e) {
      this.feedback.error(e);
    }
  }

  // Workflow ----------------------------------------------------------------------------------

  protected submit(): void {
    void this.run(async (d) => this.api.submit(d.id), 'Submitted for approval.');
  }

  protected openApprove(): void {
    this.approveNote = '';
    this.shortages.set([]);
    this.approveDialog.set(true);
  }

  protected async approve(): Promise<void> {
    const d = this.dispatch();
    if (!d) return;
    this.busy.set(true);
    try {
      this.apply(await this.api.approve(d.id, this.approveNote.trim() || null, { key: this.approveKey }));
      this.approveKey = newIdempotencyKey();
      this.approveDialog.set(false);
      this.feedback.success('Approved. Stock is reserved.');
    } catch (e) {
      const err = ApiError.from(e);
      const list = (err.problem as { shortages?: ReservationShortage[] }).shortages;
      if (err.problem.code === 'stock.shortage' && list) this.shortages.set(list);
      else this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected openReason(action: ReasonAction): void {
    this.reasonText = '';
    this.reasonDialog.set(action);
  }

  protected async confirmReason(): Promise<void> {
    const action = this.reasonDialog();
    const reason = this.reasonText.trim();
    if (!action || !reason) return;
    const ok = await this.run(async (d) =>
      action === 'return' ? this.api.returnToDraft(d.id, reason) : this.api.cancel(d.id, reason, { key: this.cancelKey }),
      action === 'return' ? 'Returned to draft.' : 'Dispatch cancelled. Reserved stock was released.');
    if (ok) {
      this.reasonDialog.set(null);
      if (action === 'cancel') this.cancelKey = newIdempotencyKey();
    }
  }

  protected startPicking(assignToMe: boolean): void {
    const me = this.auth.user()?.id ?? null;
    void this.run(async (d) => this.api.startPicking(d.id, assignToMe ? me : null), 'Picking started.');
  }

  protected ready(): void {
    const d = this.dispatch();
    if (!d) return;
    const short = d.lines.some((l) => l.quantityPicked < l.quantityReserved);
    if (!short) {
      void this.run(async (x) => this.api.ready(x.id, false), 'Ready to ship.');
      return;
    }
    this.confirm.confirm({
      header: 'Not everything is picked',
      message: 'Some lines are short. Confirm the dispatch as partial? When shipping you will choose to close it short or create a backorder for the rest.',
      acceptLabel: 'Confirm partial',
      rejectLabel: 'Keep picking',
      accept: () => void this.run(async (x) => this.api.ready(x.id, true), 'Ready to ship (partial).'),
    });
  }

  protected reopenPicking(): void {
    void this.run(async (d) => this.api.reopenPicking(d.id, null), 'Picking reopened.');
  }

  protected openShip(): void {
    const d = this.dispatch();
    this.carrier = d?.carrier ?? '';
    this.trackingNumber = d?.trackingNumber ?? '';
    this.shortDecision = null;
    this.shipDialog.set(true);
  }

  protected async ship(): Promise<void> {
    const ok = await this.run(async (d) =>
      this.api.ship(d.id, { carrier: this.carrier.trim() || null, trackingNumber: this.trackingNumber.trim() || null, shortDecision: this.isShort() ? this.shortDecision : null }, { key: this.shipKey }),
      'Shipped. Stock deducted and packing slip created.');
    if (ok) {
      this.shipDialog.set(false);
      this.shipKey = newIdempotencyKey();
    }
  }

  protected openDeliver(): void {
    this.receiverName = '';
    this.deliveredAt = new Date();
    this.deliveredAtMax = new Date(this.deliveredAt.getTime() + 5 * 60_000);
    this.deliveryNotes = '';
    this.photos.set([]);
    this.signed.set(false);
    this.deliverDialog.set(true);
  }

  protected onPhotos(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    this.photos.update((p) => [...p, ...files].slice(0, 10));
    input.value = '';
  }

  protected removePhoto(i: number): void {
    this.photos.update((p) => p.filter((_, x) => x !== i));
  }

  protected async deliver(pad: SignaturePad): Promise<void> {
    const signature = await pad.toBlob();
    if (!signature || !this.receiverName.trim() || this.photos().length === 0) return;
    const ok = await this.run(async (d) => this.api.deliver(d.id, {
      receiverName: this.receiverName.trim(), deliveredAt: this.deliveredAt.toISOString(), notes: this.deliveryNotes.trim() || null, signature, photos: this.photos(),
    }), 'Delivery recorded. The signed delivery note is being sent to the customer.');
    if (ok) this.deliverDialog.set(false);
  }

  // Picking -----------------------------------------------------------------------------------

  protected openPick(t: PickTaskDto): void {
    this.pickQty = t.remaining;
    this.pickTask.set(t);
  }

  protected async confirmPick(): Promise<void> {
    const t = this.pickTask();
    if (!t || !this.pickQty) return;
    const ok = await this.doPick({ scanId: newId(), location: t.locationCode, productId: t.productId, lotNo: t.lotNo, quantity: this.pickQty });
    if (ok) this.pickTask.set(null);
  }

  /** Scanner box: bin + product/pack barcode (USB or Bluetooth scanners type into the fields and press Enter). */
  protected async scan(lotInput?: string): Promise<void> {
    if (!this.scanBin.trim() || !this.scanCode.trim()) return;
    const ok = await this.doPick({ scanId: newId(), location: this.scanBin.trim(), barcode: this.scanCode.trim(), lotNo: lotInput?.trim() || null, quantity: this.scanQty ?? 1 });
    if (ok) this.scanCode = '';
  }

  private async doPick(body: Parameters<DispatchesApi['pick']>[1]): Promise<boolean> {
    this.busy.set(true);
    try {
      const result = await this.api.pick(this.id(), body);
      this.recentPicks.update((r) => [result, ...r].slice(0, 15));
      this.feedback.success(`${result.sku}: ${qty(result.quantity)} picked from ${result.locationCode}.`);
      await this.refresh();
      return true;
    } catch (e) {
      this.feedback.error(e);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  protected async undo(p: PickResultDto): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.undoPick(this.id(), p.pickId, newId());
      this.recentPicks.update((r) => r.filter((x) => x.pickId !== p.pickId));
      this.feedback.success('Pick undone.');
      await this.refresh();
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async openRealloc(t: PickTaskDto): Promise<void> {
    const d = this.dispatch();
    if (!d) return;
    this.reallocQty = t.remaining;
    this.reallocBalanceId = null;
    this.reallocOptions.set([]);
    this.reallocTask.set(t);
    try {
      const page = await this.inventory.balances({ warehouseId: d.warehouseId, productId: t.productId, pageSize: 200, sortBy: 'locationCode' });
      this.reallocOptions.set(page.items.filter((b) => b.available > 0 && b.isPickable && !(b.locationId === t.locationId && b.lotId === t.lotId)
        && (b.lotStatus === null || b.lotStatus === 'released')));
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected balanceLabel(b: BalanceDto): string {
    return `${b.locationCode}${b.lotNo ? ` · lot ${b.lotNo}` : ''}${b.expiryDate ? ` · exp. ${b.expiryDate}` : ''} — ${qty(b.available)} free`;
  }

  protected async confirmRealloc(): Promise<void> {
    const t = this.reallocTask();
    const b = this.reallocOptions().find((x) => x.id === this.reallocBalanceId);
    if (!t || !b || !this.reallocQty) return;
    this.busy.set(true);
    try {
      this.pickList.set(await this.api.reallocate(this.id(), { reservationId: t.reservationId, location: b.locationCode, lotNo: b.lotNo, quantity: this.reallocQty }));
      this.reallocTask.set(null);
      this.feedback.success(`Now taking ${qty(this.reallocQty)} from ${b.locationCode}.`);
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected openDiscrepancy(lineId: string | null = null): void {
    this.discrepancyType = 'missing';
    this.discrepancyLineId = lineId;
    this.discrepancyQty = null;
    this.discrepancyNote = '';
    this.discrepancyDialog.set(true);
  }

  protected async saveDiscrepancy(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.reportDiscrepancy(this.id(), { type: this.discrepancyType, lineId: this.discrepancyLineId, quantity: this.discrepancyQty, note: this.discrepancyNote.trim() || null });
      this.discrepancyDialog.set(false);
      this.feedback.success('Problem recorded.');
      await this.refresh();
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  // Documents -----------------------------------------------------------------------------------

  protected async openDocument(doc: DispatchDocumentDto, download = false): Promise<void> {
    try {
      const blob = await this.api.document(this.id(), doc.id);
      if (download) saveBlob(blob, doc.fileName ?? 'document.pdf');
      else openBlob(blob);
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected regenerate(type: DocumentType): void {
    void this.run(async (d) => this.api.regenerate(d.id, type), 'Document generated again.');
  }

  protected openSend(): void {
    this.sendTo = this.dispatch()?.customerEmail ?? '';
    this.sendDialog.set(true);
  }

  protected async send(): Promise<void> {
    const ok = await this.run(async (d) => this.api.sendDeliveryNote(d.id, this.sendTo.trim() || null), 'Email queued.');
    if (ok) this.sendDialog.set(false);
  }

  /** One stable loader per file, so the image component does not refetch on every change detection. */
  private readonly podLoaders = new Map<string, () => Promise<Blob>>();
  protected podLoader(fileId: string): () => Promise<Blob> {
    let loader = this.podLoaders.get(fileId);
    if (!loader) {
      const dispatchId = this.id();
      loader = () => this.api.podFile(dispatchId, fileId);
      this.podLoaders.set(fileId, loader);
    }
    return loader;
  }

  protected latest(type: DocumentType): DispatchDocumentDto | undefined {
    return [...(this.dispatch()?.documents ?? [])].reverse().find((d) => d.type === type);
  }

  protected editDraft(): void {
    void this.router.navigate(['/dispatches', this.id(), 'edit']);
  }

  // Helpers -----------------------------------------------------------------------------------

  private async refresh(): Promise<void> {
    const d = await this.api.getOne(this.id());
    this.apply(d);
  }

  /** Runs a command that returns the updated dispatch. Returns true on success. */
  private async run(command: (d: DispatchDto) => Promise<DispatchDto>, success: string): Promise<boolean> {
    const d = this.dispatch();
    if (!d) return false;
    this.busy.set(true);
    try {
      this.apply(await command(d));
      this.feedback.success(success);
      return true;
    } catch (e) {
      this.feedback.error(e);
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
