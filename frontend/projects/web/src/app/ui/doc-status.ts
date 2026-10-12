import { Component, computed, input } from '@angular/core';
import { Tag } from 'primeng/tag';
import { AdjustmentStatus, dispatchStatusInfo, DispatchStatus, ReceiptStatus } from '@wh/shared';

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

/** Status tag for receipts, adjustments and dispatches. */
@Component({
  selector: 'wh-doc-status',
  imports: [Tag],
  template: `<p-tag [value]="info()[0]" [severity]="info()[1]" />`,
})
export class DocStatus {
  readonly kind = input.required<'receipt' | 'adjustment' | 'dispatch'>();
  readonly status = input.required<string>();

  protected readonly info = computed<[string, Severity]>(() => {
    const s = this.status();
    switch (this.kind()) {
      case 'receipt':
        return RECEIPT[s as ReceiptStatus] ?? [s, 'secondary'];
      case 'adjustment':
        return ADJUSTMENT[s as AdjustmentStatus] ?? [s, 'secondary'];
      default: {
        const d = dispatchStatusInfo(s as DispatchStatus);
        return [d.label, d.severity];
      }
    }
  });
}
