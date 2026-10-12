/** DTOs for inventory, dispatch, documents and reporting, mirrored from the API (Inventory.Application). */

import { LocationType } from './master-data';

// ---- Inventory --------------------------------------------------------------------------------

export type LotStatus = 'released' | 'quarantine' | 'blocked';

export type MovementType =
  | 'receipt' | 'receiptReversal' | 'adjustmentIn' | 'adjustmentOut' | 'moveOut' | 'moveIn' | 'transferOut' | 'transferIn' | 'dispatch' | 'returnIn';

export const MOVEMENT_TYPE_LABELS: Record<MovementType, string> = {
  receipt: 'Receipt',
  receiptReversal: 'Receipt reversed',
  adjustmentIn: 'Adjustment +',
  adjustmentOut: 'Adjustment −',
  moveOut: 'Moved out',
  moveIn: 'Moved in',
  transferOut: 'Transfer out',
  transferIn: 'Transfer in',
  dispatch: 'Dispatched',
  returnIn: 'Return',
};

export interface BalanceDto {
  id: string;
  warehouseId: string;
  productId: string;
  sku: string;
  productName: string;
  baseUnitCode: string;
  locationId: string;
  locationCode: string;
  locationType: LocationType;
  isPickable: boolean;
  lotId: string | null;
  lotNo: string | null;
  expiryDate: string | null;
  lotStatus: LotStatus | null;
  onHand: number;
  reserved: number;
  available: number;
  lastMovementAt: string | null;
}

export interface StockSummaryDto {
  productId: string;
  sku: string;
  productName: string;
  categoryName: string | null;
  baseUnitCode: string;
  isActive: boolean;
  onHand: number;
  reserved: number;
  available: number;
  /** Available stock in pickable bins and usable lots. */
  sellable: number;
  minStockQty: number | null;
  isLow: boolean;
}

export interface ProductStockWarehouseDto {
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  onHand: number;
  reserved: number;
  available: number;
  balances: BalanceDto[];
}

export interface ProductStockDto {
  productId: string;
  sku: string;
  productName: string;
  baseUnitCode: string;
  isActive: boolean;
  isLotTracked: boolean;
  hasExpiry: boolean;
  minStockQty: number | null;
  onHand: number;
  reserved: number;
  available: number;
  warehouses: ProductStockWarehouseDto[];
}

export interface MovementDto {
  id: string;
  occurredAt: string;
  type: MovementType;
  productId: string;
  sku: string;
  productName: string;
  baseUnitCode: string;
  locationId: string;
  locationCode: string;
  lotId: string | null;
  lotNo: string | null;
  quantity: number;
  onHandAfter: number;
  sourceType: string;
  sourceId: string;
  sourceNumber: string | null;
  reason: string | null;
  createdByName: string | null;
}

export interface ExpiringLotDto {
  lotId: string;
  lotNo: string;
  expiryDate: string;
  daysLeft: number;
  status: LotStatus;
  productId: string;
  sku: string;
  productName: string;
  baseUnitCode: string;
  onHand: number;
  reserved: number;
  locations: string[];
}

export interface LotDto {
  id: string;
  productId: string;
  lotNo: string;
  expiryDate: string | null;
  status: LotStatus;
  onHand: number;
  reserved: number;
  createdAt: string;
  version: number;
}

export interface ReconciliationIssue {
  balanceId: string;
  warehouseId: string;
  sku: string;
  locationCode: string;
  lotNo: string | null;
  onHand: number;
  ledgerOnHand: number;
  reserved: number;
  openReservations: number;
}

export interface ReconciliationResult {
  checkedAt: string;
  balancesChecked: number;
  issues: ReconciliationIssue[];
}

// ---- Receipts ----------------------------------------------------------------------------------

export type ReceiptStatus = 'draft' | 'posted' | 'reversed' | 'cancelled';

export interface ReceiptListItemDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseCode: string;
  receiptDate: string;
  supplierName: string | null;
  supplierReference: string | null;
  status: ReceiptStatus;
  lineCount: number;
  createdAt: string;
  postedAt: string | null;
}

export interface ReceiptLineDto {
  id: string;
  lineNo: number;
  productId: string;
  sku: string;
  productName: string;
  productUnitId: string | null;
  unitCode: string;
  enteredQuantity: number;
  quantity: number;
  baseUnitCode: string;
  expectedQuantity: number | null;
  locationId: string;
  locationCode: string;
  lotNo: string | null;
  expiryDate: string | null;
  notes: string | null;
}

