import { inject, Injectable } from '@angular/core';
import { LocationDto, ProductDto, ProductsApi, WarehousesApi } from '@wh/shared';

/**
 * Small caches for pickers: active bins per warehouse and full product records (units, lot tracking).
 * Cleared on reload; data is re-validated by the API on every save anyway.
 */
@Injectable({ providedIn: 'root' })
export class Lookups {
  private readonly warehouses = inject(WarehousesApi);
  private readonly products = inject(ProductsApi);
  private readonly bins = new Map<string, Promise<LocationDto[]>>();
  private readonly productCache = new Map<string, Promise<ProductDto>>();

  /** All active bins of a warehouse, ordered by code. */
  binsOf(warehouseId: string): Promise<LocationDto[]> {
    let p = this.bins.get(warehouseId);
    if (!p) {
      p = this.loadBins(warehouseId);
      this.bins.set(warehouseId, p);
      p.catch(() => this.bins.delete(warehouseId));
    }
    return p;
  }

  product(id: string): Promise<ProductDto> {
    let p = this.productCache.get(id);
    if (!p) {
      p = this.products.getOne(id);
      this.productCache.set(id, p);
      p.catch(() => this.productCache.delete(id));
    }
    return p;
  }

  invalidateBins(warehouseId?: string): void {
    if (warehouseId) this.bins.delete(warehouseId);
    else this.bins.clear();
  }

  private async loadBins(warehouseId: string): Promise<LocationDto[]> {
    const all: LocationDto[] = [];
    for (let page = 1; page <= 50; page++) {
      const result = await this.warehouses.locations(warehouseId, { page, pageSize: 200, isActive: true, sortBy: 'code' });
      all.push(...result.items);
      if (all.length >= result.totalCount || result.items.length === 0) break;
    }
    return all;
  }
}
