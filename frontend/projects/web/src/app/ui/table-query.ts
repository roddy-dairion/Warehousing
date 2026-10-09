import { TableLazyLoadEvent } from 'primeng/table';
import { PageQuery } from '@wh/shared';

/** Maps a PrimeNG lazy-load event to the API's paging/sorting query. */
export function fromLazyEvent(event: TableLazyLoadEvent, current: PageQuery): PageQuery {
  const rows = event.rows ?? current.pageSize ?? 25;
  const first = event.first ?? 0;
  const sortField = typeof event.sortField === 'string' ? event.sortField : undefined;
  return {
    ...current,
    page: Math.floor(first / rows) + 1,
    pageSize: rows,
    sortBy: sortField ?? current.sortBy,
    sortDir: sortField ? (event.sortOrder === -1 ? 'desc' : 'asc') : current.sortDir,
  };
}

/** Calls fn after the user stops typing. */
export function debounce<A extends unknown[]>(fn: (...args: A) => void, ms = 300): (...args: A) => void {
  let handle: ReturnType<typeof setTimeout> | undefined;
  return (...args: A) => {
    clearTimeout(handle);
    handle = setTimeout(() => fn(...args), ms);
  };
}

export const ACTIVE_FILTERS = [
  { label: 'Active', value: true },
  { label: 'Inactive', value: false },
];
