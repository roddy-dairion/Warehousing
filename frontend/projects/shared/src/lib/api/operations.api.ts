import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { idempotent } from '../http/context';
import { toHttpParams } from '../http/query';
import { PagedResult, PageQuery } from '../models/paging';
import {
  AdjustmentDto,
  AdjustmentLineInput,
  AdjustmentListItemDto,
  AdjustmentReason,
  AdjustmentStatus,
  AuditEntryDto,
  BalanceDto,
  CompanyProfileDto,
  DiscrepancyDto,
  DiscrepancyType,
  DispatchDto,
  DispatchInput,
  DispatchListItemDto,
  DispatchStatus,
  DocumentType,
  ExpiringLotDto,
  LotDto,
  LotStatus,
  MoveLineInput,
  MovementDto,
  MovementType,
  PickInput,
  PickListDto,
  PickResultDto,
  ProductStockDto,
  PurchaseOrderDto,
  PurchaseOrderInput,
  PurchaseOrderListItemDto,
  PurchaseOrderStatus,
  PurchasingSettingsDto,
  ReceiptDto,
  ReceiptInput,
  ReceiptListItemDto,
  ReceiptStatus,
  ReconciliationResult,
  ReorderSuggestionDto,
  ReportInfoDto,
  ReportResult,
  ShortShipDecision,
  StockSummaryDto,
  SupplierDto,
  SupplierInput,
  SupplierListItemDto,
  TemplateDto,
  TransferDto,
  TransferListItemDto,
  WarehouseDashboardDto,
} from '../models/operations';
import { ApiBase } from './api-base';

/**
 * Stock-changing commands take an idempotency key. Create it once per user action
 * (e.g. when the confirm dialog opens) and pass the same key if the user retries.
 */
type Key = { key?: string };

@Injectable({ providedIn: 'root' })
export class InventoryApi extends ApiBase {
  balances(query: PageQuery & { warehouseId: string; productId?: string; locationId?: string; lotId?: string; onlyWithStock?: boolean }): Promise<PagedResult<BalanceDto>> {
    return this.get('/inventory/balances', toHttpParams(query));
  }
  summary(query: PageQuery & { warehouseId: string; categoryId?: string; lowStockOnly?: boolean; onlyWithStock?: boolean }): Promise<PagedResult<StockSummaryDto>> {
    return this.get('/inventory/summary', toHttpParams(query));
  }
  productStock(productId: string, warehouseId?: string): Promise<ProductStockDto> {
    return this.get(`/inventory/products/${productId}`, warehouseId ? new HttpParams().set('warehouseId', warehouseId) : undefined);
  }
  movements(query: PageQuery & { warehouseId: string; productId?: string; locationId?: string; type?: MovementType; sourceId?: string; from?: string; to?: string }): Promise<PagedResult<MovementDto>> {
    return this.get('/inventory/movements', toHttpParams(query));
  }
  expiring(warehouseId: string, days?: number): Promise<ExpiringLotDto[]> {
    return this.get('/inventory/expiring', toHttpParams({ warehouseId, days }));
  }
  reconciliation(warehouseId: string): Promise<ReconciliationResult> {
    return this.get('/inventory/reconciliation', toHttpParams({ warehouseId }));
  }
  lots(productId: string, onlyWithStock = false): Promise<LotDto[]> {
    return this.get(`/products/${productId}/lots`, toHttpParams({ onlyWithStock }));
  }
  setLotStatus(lotId: string, status: LotStatus, reason: string | null, version: number): Promise<LotDto> {
    return this.post(`/lots/${lotId}/status`, { status, reason, version });
  }
}

