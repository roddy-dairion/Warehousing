import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toHttpParams } from '../http/query';
import { PagedResult, PageQuery } from '../models/paging';
import {
  AddressInput,
  BarcodeLookupDto,
  CategoryDto,
  CustomerDto,
  CustomerInput,
  CustomerListItemDto,
  ImportKind,
  ImportResultDto,
  LocationDto,
  LocationType,
  ProductDto,
  ProductInput,
  ProductListItemDto,
  RoleDto,
  UnitDto,
  UserDto,
  UserListItemDto,
  WarehouseContact,
  WarehouseDto,
} from '../models/master-data';
import { ApiBase } from './api-base';

@Injectable({ providedIn: 'root' })
export class WarehousesApi extends ApiBase {
  list(includeInactive = false): Promise<WarehouseDto[]> {
    return this.get('/warehouses', new HttpParams().set('includeInactive', includeInactive));
  }
  getOne(id: string): Promise<WarehouseDto> {
    return this.get(`/warehouses/${id}`);
  }
  create(body: { code: string; name: string } & WarehouseContact): Promise<WarehouseDto> {
    return this.post('/warehouses', body);
  }
  update(id: string, body: { name: string; version: number } & WarehouseContact): Promise<WarehouseDto> {
    return this.put(`/warehouses/${id}`, body);
  }
  setActive(id: string, active: boolean): Promise<void> {
    return this.post(`/warehouses/${id}/${active ? 'activate' : 'deactivate'}`);
  }
  locations(warehouseId: string, query: PageQuery & { type?: LocationType }): Promise<PagedResult<LocationDto>> {
    return this.get(`/warehouses/${warehouseId}/locations`, toHttpParams(query));
  }
  createLocation(warehouseId: string, body: { code: string; type: LocationType; zone?: string | null; barcode?: string | null; isPickable?: boolean | null }): Promise<LocationDto> {
    return this.post(`/warehouses/${warehouseId}/locations`, body);
  }
  updateLocation(id: string, body: { type: LocationType; zone?: string | null; barcode?: string | null; isPickable: boolean; version: number }): Promise<LocationDto> {
    return this.put(`/locations/${id}`, body);
  }
  setLocationActive(id: string, active: boolean): Promise<void> {
    return this.post(`/locations/${id}/${active ? 'activate' : 'deactivate'}`);
  }
}

@Injectable({ providedIn: 'root' })
export class CustomersApi extends ApiBase {
  list(query: PageQuery): Promise<PagedResult<CustomerListItemDto>> {
    return this.get('/customers', toHttpParams(query));
  }
  getOne(id: string): Promise<CustomerDto> {
    return this.get(`/customers/${id}`);
  }
  create(body: CustomerInput & { reference: string; addresses?: AddressInput[] }): Promise<CustomerDto> {
    return this.post('/customers', body);
  }
  update(id: string, body: CustomerInput & { version: number }): Promise<CustomerDto> {
    return this.put(`/customers/${id}`, body);
  }
  setActive(id: string, active: boolean): Promise<void> {
    return this.post(`/customers/${id}/${active ? 'activate' : 'deactivate'}`);
  }
  addAddress(id: string, address: AddressInput, version: number): Promise<CustomerDto> {
    return this.post(`/customers/${id}/addresses`, { address, version });
  }
  updateAddress(id: string, addressId: string, address: AddressInput, version: number): Promise<CustomerDto> {
    return this.put(`/customers/${id}/addresses/${addressId}`, { address, version });
  }
  setAddressActive(id: string, addressId: string, active: boolean, version: number): Promise<CustomerDto> {
    return this.post(`/customers/${id}/addresses/${addressId}/${active ? 'activate' : 'deactivate'}`, {}, this.version(version));
  }
}

@Injectable({ providedIn: 'root' })
export class CatalogApi extends ApiBase {
  categories(): Promise<CategoryDto[]> {
    return this.get('/categories');
  }
  createCategory(body: { code: string; name: string; parentId?: string | null }): Promise<CategoryDto> {
    return this.post('/categories', body);
  }
  updateCategory(id: string, body: { name: string; parentId?: string | null; version: number }): Promise<CategoryDto> {
    return this.put(`/categories/${id}`, body);
  }
  setCategoryActive(id: string, active: boolean): Promise<void> {
    return this.post(`/categories/${id}/${active ? 'activate' : 'deactivate'}`);
  }
  units(): Promise<UnitDto[]> {
    return this.get('/units');
  }
  createUnit(body: { code: string; name: string; decimals: number }): Promise<UnitDto> {
    return this.post('/units', body);
  }
  updateUnit(id: string, body: { name: string; decimals: number; version: number }): Promise<UnitDto> {
    return this.put(`/units/${id}`, body);
  }
  setUnitActive(id: string, active: boolean): Promise<void> {
    return this.post(`/units/${id}/${active ? 'activate' : 'deactivate'}`);
  }
}

