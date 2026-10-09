import { HttpParams } from '@angular/common/http';
import { PageQuery } from '../models/paging';

/** Serialises a query object, skipping empty values. Arrays become repeated parameters. */
export function toHttpParams(query: PageQuery | undefined): HttpParams {
  let params = new HttpParams();
  if (!query) return params;
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    if (Array.isArray(value)) {
      for (const v of value) params = params.append(key, String(v));
    } else {
      params = params.set(key, String(value));
    }
  }
  return params;
}