@Injectable({ providedIn: 'root' })
export class ReceiptsApi extends ApiBase {
  list(query: PageQuery & { warehouseId: string; status?: ReceiptStatus; from?: string; to?: string; purchaseOrderId?: string }): Promise<PagedResult<ReceiptListItemDto>> {
    return this.get('/receipts', toHttpParams(query));
  }
  getOne(id: string): Promise<ReceiptDto> {
    return this.get(`/receipts/${id}`);
  }
  create(warehouseId: string, body: ReceiptInput): Promise<ReceiptDto> {
    return this.post('/receipts', { warehouseId, ...body });
  }
  update(id: string, body: ReceiptInput & { receiptDate: string; version: number }): Promise<ReceiptDto> {
    return this.put(`/receipts/${id}`, body);
  }
  postReceipt(id: string, { key }: Key = {}): Promise<ReceiptDto> {
    return firstValueFrom(this.http.post<ReceiptDto>(`${this.base}/receipts/${id}/post`, {}, { context: idempotent(key) }));
  }
  reverse(id: string, reason: string, { key }: Key = {}): Promise<ReceiptDto> {
    return firstValueFrom(this.http.post<ReceiptDto>(`${this.base}/receipts/${id}/reverse`, { reason }, { context: idempotent(key) }));
  }
  cancel(id: string): Promise<ReceiptDto> {
    return this.post(`/receipts/${id}/cancel`);
  }
}

@Injectable({ providedIn: 'root' })
export class SuppliersApi extends ApiBase {
  list(query: PageQuery): Promise<PagedResult<SupplierListItemDto>> {
    return this.get('/suppliers', toHttpParams(query));
  }
  getOne(id: string): Promise<SupplierDto> {
    return this.get(`/suppliers/${id}`);
  }
  create(body: SupplierInput & { code: string }): Promise<SupplierDto> {
    return this.post('/suppliers', body);
  }
  update(id: string, body: SupplierInput & { version: number }): Promise<SupplierDto> {
    return this.put(`/suppliers/${id}`, body);
  }
  setActive(id: string, active: boolean): Promise<void> {
    return this.post(`/suppliers/${id}/${active ? 'activate' : 'deactivate'}`);
  }
}

@Injectable({ providedIn: 'root' })
export class PurchaseOrdersApi extends ApiBase {
  list(query: PageQuery & { warehouseId: string; status?: PurchaseOrderStatus; openOnly?: boolean; supplierId?: string; productId?: string }): Promise<PagedResult<PurchaseOrderListItemDto>> {
    return this.get('/purchase-orders', toHttpParams(query));
  }
  getOne(id: string): Promise<PurchaseOrderDto> {
    return this.get(`/purchase-orders/${id}`);
  }
  create(warehouseId: string, body: PurchaseOrderInput): Promise<PurchaseOrderDto> {
    return this.post('/purchase-orders', { warehouseId, ...body });
  }
  /** Draft: everything. Placed: dates, reference and notes (`lines: null`). */
  update(id: string, body: PurchaseOrderInput & { orderDate: string; version: number }): Promise<PurchaseOrderDto> {
    return this.put(`/purchase-orders/${id}`, body);
  }
  place(id: string): Promise<PurchaseOrderDto> {
    return this.post(`/purchase-orders/${id}/place`);
  }
  cancel(id: string, reason: string | null): Promise<PurchaseOrderDto> {
    return this.post(`/purchase-orders/${id}/cancel`, { reason });
  }
  close(id: string, reason: string): Promise<PurchaseOrderDto> {
    return this.post(`/purchase-orders/${id}/close`, { reason });
  }
  /** A draft goods receipt for everything still outstanding. */
  createReceipt(id: string): Promise<ReceiptDto> {
    return this.post(`/purchase-orders/${id}/receipts`);
  }
}

@Injectable({ providedIn: 'root' })
export class PurchasingApi extends ApiBase {
  suggestions(warehouseId: string): Promise<ReorderSuggestionDto[]> {
    return this.get('/purchasing/suggestions', new HttpParams().set('warehouseId', warehouseId));
  }
  settings(): Promise<PurchasingSettingsDto> {
    return this.get('/purchasing/settings');
  }
  updateSettings(lowStockAlertEmails: string | null): Promise<PurchasingSettingsDto> {
    return this.put('/purchasing/settings', { lowStockAlertEmails });
  }
}

