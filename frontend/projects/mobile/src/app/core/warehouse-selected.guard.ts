import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { AuthService, WarehouseContextService } from '@wh/shared';

/**
 * Operational pages need a selected warehouse.
 * Angular starts canMatch guards together, so wait for the session to be restored first
 * (otherwise a page reload would see "no user, no warehouse" and redirect wrongly).
 */
export const warehouseSelectedGuard: CanMatchFn = async () => {
  const auth = inject(AuthService);
  const ctx = inject(WarehouseContextService);
  const router = inject(Router);
  if (!(await auth.restoreSession())) {
    return router.createUrlTree(['/login']);
  }
  return ctx.current() ? true : router.createUrlTree(['/select-warehouse']);
};
