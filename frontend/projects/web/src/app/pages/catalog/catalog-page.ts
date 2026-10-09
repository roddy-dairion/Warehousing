import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { Tabs, TabList, Tab, TabPanels, TabPanel } from 'primeng/tabs';
import { ApiError, AuthService, CatalogApi, CategoryDto, Permissions, UnitDto } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';

@Component({
  selector: 'wh-catalog-page',
  imports: [ReactiveFormsModule, TableModule, Button, Dialog, InputText, InputNumber, Select, Tabs, TabList, Tab, TabPanels, TabPanel, FieldError, StatusTag, LoadError],
  templateUrl: './catalog-page.html',
})
export class CatalogPage implements OnInit {
  private readonly api = inject(CatalogApi);
  private readonly feedback = inject(ApiFeedback);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.ProductsManage);

  protected readonly categories = signal<CategoryDto[]>([]);
  protected readonly units = signal<UnitDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly categoryDialog = signal(false);
  protected readonly editingCategory = signal<CategoryDto | null>(null);
  protected readonly unitDialog = signal(false);
  protected readonly editingUnit = signal<UnitDto | null>(null);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly categoryForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{0,31}$/)]],
    name: ['', [Validators.required, Validators.maxLength(120)]],
    parentId: this.fb.control<string | null>(null),
  });
  protected readonly unitForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{1,10}$/)]],
    name: ['', [Validators.required, Validators.maxLength(60)]],
    decimals: [0, [Validators.required, Validators.min(0), Validators.max(3)]],
  });

  /** Parent choices exclude the category being edited (the server also blocks deeper cycles). */
  protected readonly parentChoices = computed(() => this.categories().filter((c) => c.isActive && c.id !== this.editingCategory()?.id));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [categories, units] = await Promise.all([this.api.categories(), this.api.units()]);
      this.categories.set(categories);
      this.units.set(units);
    } catch (e) {
      this.error.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected openCategory(c: CategoryDto | null): void {
    this.editingCategory.set(c);
    this.categoryForm.reset({ code: c?.code ?? '', name: c?.name ?? '', parentId: c?.parentId ?? null });
    if (c) this.categoryForm.controls.code.disable();
    else this.categoryForm.controls.code.enable();
    this.categoryDialog.set(true);
  }

  protected async saveCategory(): Promise<void> {
    if (this.categoryForm.invalid) {
      this.categoryForm.markAllAsTouched();
      return;
    }
    const v = this.categoryForm.getRawValue();
    await this.submit(this.categoryForm, async () => {
      const editing = this.editingCategory();
      if (editing) await this.api.updateCategory(editing.id, { name: v.name, parentId: v.parentId, version: editing.version });
      else await this.api.createCategory(v);
      this.categoryDialog.set(false);
      this.feedback.success('Category saved.');
    });
  }

  protected async toggleCategory(c: CategoryDto): Promise<void> {
    await this.submit(undefined, () => this.api.setCategoryActive(c.id, !c.isActive));
  }

  protected openUnit(u: UnitDto | null): void {
    this.editingUnit.set(u);
    this.unitForm.reset({ code: u?.code ?? '', name: u?.name ?? '', decimals: u?.decimals ?? 0 });
    if (u) this.unitForm.controls.code.disable();
    else this.unitForm.controls.code.enable();
    this.unitDialog.set(true);
  }

  protected async saveUnit(): Promise<void> {
    if (this.unitForm.invalid) {
      this.unitForm.markAllAsTouched();
      return;
    }
    const v = this.unitForm.getRawValue();
    await this.submit(this.unitForm, async () => {
      const editing = this.editingUnit();
      if (editing) await this.api.updateUnit(editing.id, { name: v.name, decimals: v.decimals, version: editing.version });
      else await this.api.createUnit(v);
      this.unitDialog.set(false);
      this.feedback.success('Unit saved.');
    });
  }

  protected async toggleUnit(u: UnitDto): Promise<void> {
    await this.submit(undefined, () => this.api.setUnitActive(u.id, !u.isActive));
  }

  private async submit(form: Parameters<ApiFeedback['error']>[1], action: () => Promise<unknown>): Promise<void> {
    this.saving.set(true);
    try {
      await action();
      await this.load();
    } catch (e) {
      this.feedback.error(e, form);
    } finally {
      this.saving.set(false);
    }
  }
}