@Injectable({ providedIn: 'root' })
export class AdjustmentsApi extends ApiBase {
  list(query: PageQuery & { warehouseId: string; status?: AdjustmentStatus }): Promise<PagedResult<AdjustmentListItemDto>> {
    return this.get('/adjustments', toHttpParams(query));
  }
  getOne(id: string): Promise<AdjustmentDto> {
    return this.get(`/adjustments/${id}`);
  }
  create(body: { warehouseId: string; reason: AdjustmentReason; notes: string | null; lines: AdjustmentLineInput[] }): Promise<AdjustmentDto> {
    return this.post('/adjustments', body);
  }
  update(id: string, body: { reason: AdjustmentReason; notes: string | null; lines: AdjustmentLineInput[]; version: number }): Promise<AdjustmentDto> {
    return this.put(`/adjustments/${id}`, body);
  }
  submit(id: string): Promise<AdjustmentDto> {
    return this.post(`/adjustments/${id}/submit`);
  }
  approve(id: string, note: string | null, { key }: Key = {}): Promise<AdjustmentDto> {
    return firstValueFrom(this.http.post<AdjustmentDto>(`${this.base}/adjustments/${id}/approve`, { note }, { context: idempotent(key) }));
  }
  reject(id: string, reason: string): Promise<AdjustmentDto> {
    return this.post(`/adjustments/${id}/reject`, { reason });
  }
  reopen(id: string): Promise<AdjustmentDto> {
    return this.post(`/adjustments/${id}/reopen`);
  }
  cancel(id: string): Promise<AdjustmentDto> {
    return this.post(`/adjustments/${id}/cancel`);
  }
}

@Injectable({ providedIn: 'root' })
export class TransfersApi extends ApiBase {
  list(query: PageQuery & { warehouseId: string }): Promise<PagedResult<TransferListItemDto>> {
    return this.get('/transfers', toHttpParams(query));
  }
  getOne(id: string): Promise<TransferDto> {
    return this.get(`/transfers/${id}`);
  }
  createBinMove(body: { warehouseId: string; notes: string | null; lines: MoveLineInput[] }, { key }: Key = {}): Promise<TransferDto> {
    return firstValueFrom(this.http.post<TransferDto>(`${this.base}/transfers/bin-moves`, body, { context: idempotent(key) }));
  }
}

