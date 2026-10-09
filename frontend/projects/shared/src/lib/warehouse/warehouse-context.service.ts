import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { KEY_VALUE_STORE } from '../core/storage';
import { WarehouseSummary } from '../models/auth';

const STORAGE_KEY = 'wh.selectedWarehouseId';

/**
 * The warehouse the user is currently working in. Purely a UI selection:
 * the API re-checks warehouse access on every request.
 */
@Injectable({ providedIn: 'root' })
export class WarehouseContextService {
  private readonly auth = inject(AuthService);
  private readonly store = inject(KEY_VALUE_STORE);
  private readonly selectedId = signal<string | null>(this.store.get(STORAGE_KEY));

  /** Warehouses the signed-in user may work in. */
  readonly available = computed<WarehouseSummary[]>(() => this.auth.user()?.warehouses ?? []);

  /**
   * The warehouse in use: the stored selection if still allowed, otherwise the only
   * available warehouse (the initial single-branch deployment), otherwise null.
   */
  readonly current = computed<WarehouseSummary | null>(() => {
    const list = this.available();
    const id = this.selectedId();
    return list.find((w) => w.id === id) ?? (list.length === 1 ? (list[0] ?? null) : null);
  });

  /** True when the user has a choice to make. */
  readonly needsSelection = computed(() => this.current() === null && this.available().length > 1);

  select(warehouseId: string): boolean {
    if (!this.available().some((w) => w.id === warehouseId)) return false;
    this.selectedId.set(warehouseId);
    this.store.set(STORAGE_KEY, warehouseId);
    return true;
  }

  clear(): void {
    this.selectedId.set(null);
    this.store.remove(STORAGE_KEY);
  }
}