export interface ReceiptDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseCode: string;
  receiptDate: string;
  supplierName: string | null;
  supplierReference: string | null;
  notes: string | null;
  status: ReceiptStatus;
  lines: ReceiptLineDto[];
  createdAt: string;
  createdByName: string | null;
  postedAt: string | null;
  postedByName: string | null;
  reversedAt: string | null;
  reversedByName: string | null;
  reversalReason: string | null;
  version: number;
}

export interface ReceiptLineInput {
  productId: string;
  productUnitId: string | null;
  quantity: number;
  expectedQuantity: number | null;
  locationId: string | null;
  lotNo: string | null;
  expiryDate: string | null;
  notes: string | null;
}

export interface ReceiptInput {
  receiptDate: string | null;
  supplierName: string | null;
  supplierReference: string | null;
  notes: string | null;
  lines: ReceiptLineInput[];
}

// ---- Adjustments -------------------------------------------------------------------------------

export type AdjustmentReason = 'countCorrection' | 'damage' | 'loss' | 'found' | 'expired' | 'other';
export type AdjustmentStatus = 'draft' | 'pendingApproval' | 'posted' | 'rejected' | 'cancelled';

export const ADJUSTMENT_REASONS: { value: AdjustmentReason; label: string }[] = [
  { value: 'countCorrection', label: 'Count correction' },
  { value: 'damage', label: 'Damage' },
  { value: 'loss', label: 'Loss' },
  { value: 'found', label: 'Found' },
  { value: 'expired', label: 'Expired' },
  { value: 'other', label: 'Other' },
];

export interface AdjustmentListItemDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseCode: string;
  reason: AdjustmentReason;
  status: AdjustmentStatus;
  lineCount: number;
  createdAt: string;
  submittedAt: string | null;
  decidedAt: string | null;
}

export interface AdjustmentLineDto {
  id: string;
  lineNo: number;
  productId: string;
  sku: string;
  productName: string;
  baseUnitCode: string;
  locationId: string;
  locationCode: string;
  lotNo: string | null;
  expiryDate: string | null;
  quantityDelta: number;
  currentOnHand: number | null;
  notes: string | null;
}

export interface AdjustmentDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseCode: string;
  reason: AdjustmentReason;
  notes: string | null;
  status: AdjustmentStatus;
  lines: AdjustmentLineDto[];
  createdAt: string;
  createdByName: string | null;
  submittedAt: string | null;
  submittedByName: string | null;
  decidedAt: string | null;
  decidedByName: string | null;
  decisionNote: string | null;
  canDecide: boolean;
  version: number;
}

export interface AdjustmentLineInput {
  productId: string;
  locationId: string;
  lotNo: string | null;
  expiryDate: string | null;
  quantityDelta: number;
  notes: string | null;
}

// ---- Transfers ---------------------------------------------------------------------------------

export interface TransferListItemDto {
  id: string;
  number: string;
  type: 'binMove' | 'interWarehouse';
  warehouseCode: string;
  lineCount: number;
  postedAt: string;
  notes: string | null;
}

export interface TransferLineDto {
  lineNo: number;
  productId: string;
  sku: string;
  productName: string;
  lotNo: string | null;
  fromLocationId: string;
  fromLocationCode: string;
  toLocationId: string;
  toLocationCode: string;
  quantity: number;
  baseUnitCode: string;
}

export interface TransferDto {
  id: string;
  number: string;
  type: 'binMove' | 'interWarehouse';
  warehouseId: string;
  warehouseCode: string;
  notes: string | null;
  postedAt: string;
  postedByName: string | null;
  lines: TransferLineDto[];
}

export interface MoveLineInput {
  productId: string;
  lotNo: string | null;
  fromLocationId: string;
  toLocationId: string;
  quantity: number;
  productUnitId: string | null;
}

// ---- Dispatches --------------------------------------------------------------------------------

export type DispatchStatus = 'draft' | 'submitted' | 'approved' | 'picking' | 'readyForDispatch' | 'dispatched' | 'delivered' | 'cancelled';

export const DISPATCH_STATUSES: { value: DispatchStatus; label: string; severity: 'secondary' | 'info' | 'warn' | 'success' | 'danger' | 'contrast' }[] = [
  { value: 'draft', label: 'Draft', severity: 'secondary' },
  { value: 'submitted', label: 'Awaiting approval', severity: 'warn' },
  { value: 'approved', label: 'Approved', severity: 'info' },
  { value: 'picking', label: 'Picking', severity: 'info' },
  { value: 'readyForDispatch', label: 'Ready to ship', severity: 'contrast' },
  { value: 'dispatched', label: 'Dispatched', severity: 'success' },
  { value: 'delivered', label: 'Delivered', severity: 'success' },
  { value: 'cancelled', label: 'Cancelled', severity: 'danger' },
];