@Injectable({ providedIn: 'root' })
export class ProductsApi extends ApiBase {
  list(query: PageQuery & { categoryId?: string }): Promise<PagedResult<ProductListItemDto>> {
    return this.get('/products', toHttpParams(query));
  }
  getOne(id: string): Promise<ProductDto> {
    return this.get(`/products/${id}`);
  }
  create(body: ProductInput & { sku: string; units?: { unitId: string; factorToBase: number }[]; barcodes?: { value: string; unitId?: string | null }[] }): Promise<ProductDto> {
    return this.post('/products', body);
  }
  update(id: string, body: ProductInput & { version: number }): Promise<ProductDto> {
    return this.put(`/products/${id}`, body);
  }
  setActive(id: string, active: boolean): Promise<void> {
    return this.post(`/products/${id}/${active ? 'activate' : 'deactivate'}`);
  }
  addUnit(id: string, unitId: string, factorToBase: number, version: number): Promise<ProductDto> {
    return this.post(`/products/${id}/units`, { unitId, factorToBase, version });
  }
  changeUnit(id: string, productUnitId: string, factorToBase: number, version: number): Promise<ProductDto> {
    return this.put(`/products/${id}/units/${productUnitId}`, { factorToBase, version });
  }
  removeUnit(id: string, productUnitId: string, version: number): Promise<ProductDto> {
    return this.delete(`/products/${id}/units/${productUnitId}`, this.version(version));
  }
  addBarcode(id: string, value: string, unitId: string | null, version: number): Promise<ProductDto> {
    return this.post(`/products/${id}/barcodes`, { value, unitId, version });
  }
  removeBarcode(id: string, barcodeId: string, version: number): Promise<ProductDto> {
    return this.delete(`/products/${id}/barcodes/${barcodeId}`, this.version(version));
  }
  setPrimaryBarcode(id: string, barcodeId: string, version: number): Promise<ProductDto> {
    return this.post(`/products/${id}/barcodes/${barcodeId}/primary`, {}, this.version(version));
  }
  /** Image bytes (authenticated), for an object URL. */
  image(id: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/products/${id}/image`, { responseType: 'blob' }));
  }
  uploadImage(id: string, file: File): Promise<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.put<void>(`${this.base}/products/${id}/image`, form));
  }
  removeImage(id: string): Promise<void> {
    return this.delete(`/products/${id}/image`);
  }
  lookupBarcode(value: string): Promise<BarcodeLookupDto> {
    return this.get(`/product-barcodes/${encodeURIComponent(value)}`);
  }
}

@Injectable({ providedIn: 'root' })
export class UsersApi extends ApiBase {
  list(query: PageQuery & { role?: string }): Promise<PagedResult<UserListItemDto>> {
    return this.get('/users', toHttpParams(query));
  }
  getOne(id: string): Promise<UserDto> {
    return this.get(`/users/${id}`);
  }
  create(body: { email: string; displayName: string; password: string; roles: string[]; allWarehouses: boolean; warehouseIds: string[] }): Promise<UserDto> {
    return this.post('/users', body);
  }
  update(id: string, body: { displayName: string; roles: string[]; allWarehouses: boolean; warehouseIds: string[]; concurrencyStamp: string }): Promise<UserDto> {
    return this.put(`/users/${id}`, body);
  }
  setActive(id: string, active: boolean): Promise<void> {
    return this.post(`/users/${id}/${active ? 'activate' : 'deactivate'}`);
  }
  resetPassword(id: string, newPassword: string): Promise<void> {
    return this.post(`/users/${id}/reset-password`, { newPassword });
  }
  unlock(id: string): Promise<void> {
    return this.post(`/users/${id}/unlock`);
  }
  roles(): Promise<RoleDto[]> {
    return this.get('/roles');
  }
}

@Injectable({ providedIn: 'root' })
export class ImportsApi extends ApiBase {
  template(kind: ImportKind): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/imports/${kind}/template`, { responseType: 'blob' }));
  }
  run(kind: ImportKind, file: File, dryRun: boolean): Promise<ImportResultDto> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<ImportResultDto>(`${this.base}/imports/${kind}`, form, { params: new HttpParams().set('dryRun', dryRun) }));
  }
}