@Injectable({ providedIn: 'root' })
export class DispatchesApi extends ApiBase {
  list(query: PageQuery & { warehouseId: string; status?: DispatchStatus; open?: boolean; customerId?: string; assignedToMe?: boolean; from?: string; to?: string }): Promise<PagedResult<DispatchListItemDto>> {
    return this.get('/dispatches', toHttpParams(query));
  }
  getOne(id: string): Promise<DispatchDto> {
    return this.get(`/dispatches/${id}`);
  }
  create(warehouseId: string, body: DispatchInput): Promise<DispatchDto> {
    return this.post('/dispatches', { warehouseId, ...body });
  }
  update(id: string, body: DispatchInput & { version: number }): Promise<DispatchDto> {
    return this.put(`/dispatches/${id}`, body);
  }
  submit(id: string): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/submit`);
  }
  returnToDraft(id: string, reason: string): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/return`, { reason });
  }
  approve(id: string, note: string | null, { key }: Key = {}): Promise<DispatchDto> {
    return firstValueFrom(this.http.post<DispatchDto>(`${this.base}/dispatches/${id}/approve`, { note }, { context: idempotent(key) }));
  }
  startPicking(id: string, assignedToId: string | null): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/picking`, { assignedToId });
  }
  assign(id: string, assignedToId: string | null): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/assign`, { assignedToId });
  }
  pickList(id: string): Promise<PickListDto> {
    return this.get(`/dispatches/${id}/pick-list`);
  }
  pick(id: string, body: PickInput): Promise<PickResultDto> {
    return this.post(`/dispatches/${id}/picks`, body);
  }
  undoPick(id: string, pickId: string, scanId: string): Promise<PickResultDto> {
    return this.post(`/dispatches/${id}/picks/${pickId}/undo`, { scanId });
  }
  reallocate(id: string, body: { reservationId: string; location: string; lotNo: string | null; quantity: number }): Promise<PickListDto> {
    return this.post(`/dispatches/${id}/reallocate`, body);
  }
  reportDiscrepancy(id: string, body: { type: DiscrepancyType; lineId?: string | null; location?: string | null; barcode?: string | null; quantity?: number | null; note?: string | null }): Promise<DiscrepancyDto> {
    return this.post(`/dispatches/${id}/discrepancies`, body);
  }
  ready(id: string, allowShort: boolean): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/ready`, { allowShort });
  }
  reopenPicking(id: string, note: string | null): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/reopen-picking`, { note });
  }
  ship(id: string, body: { carrier: string | null; trackingNumber: string | null; shortDecision: ShortShipDecision | null }, { key }: Key = {}): Promise<DispatchDto> {
    return firstValueFrom(this.http.post<DispatchDto>(`${this.base}/dispatches/${id}/ship`, body, { context: idempotent(key) }));
  }
  /** Proof of delivery: receiver name, signature image and photos (multipart). */
  deliver(id: string, body: { receiverName: string; deliveredAt?: string | null; notes?: string | null; signature: Blob; photos: File[] }): Promise<DispatchDto> {
    const form = new FormData();
    form.append('receiverName', body.receiverName);
    if (body.deliveredAt) form.append('deliveredAt', body.deliveredAt);
    if (body.notes) form.append('notes', body.notes);
    form.append('signature', body.signature, 'signature.png');
    for (const photo of body.photos) form.append('photos', photo, photo.name);
    return firstValueFrom(this.http.post<DispatchDto>(`${this.base}/dispatches/${id}/deliver`, form));
  }
  cancel(id: string, reason: string, { key }: Key = {}): Promise<DispatchDto> {
    return firstValueFrom(this.http.post<DispatchDto>(`${this.base}/dispatches/${id}/cancel`, { reason }, { context: idempotent(key) }));
  }
  document(id: string, documentId: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/dispatches/${id}/documents/${documentId}`, { responseType: 'blob' }));
  }
  regenerate(id: string, type: DocumentType): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/documents/${type}`);
  }
  sendDeliveryNote(id: string, to: string | null): Promise<DispatchDto> {
    return this.post(`/dispatches/${id}/delivery-note/send`, { to });
  }
  podFile(id: string, fileId: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/dispatches/${id}/pod/files/${fileId}`, { responseType: 'blob' }));
  }
}

@Injectable({ providedIn: 'root' })
export class SettingsApi extends ApiBase {
  company(): Promise<CompanyProfileDto> {
    return this.get('/settings/company');
  }
  updateCompany(body: Omit<CompanyProfileDto, 'hasLogo'>): Promise<CompanyProfileDto> {
    return this.put('/settings/company', body);
  }
  logo(): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/settings/company/logo`, { responseType: 'blob' }));
  }
  setLogo(file: File): Promise<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<void>(`${this.base}/settings/company/logo`, form));
  }
  removeLogo(): Promise<void> {
    return this.delete('/settings/company/logo');
  }
  templates(): Promise<TemplateDto[]> {
    return this.get('/document-templates');
  }
  template(type: DocumentType, version?: number): Promise<TemplateDto> {
    return this.get(`/document-templates/${type}`, version ? new HttpParams().set('version', version) : undefined);
  }
  saveTemplate(type: DocumentType, html: string, comment: string | null): Promise<TemplateDto> {
    return this.put(`/document-templates/${type}`, { html, comment });
  }
  restoreTemplate(type: DocumentType, version: number): Promise<TemplateDto> {
    return this.post(`/document-templates/${type}/versions/${version}/restore`);
  }
  preview(type: DocumentType, html: string | null, dispatchId?: string | null): Promise<Blob> {
    return firstValueFrom(this.http.post(`${this.base}/document-templates/${type}/preview`, { html, dispatchId: dispatchId ?? null }, { responseType: 'blob' }));
  }
}

@Injectable({ providedIn: 'root' })
export class ReportingApi extends ApiBase {
  dashboard(warehouseId: string): Promise<WarehouseDashboardDto> {
    return this.get('/dashboard/warehouse', toHttpParams({ warehouseId }));
  }
  reports(): Promise<ReportInfoDto[]> {
    return this.get('/reports');
  }
  report(kind: string, query: { warehouseId: string; from?: string; to?: string; status?: DispatchStatus }): Promise<ReportResult> {
    return this.get(`/reports/${kind}`, toHttpParams(query));
  }
  reportCsv(kind: string, query: { warehouseId: string; from?: string; to?: string; status?: DispatchStatus }): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/reports/${kind}/csv`, { params: toHttpParams(query), responseType: 'blob' }));
  }
  audit(query: PageQuery & { action?: string; entityType?: string; entityId?: string; warehouseId?: string; from?: string; to?: string }): Promise<PagedResult<AuditEntryDto>> {
    return this.get('/audit-logs', toHttpParams(query));
  }
}