export type ShortShipDecision = 'closeShort' | 'backorder';
export type DiscrepancyType = 'missing' | 'damaged' | 'unexpected' | 'wrongLot' | 'other';

export const DISCREPANCY_TYPES: { value: DiscrepancyType; label: string }[] = [
  { value: 'missing', label: 'Missing' },
  { value: 'damaged', label: 'Damaged' },
  { value: 'unexpected', label: 'Unexpected product' },
  { value: 'wrongLot', label: 'Wrong lot' },
  { value: 'other', label: 'Other' },
];

export type DocumentType = 'packingSlip' | 'deliveryNote';
export type DocumentStatus = 'pending' | 'generated' | 'failed';
export type EmailStatus = 'pending' | 'sent' | 'failed';

export interface DispatchListItemDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseCode: string;
  customerId: string;
  customerReference: string;
  customerName: string;
  customerOrderReference: string | null;
  requestedDate: string;
  status: DispatchStatus;
  lineCount: number;
  quantityRequested: number;
  quantityShipped: number;
  backorderOfNumber: string | null;
  assignedToName: string | null;
  createdAt: string;
  shippedAt: string | null;
  deliveredAt: string | null;
}

export interface DispatchLineDto {
  id: string;
  lineNo: number;
  productId: string;
  sku: string;
  productName: string;
  isLotTracked: boolean;
  baseUnitCode: string;
  productUnitId: string | null;
  unitCode: string;
  enteredQuantity: number;
  quantity: number;
  quantityReserved: number;
  quantityPicked: number;
  quantityShipped: number;
  outstanding: number;
  notes: string | null;
}

export interface AddressSnapshotDto {
  label: string;
  line1: string;
  line2: string | null;
  city: string | null;
  country: string | null;
  contactName: string | null;
  phone: string | null;
}

export interface StatusHistoryDto {
  from: DispatchStatus | null;
  to: DispatchStatus;
  at: string;
  userName: string | null;
  note: string | null;
}

export interface DiscrepancyDto {
  id: string;
  type: DiscrepancyType;
  lineId: string | null;
  sku: string | null;
  locationCode: string | null;
  quantity: number | null;
  barcode: string | null;
  note: string | null;
  at: string;
  userName: string | null;
}

export interface DispatchDocumentDto {
  id: string;
  type: DocumentType;
  status: DocumentStatus;
  fileName: string | null;
  templateVersion: number | null;
  requestedAt: string;
  generatedAt: string | null;
  lastError: string | null;
}

export interface DispatchEmailDto {
  id: string;
  to: string;
  subject: string;
  status: EmailStatus;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  sentAt: string | null;
}

export interface ProofOfDeliveryDto {
  receiverName: string;
  deliveredAt: string;
  notes: string | null;
  signatureFileId: string;
  photoFileIds: string[];
  recordedAt: string;
  recordedByName: string | null;
}

export interface DispatchActionsDto {
  canEdit: boolean;
  canSubmit: boolean;
  canReturn: boolean;
  canApprove: boolean;
  canStartPicking: boolean;
  canPick: boolean;
  canCompletePicking: boolean;
  canReopenPicking: boolean;
  canShip: boolean;
  canDeliver: boolean;
  canCancel: boolean;
}

export interface BackorderRefDto {
  id: string;
  number: string;
  status: DispatchStatus;
}

export interface DispatchDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseCode: string;
  customerId: string;
  customerReference: string;
  customerName: string;
  customerEmail: string | null;
  deliveryAddressId: string | null;
  shipTo: AddressSnapshotDto | null;
  customerOrderReference: string | null;
  requestedDate: string;
  notes: string | null;
  status: DispatchStatus;
  backorderOfId: string | null;
  backorderOfNumber: string | null;
  backorders: BackorderRefDto[];
  assignedToId: string | null;
  assignedToName: string | null;
  lines: DispatchLineDto[];
  history: StatusHistoryDto[];
  discrepancies: DiscrepancyDto[];
  documents: DispatchDocumentDto[];
  emails: DispatchEmailDto[];
  proofOfDelivery: ProofOfDeliveryDto | null;
  createdByName: string | null;
  createdAt: string;
  submittedByName: string | null;
  submittedAt: string | null;
  approvedByName: string | null;
  approvedAt: string | null;
  shippedByName: string | null;
  shippedAt: string | null;
  carrier: string | null;
  trackingNumber: string | null;
  shortDecision: ShortShipDecision | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
  cancelledByName: string | null;
  cancelReason: string | null;
  actions: DispatchActionsDto;
  version: number;
}

