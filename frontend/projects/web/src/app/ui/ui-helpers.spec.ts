import { FormControl, FormGroup } from '@angular/forms';
import { applyFieldErrors, nullIfBlank } from './api-feedback';
import { fromLazyEvent } from './table-query';

describe('applyFieldErrors', () => {
  it('puts server messages on matching controls and reports the rest', () => {
    const form = new FormGroup({ sku: new FormControl('x'), name: new FormControl('') });
    const unmatched = applyFieldErrors(form, { sku: ['SKU already exists'], version: ['stale'] });
    expect(form.controls.sku.errors).toEqual({ server: 'SKU already exists' });
    expect(form.controls.sku.touched).toBe(true);
    expect(unmatched).toEqual(['version']);
  });
});

describe('nullIfBlank', () => {
  it('turns blank strings into null and keeps other values', () => {
    expect(nullIfBlank({ a: '  ', b: 'x', c: 0, d: false })).toEqual({ a: null, b: 'x', c: 0, d: false });
  });
});

describe('fromLazyEvent', () => {
  it('maps PrimeNG paging and sorting to the API query', () => {
    expect(fromLazyEvent({ first: 50, rows: 25, sortField: 'name', sortOrder: -1 }, { page: 1, pageSize: 25, search: 'soap' })).toEqual({
      page: 3,
      pageSize: 25,
      sortBy: 'name',
      sortDir: 'desc',
      search: 'soap',
    });
  });

  it('keeps the current sort when the event has none', () => {
    expect(fromLazyEvent({ first: 0, rows: 50 }, { page: 2, pageSize: 25, sortBy: 'sku', sortDir: 'asc' })).toEqual({ page: 1, pageSize: 50, sortBy: 'sku', sortDir: 'asc' });
  });
});
