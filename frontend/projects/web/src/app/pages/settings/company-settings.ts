import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { ApiError, CompanyProfileDto, SettingsApi } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { AuthImage } from '../../ui/auth-image';
import { FieldError } from '../../ui/field-error';
import { LoadError } from '../../ui/load-error';

const TIME_ZONES = ['Indian/Mauritius', 'Europe/Zurich', 'Europe/Paris', 'Europe/London', 'Africa/Johannesburg', 'Indian/Reunion', 'Asia/Dubai', 'UTC'];

/** Company details printed on documents, the logo, and the delivery-note email text. */
@Component({
  selector: 'wh-company-settings',
  imports: [ReactiveFormsModule, RouterLink, Button, InputText, Textarea, Select, Skeleton, AuthImage, FieldError, LoadError],
  template: `
    <section class="page">
      <header class="page-header">
        <div>
          <h1>Company and email</h1>
          <p>Printed on packing slips and delivery notes. Edit the document layouts in <a routerLink="/settings/templates">Document templates</a>.</p>
        </div>
        <div class="page-actions"><p-button label="Save" icon="pi pi-save" (onClick)="save()" [loading]="saving()" [disabled]="!profile()" /></div>
      </header>
      @if (loadError(); as m) {
        <wh-load-error [message]="m" (retry)="load()" />
      } @else if (!profile()) {
        <p-skeleton height="20rem" />
      } @else {
        <form class="panel" [formGroup]="form" novalidate>
          <h2>Company</h2>
          <div class="form-grid">
            <div class="field"><label for="name">Company name</label><input pInputText id="name" formControlName="name" /><wh-field-error [control]="form.controls.name" requiredText="Enter the company name." /></div>
            <div class="field"><label for="email">Email</label><input pInputText id="email" formControlName="email" /></div>
            <div class="field"><label for="phone">Phone</label><input pInputText id="phone" formControlName="phone" /></div>
            <div class="field"><label for="website">Website</label><input pInputText id="website" formControlName="website" /></div>
            <div class="field"><label for="a1">Address line 1</label><input pInputText id="a1" formControlName="addressLine1" /></div>
            <div class="field"><label for="a2">Address line 2</label><input pInputText id="a2" formControlName="addressLine2" /></div>
            <div class="field"><label for="city">City</label><input pInputText id="city" formControlName="city" /></div>
            <div class="field"><label for="country">Country</label><input pInputText id="country" formControlName="country" /></div>
            <div class="field"><label for="reg">Registration number</label><input pInputText id="reg" formControlName="registrationNumber" /></div>
            <div class="field"><label for="vat">VAT number</label><input pInputText id="vat" formControlName="vatNumber" /></div>
            <div class="field">
              <label for="tz">Time zone for printed dates</label>
              <p-select inputId="tz" formControlName="timeZone" [options]="timeZones" [editable]="true" appendTo="body" />
              <wh-field-error [control]="form.controls.timeZone" />
            </div>
            <div class="field span-all"><label for="footer">Footer text</label><textarea pTextarea id="footer" formControlName="footerText" rows="2" [autoResize]="true"></textarea></div>
          </div>
        </form>

        <div class="panel">
          <h2>Logo</h2>
          <div class="logo-row">
            @if (profile()!.hasLogo) {
              <div class="logo"><wh-auth-image [load]="logoLoader()" alt="Company logo" maxHeight="4rem" /></div>
              <p-button label="Remove" severity="danger" [text]="true" (onClick)="removeLogo()" />
            } @else {
              <span class="muted">No logo. The company name is printed instead.</span>
            }
            <label class="upload">
              <input type="file" accept="image/png,image/jpeg,image/webp" (change)="uploadLogo($event)" />
              <span class="p-button p-button-outlined p-button-sm">Upload logo…</span>
            </label>
          </div>
          <p class="hint">PNG, JPEG or WebP, up to 1 MB. A wide logo about 600 × 150 pixels prints best.</p>
        </div>

        <form class="panel" [formGroup]="form" novalidate>
          <h2>Delivery-note email</h2>
          <p class="muted">Sent from the dispatch mailbox with the signed delivery note attached. You can use placeholders such as <code>{{ '{{' }} dispatch.number {{ '}}' }}</code>, <code>{{ '{{' }} customer.name {{ '}}' }}</code> and <code>{{ '{{' }} delivery.receiver_name {{ '}}' }}</code>.</p>
          <div class="form-grid">
            <div class="field span-all"><label for="subject">Subject</label><input pInputText id="subject" formControlName="deliveryEmailSubject" /><wh-field-error [control]="form.controls.deliveryEmailSubject" /></div>
            <div class="field span-all"><label for="body">Text</label><textarea pTextarea id="body" formControlName="deliveryEmailBody" rows="8"></textarea><wh-field-error [control]="form.controls.deliveryEmailBody" /></div>
            <div class="field span-all"><label for="bcc">Copy to (internal, comma separated)</label><input pInputText id="bcc" formControlName="deliveryEmailBcc" placeholder="e.g. dispatch@hellocherie.ch" /></div>
          </div>
        </form>
      }
    </section>
  `,
  styles: `
    .logo-row { display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .logo { padding: 0.5rem; border: 1px solid var(--wh-line); border-radius: 6px; background: #fff; }
    .upload input { display: none; }
    .upload span { cursor: pointer; }
    .hint { font-size: 0.8125rem; color: var(--wh-muted); }
    code { background: var(--wh-concrete); padding: 0 0.25rem; border-radius: 3px; }
  `,
})
export class CompanySettings implements OnInit {
  private readonly api = inject(SettingsApi);
  private readonly feedback = inject(ApiFeedback);
  protected readonly timeZones = TIME_ZONES;
  protected readonly profile = signal<CompanyProfileDto | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly logoLoader = signal<() => Promise<Blob>>(() => this.api.logo());

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    addressLine1: [''], addressLine2: [''], city: [''], country: [''], phone: [''], email: [''], website: [''],
    registrationNumber: [''], vatNumber: [''], footerText: [''],
    timeZone: ['Indian/Mauritius', Validators.required],
    deliveryEmailSubject: ['', Validators.required],
    deliveryEmailBody: ['', Validators.required],
    deliveryEmailBcc: [''],
  });

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loadError.set(null);
    try {
      const p = await this.api.company();
      this.profile.set(p);
      this.form.reset({
        name: p.name, addressLine1: p.addressLine1 ?? '', addressLine2: p.addressLine2 ?? '', city: p.city ?? '', country: p.country ?? '', phone: p.phone ?? '',
        email: p.email ?? '', website: p.website ?? '', registrationNumber: p.registrationNumber ?? '', vatNumber: p.vatNumber ?? '', footerText: p.footerText ?? '',
        timeZone: p.timeZone, deliveryEmailSubject: p.deliveryEmailSubject, deliveryEmailBody: p.deliveryEmailBody, deliveryEmailBcc: p.deliveryEmailBcc ?? '',
      });
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    }
  }

  protected async save(): Promise<void> {
    const p = this.profile();
    this.form.markAllAsTouched();
    if (!p || this.form.invalid) return;
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      const blank = (s: string) => (s.trim() === '' ? null : s.trim());
      this.profile.set(await this.api.updateCompany({
        name: v.name.trim(), addressLine1: blank(v.addressLine1), addressLine2: blank(v.addressLine2), city: blank(v.city), country: blank(v.country),
        phone: blank(v.phone), email: blank(v.email), website: blank(v.website), registrationNumber: blank(v.registrationNumber), vatNumber: blank(v.vatNumber),
        footerText: blank(v.footerText), timeZone: v.timeZone.trim(), deliveryEmailSubject: v.deliveryEmailSubject, deliveryEmailBody: v.deliveryEmailBody,
        deliveryEmailBcc: blank(v.deliveryEmailBcc), version: p.version,
      }));
      this.form.markAsPristine();
      this.feedback.success('Company settings saved.');
    } catch (e) {
      this.feedback.error(e, this.form);
    } finally {
      this.saving.set(false);
    }
  }

  protected async uploadLogo(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      await this.api.setLogo(file);
      this.feedback.success('Logo uploaded.');
      await this.load();
      this.logoLoader.set(() => this.api.logo());
    } catch (e) {
      this.feedback.error(e);
    }
  }

  protected async removeLogo(): Promise<void> {
    try {
      await this.api.removeLogo();
      await this.load();
    } catch (e) {
      this.feedback.error(e);
    }
  }
}