export interface DispatchLineInput {
  productId: string;
  productUnitId: string | null;
  quantity: number;
  notes: string | null;
}

export interface DispatchInput {
  customerId: string;
  deliveryAddressId: string | null;
  requestedDate: string;
  customerOrderReference: string | null;
  notes: string | null;
  lines: DispatchLineInput[];
}

export interface PickTaskDto {
  reservationId: string;
  lineId: string;
  lineNo: number;
  productId: string;
  sku: string;
  productName: string;
  baseUnitCode: string;
  locationId: string;
  locationCode: string;
  locationBarcode: string | null;
  lotId: string | null;
  lotNo: string | null;
  expiryDate: string | null;
  quantity: number;
  picked: number;
  remaining: number;
}

export interface PickListDto {
  dispatchId: string;
  number: string;
  status: DispatchStatus;
  assignedToName: string | null;
  tasks: PickTaskDto[];
  lines: DispatchLineDto[];
}

export interface PickInput {
  scanId: string;
  location: string;
  barcode?: string | null;
  productId?: string | null;
  productUnitId?: string | null;
  lotNo?: string | null;
  quantity?: number | null;
}

export interface PickResultDto {
  pickId: string;
  replayed: boolean;
  lineId: string;
  lineNo: number;
  sku: string;
  locationCode: string;
  lotNo: string | null;
  quantity: number;
  linePicked: number;
  lineReserved: number;
  taskRemaining: number;
  status: DispatchStatus;
}

/** Body of a 409 stock.shortage problem. */
export interface ReservationShortage {
  lineId: string;
  productId: string;
  sku: string;
  requested: number;
  available: number;
}

// ---- Settings and templates ---------------------------------------------------------------------

export interface TemplateVersionDto {
  id: string;
  type: DocumentType;
  version: number;
  isCurrent: boolean;
  comment: string | null;
  createdAt: string;
  createdByName: string | null;
}

export interface TemplateDto {
  type: DocumentType;
  version: number;
  html: string;
  comment: string | null;
  createdAt: string;
  createdByName: string | null;
  versions: TemplateVersionDto[];
  placeholders: string[];
}

export interface CompanyProfileDto {
  name: string;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  country: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  registrationNumber: string | null;
  vatNumber: string | null;
  footerText: string | null;
  hasLogo: boolean;
  timeZone: string;
  deliveryEmailSubject: string;
  deliveryEmailBody: string;
  deliveryEmailBcc: string | null;
  version: number;
}

// ---- Dashboard, reports, audit ------------------------------------------------------------------

export interface WarehouseDashboardDto {
  warehouseId: string;
  stock: {
    productsInStock: number;
    onHand: number;
    reserved: number;
    available: number;
    lowStockProducts: number;
    expiringLots: number;
    expiredLots: number;
  } | null;
  pendingReceipts: number | null;
  adjustmentsAwaitingApproval: number | null;
  dispatches: {
    draft: number;
    awaitingApproval: number;
    toPick: number;
    picking: number;
    readyToShip: number;
    awaitingDelivery: number;
    deliveredToday: number;
  } | null;
  dispatchesToAction: { id: string; number: string; customerName: string; requestedDate: string; status: DispatchStatus }[] | null;
  recentMovements: { occurredAt: string; type: MovementType; sku: string; productName: string; locationCode: string; lotNo: string | null; quantity: number; sourceNumber: string | null }[] | null;
  failedDocuments: number | null;
  failedEmails: number | null;
}

export interface ReportInfoDto {
  kind: string;
  title: string;
  usesDates: boolean;
}

export interface ReportResult {
  kind: string;
  title: string;
  columns: string[];
  rows: (string | null)[][];
  truncated: boolean;
}

export interface AuditEntryDto {
  id: number;
  occurredAt: string;
  action: string;
  entityType: string | null;
  entityId: string | null;
  warehouseId: string | null;
  warehouseCode: string | null;
  userId: string | null;
  userName: string | null;
  data: string | null;
  ipAddress: string | null;
}

/** Labels for status values shown in tags. */
export function dispatchStatusInfo(status: DispatchStatus): (typeof DISPATCH_STATUSES)[number] {
  return DISPATCH_STATUSES.find((s) => s.value === status) ?? DISPATCH_STATUSES[0]!;
}
