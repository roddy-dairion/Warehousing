import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';
import { Dialog } from 'primeng/dialog';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Skeleton } from 'primeng/skeleton';
import { ApiError, AuthService, CustomerAddressDto, CustomerDto, CustomersApi, Permissions } from '@wh/shared';
import { ApiFeedback, nullIfBlank } from '../../ui/api-feedback';
import { FieldError } from '../../ui/field-error';
import { StatusTag } from '../../ui/status-tag';
import { LoadError } from '../../ui/load-error';

@Component({
  selector: 'wh-customer-edit',
  imports: [ReactiveFormsModule, RouterLink, Button, InputText, Textarea, Dialog, ToggleSwitch, TableModule, Tag, Skeleton, FieldError, StatusTag, LoadError],
  templateUrl: './customer-edit.html',
})
export class CustomerEdit implements OnInit {
  private readonly api = inject(CustomersApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.CustomersManage);

  readonly id = input<string>();

  protected readonly customer = signal<CustomerDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly isNew = computed(() => !this.id());

  protected readonly addressDialog = signal(false);
  protected readonly editingAddress = signal<CustomerAddressDto | null>(null);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    reference: ['', [Validators.required, Validators.maxLength(32), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]*$/)]],
    companyName: ['', [Validators.required, Validators.maxLength(200)]],
    contactName: ['', Validators.maxLength(120)],
    phone: ['', Validators.maxLength(40)],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    billingLine1: ['', Validators.maxLength(200)],
    billingLine2: ['', Validators.maxLength(200)],
    billingCity: ['', Validators.maxLength(100)],
    billingCountry: ['', Validators.maxLength(100)],
    notes: ['', Validators.maxLength(2000)],
  });

  protected readonly addressForm = this.fb.group({
    label: ['', [Validators.required, Validators.maxLength(60)]],
    line1: ['', [Validators.required, Validators.maxLength(200)]],
    line2: ['', Validators.maxLength(200)],
    city: ['', Validators.maxLength(100)],
    country: ['', Validators.maxLength(100)],
    contactName: ['', Validators.maxLength(120)],
    phone: ['', Validators.maxLength(40)],
    isDefault: [false],
  });

  async ngOnInit(): Promise<void> {
    if (!this.canManage) this.form.disable();
    await this.load();
  }

  protected async load(): Promise<void> {
    const id = this.id();
    this.loading.set(true);
    this.loadError.set(null);
    try {
      if (id) this.setCustomer(await this.api.getOne(id));
      else this.form.controls.billingCountry.setValue('Mauritius');
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const { reference, ...rest } = nullIfBlank(this.form.getRawValue());
    try {
      const current = this.customer();
      if (current) {
        this.setCustomer(await this.api.update(current.id, { ...rest, companyName: rest.companyName!, version: current.version }));
        this.feedback.success('Customer saved.');
      } else {
        const created = await this.api.create({ ...rest, companyName: rest.companyName!, reference: reference! });
        this.feedback.success(`Customer ${created.reference} created. Add delivery addresses below.`);
        await this.router.navigate(['/customers', created.id], { replaceUrl: true });
      }
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.saving.set(false);
    }
  }

  protected toggleActive(): void {
    const c = this.customer();
    if (!c) return;
    const activate = !c.isActive;
    this.confirm.confirm({
      header: activate ? 'Reactivate customer' : 'Deactivate customer',
      message: activate ? `Allow new dispatches to ${c.companyName} again?` : `Deactivate ${c.companyName}? No new dispatches can be created for them. History is kept.`,
      acceptLabel: activate ? 'Reactivate' : 'Deactivate',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: activate ? undefined : 'p-button-danger',
      accept: async () => {
        try {
          await this.api.setActive(c.id, activate);
          this.setCustomer(await this.api.getOne(c.id));
        } catch (e) {
          this.feedback.error(e);
        }
      },
    });
  }

  // ---- Addresses ----

  protected openAddress(address: CustomerAddressDto | null): void {
    this.editingAddress.set(address);
    this.addressForm.reset(
      address
        ? { label: address.label, line1: address.line1, line2: address.line2 ?? '', city: address.city ?? '', country: address.country ?? '', contactName: address.contactName ?? '', phone: address.phone ?? '', isDefault: address.isDefault }
        : { label: '', line1: '', line2: '', city: '', country: this.customer()?.billingCountry ?? '', contactName: '', phone: '', isDefault: (this.customer()?.addresses.length ?? 0) === 0 },
    );
    this.addressDialog.set(true);
  }

  protected async saveAddress(): Promise<void> {
    const c = this.customer();
    if (!c || this.addressForm.invalid) {
      this.addressForm.markAllAsTouched();
      return;
    }
    const value = nullIfBlank(this.addressForm.getRawValue());
    const address = { ...value, label: value.label!, line1: value.line1! };
    this.saving.set(true);
    try {
      const editing = this.editingAddress();
      this.setCustomer(editing ? await this.api.updateAddress(c.id, editing.id, address, c.version) : await this.api.addAddress(c.id, address, c.version));
      this.addressDialog.set(false);
      this.feedback.success(editing ? 'Address saved.' : 'Address added.');
    } catch (e) {
      const err = ApiError.from(e);
      // Field errors come back as "address.label" etc.
      if (err.kind === 'validation') {
        for (const [key, messages] of Object.entries(err.fieldErrors)) {
          const control = this.addressForm.get(key.replace(/^address\./, ''));
          if (control && messages[0]) control.setErrors({ server: messages[0] });
        }
      }
      this.feedback.error(e);
    } finally {
      this.saving.set(false);
    }
  }

  protected async toggleAddress(a: CustomerAddressDto): Promise<void> {
    const c = this.customer();
    if (!c) return;
    try {
      this.setCustomer(await this.api.setAddressActive(c.id, a.id, !a.isActive, c.version));
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected addressLines(a: CustomerAddressDto): string {
    return [a.line1, a.line2, a.city, a.country].filter(Boolean).join(', ');
  }

  private setCustomer(c: CustomerDto): void {
    this.customer.set(c);
    this.form.reset({
      reference: c.reference,
      companyName: c.companyName,
      contactName: c.contactName ?? '',
      phone: c.phone ?? '',
      email: c.email ?? '',
      billingLine1: c.billingLine1 ?? '',
      billingLine2: c.billingLine2 ?? '',
      billingCity: c.billingCity ?? '',
      billingCountry: c.billingCountry ?? '',
      notes: c.notes ?? '',
    });
    this.form.controls.reference.disable();
  }
}
