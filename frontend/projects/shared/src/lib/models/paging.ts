export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type SortDirection = 'asc' | 'desc';

export interface PageQuery {
  page?: number;
  pageSize?: number;
  /** Field name, e.g. `name`. */
  sortBy?: string;
  sortDir?: SortDirection;
  search?: string;
  /** Additional simple filters, serialised as query parameters. */
  [key: string]: string | number | boolean | readonly (string | number)[] | undefined;
}
