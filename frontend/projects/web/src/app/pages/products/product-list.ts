import { Component, inject, OnInit, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Table, TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { Select } from 'primeng/select';
import { Tag } from 'primeng/tag';
import { ApiError, AuthService, CatalogApi, CategoryDto, PageQuery, Permissions, ProductListItemDto, ProductsApi } from '@wh/shared';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';
import { ACTIVE_FILTERS, debounce, fromLazyEvent } from '../../ui/table-query';

@Component({
  selector: 'wh-product-list',
  imports: [FormsModule, RouterLink, TableModule, Button, InputText, IconField, InputIcon, Select, Tag, StatusTag, LoadError],
  templateUrl: './product-list.html',
})
export class ProductList implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly catalog = inject(CatalogApi);
  private readonly router = inject(Router);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.ProductsManage);
  private readonly table = viewChild.required<Table>('table');

  protected readonly rows = signal<ProductListItemDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categories = signal<CategoryDto[]>([]);
  protected readonly activeFilters = ACTIVE_FILTERS;

  protected search = '';
  protected categoryId: string | null = null;
  protected isActive: boolean | null = true;
  private query: PageQuery = { page: 1, pageSize: 25, sortBy: 'sku', isActive: true };

  async ngOnInit(): Promise<void> {
    try {
      this.categories.set(await this.catalog.categories());
    } catch {
      /* the filter is optional */
    }
  }

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    this.query = fromLazyEvent(event, this.query);
    void this.load();
  }

  protected readonly onSearch = debounce(() => this.applyFilters());

  protected applyFilters(): void {
    this.query = { ...this.query, page: 1, search: this.search || undefined, categoryId: this.categoryId ?? undefined, isActive: this.isActive ?? undefined };
    this.table().first = 0;
    void this.load();
  }

  protected open(row: ProductListItemDto): void {
    void this.router.navigate(['/products', row.id]);
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.api.list(this.query);
      this.rows.set(page.items);
      this.total.set(page.totalCount);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }
}
