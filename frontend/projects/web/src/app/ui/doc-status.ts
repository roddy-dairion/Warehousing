import { Component, computed, input } from '@angular/core';
import { Tag } from 'primeng/tag';
import { AdjustmentStatus, dispatchStatusInfo, DispatchStatus, PurchaseOrderStatus, ReceiptStatus } from '@wh/shared';

type Severity = 'secondary' | 'info' | 'warn' | 'success' | 'danger' | 'contrast';

const RECEIPT: Record<ReceiptStatus, [string, Severity]> = {
  draft: ['Draft', 'secondary'],
  posted: ['Posted', 'success'],
  reversed: ['Reversed', 'danger'],
  cancelled: ['Cancelled', 'secondary'],
};

const ADJUSTMENT: Record<AdjustmentStatus, [string, Severity]> = {
  draft: ['Draft', 'secondary'],
  pendingApproval: ['Awaiting approval', 'warn'],
  posted: ['Posted', 'success'],
  rejected: ['Rejected', 'danger'],
  cancelled: ['Cancelled', 'secondary'],
};

const PURCHASE_ORDER: Record<PurchaseOrderStatus, [string, Severity]> = {
  draft: ['Draft', 'secondary'],
  ordered: ['Ordered', 'info'],
  partiallyReceived: ['Partially received', 'warn'],
  received: ['Received', 'success'],
  closed: ['Closed', 'contrast'],
  cancelled: ['Cancelled', 'secondary'],
};

export const PURCHASE_ORDER_STATUSES = (Object.keys(PURCHASE_ORDER) as PurchaseOrderStatus[]).map((value) => ({ value, label: PURCHASE_ORDER[value][0] }));

/** Status tag for receipts, adjustments, dispatches and purchase orders. */
@Component({
  selector: 'wh-doc-status',
  imports: [Tag],
  template: `<p-tag [value]="info()[0]" [severity]="info()[1]" />`,
})
export class DocStatus {
  readonly kind = input.required<'receipt' | 'adjustment' | 'dispatch' | 'purchaseOrder'>();
  readonly status = input.required<string>();

  protected readonly info = computed<[string, Severity]>(() => {
    const s = this.status();
    switch (this.kind()) {
      case 'receipt':
        return RECEIPT[s as ReceiptStatus] ?? [s, 'secondary'];
      case 'adjustment':
        return ADJUSTMENT[s as AdjustmentStatus] ?? [s, 'secondary'];
      case 'purchaseOrder':
        return PURCHASE_ORDER[s as PurchaseOrderStatus] ?? [s, 'secondary'];
      default: {
        const d = dispatchStatusInfo(s as DispatchStatus);
        return [d.label, d.severity];
      }
    }
  });
}
