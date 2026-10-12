import { Component } from '@angular/core';

/** Shown on warehouse-scoped pages when no warehouse is selected or assigned. */
@Component({
  selector: 'wh-need-warehouse',
  template: `
    <div class="panel need-warehouse" role="status">
      <i class="pi pi-warehouse" aria-hidden="true"></i>
      <div>
        <strong>Choose a warehouse</strong>
        <p>Select the warehouse you work in from the top bar. If none is listed, ask an administrator to grant you access.</p>
      </div>
    </div>
  `,
  styles: `
    .need-warehouse { display: flex; gap: 1rem; align-items: flex-start; max-width: 40rem; }
    .need-warehouse i { font-size: 1.5rem; color: var(--wh-muted); margin-top: 0.125rem; }
    p { margin: 0.25rem 0 0; color: var(--wh-muted); }
  `,
})
export class NeedWarehouse {}
