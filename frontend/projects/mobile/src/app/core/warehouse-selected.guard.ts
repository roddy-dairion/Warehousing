import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { WarehouseContextService } from '@wh/shared';

/** Operational pages need a selected warehouse. */
export const warehouseSelectedGuard: CanMatchFn = () => {
  const ctx = inject(WarehouseContextService);
  return ctx.current() ? true : inject(Router).createUrlTree(['/select-warehouse']);
};
