/** DTOs mirrored from the API (Inventory.Application). Keep in sync with the backend records. */

export interface WarehouseDto {
  id: string;
  code: string;
  name: string;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  country: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
  version: number;
}

export interface WarehouseContact {
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  country?: string | null;
  phone?: string | null;
  email?: string | null;
}

export type LocationType = 'storage' | 'receiving' | 'dispatch' | 'quarantine';

export const LOCATION_TYPES: { value: LocationType; label: string }[] = [
  { value: 'storage', label: 'Storage bin' },
  { value: 'receiving', label: 'Receiving area' },
  { value: 'dispatch', label: 'Dispatch dock' },
  { value: 'quarantine', label: 'Quarantine' },
];

export interface LocationDto {
  id: string;
  warehouseId: string;
  code: string;
  zone: string | null;
  type: LocationType;
  barcode: string | null;
  isPickable: boolean;
  isSystem: boolean;
  isActive: boolean;
  version: number;
}

export interface CustomerListItemDto {
  id: string;
  reference: string;
  companyName: string;
  contactName: string | null;
  phone: string | null;
  email: string | null;
  city: string | null;
  addressCount: number;
  isActive: boolean;
}

export interface CustomerAddressDto {
  id: string;
  label: string;
  line1: string;
  line2: string | null;
  city: string | null;
  country: string | null;
  contactName: string | null;
  phone: string | null;
  isDefault: boolean;
  isActive: boolean;
}

export interface CustomerDto {
  id: string;
  reference: string;
  companyName: string;
  contactName: string | null;
  phone: string | null;
  email: string | null;
  billingLine1: string | null;
  billingLine2: string | null;
  billingCity: string | null;
  billingCountry: string | null;
  notes: string | null;
  isActive: boolean;
  addresses: CustomerAddressDto[];
  version: number;
}

export interface AddressInput {
  label: string;
  line1: string;
  line2?: string | null;
  city?: string | null;
  country?: string | null;
  contactName?: string | null;
  phone?: string | null;
  isDefault: boolean;
}

export interface CustomerInput {
  companyName: string;
  contactName?: string | null;
  phone?: string | null;
  email?: string | null;
  billingLine1?: string | null;
  billingLine2?: string | null;
  billingCity?: string | null;
  billingCountry?: string | null;
  notes?: string | null;
}

export interface CategoryDto {
  id: string;
  code: string;
  name: string;
  parentId: string | null;
  path: string;
  isActive: boolean;
  productCount: number;
  version: number;
}

export interface UnitDto {
  id: string;
  code: string;
  name: string;
  decimals: number;
  isActive: boolean;
  version: number;
}

export interface ProductListItemDto {
  id: string;
  sku: string;
  name: string;
  categoryName: string | null;
  baseUnitCode: string;
  primaryBarcode: string | null;
  minStockQty: number | null;
  isLotTracked: boolean;
  hasExpiry: boolean;
  hasImage: boolean;
  isActive: boolean;
}

export interface ProductUnitDto {
  id: string;
  unitId: string;
  unitCode: string;
  unitName: string;
  factorToBase: number;
}

export interface ProductBarcodeDto {
  id: string;
  value: string;
  productUnitId: string | null;
  unitCode: string;
  factorToBase: number;
  symbology: string | null;
  isPrimary: boolean;
}

export interface ProductDto {
  id: string;
  sku: string;
  name: string;
  description: string | null;
  categoryId: string | null;
  categoryName: string | null;
  baseUnitId: string;
  baseUnitCode: string;
  baseUnitDecimals: number;
  minStockQty: number | null;
  isLotTracked: boolean;
  hasExpiry: boolean;
  shelfLifeDays: number | null;
  hasImage: boolean;
  isActive: boolean;
  units: ProductUnitDto[];
  barcodes: ProductBarcodeDto[];
  createdAt: string;
  updatedAt: string;
  version: number;
}

export interface ProductInput {
  name: string;
  description?: string | null;
  categoryId?: string | null;
  baseUnitId: string;
  minStockQty?: number | null;
  isLotTracked: boolean;
  hasExpiry: boolean;
  shelfLifeDays?: number | null;
}

export interface BarcodeLookupDto {
  productId: string;
  sku: string;
  name: string;
  isActive: boolean;
  barcode: string;
  unitCode: string;
  factorToBase: number;
  baseUnitCode: string;
  isLotTracked: boolean;
  hasExpiry: boolean;
}

export interface UserListItemDto {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  allWarehouses: boolean;
  warehouseCodes: string[];
  isActive: boolean;
  isLockedOut: boolean;
  lastLoginAt: string | null;
}

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  allWarehouses: boolean;
  warehouses: { id: string; code: string; name: string }[];
  isActive: boolean;
  isLockedOut: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  concurrencyStamp: string;
}

export interface RoleDto {
  id: string;
  code: string;
  name: string;
  permissions: string[];
}

export type ImportKind = 'products' | 'customers' | 'locations';

export interface ImportError {
  row: number;
  column: string | null;
  message: string;
}

export interface ImportResultDto {
  kind: ImportKind;
  dryRun: boolean;
  applied: boolean;
  totalRows: number;
  created: number;
  updated: number;
  errors: ImportError[];
}
