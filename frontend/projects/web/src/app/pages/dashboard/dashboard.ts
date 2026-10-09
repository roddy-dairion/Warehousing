import { Component, inject } from '@angular/core';
import { AuthService, WarehouseContextService } from '@wh/shared';

@Component({
  selector: 'wh-dashboard',
  template: `
    <h1>Good to see you, {{ firstName() }}</h1>
    @if (warehouses.current(); as wh) {
      <p class="lead">You are working in <strong>{{ wh.name }}</strong> ({{ wh.code }}).</p>
    } @else if (warehouses.needsSelection()) {
      <p class="lead">Choose a warehouse in the top bar to see its stock and dispatches.</p>
    } @else {
      <p class="lead">No warehouse is assigned to your account yet. Ask an administrator to grant access.</p>
    }
    <p class="muted">Stock levels, pending receipts and dispatches ready to ship will appear here once the inventory and dispatch modules are built.</p>
  `,
  styles: `
    h1 { margin: 0 0 0.5rem; font-size: 1.5rem; font-weight: 600; }
    .lead { margin: 0 0 1rem; }
    .muted { color: var(--wh-muted); max-width: 60ch; }
  `,
})
export class DashboardPage {
  private readonly auth = inject(AuthService);
  protected readonly warehouses = inject(WarehouseContextService);
  protected firstName(): string {
    return this.auth.user()?.displayName.split(' ')[0] ?? '';
  }
}
