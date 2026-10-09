import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { IonButton, IonContent, IonInput, IonInputPasswordToggle, IonNote, IonSpinner, IonText } from '@ionic/angular';
import { ApiError, AuthService, WarehouseContextService } from '@wh/shared';

@Component({
  selector: 'wh-login',
  imports: [ReactiveFormsModule, IonContent, IonInput, IonInputPasswordToggle, IonButton, IonSpinner, IonText, IonNote],
  templateUrl: './login.page.html',
  styleUrl: './login.page.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly warehouses = inject(WarehouseContextService);
  private readonly router = inject(Router);

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      await this.router.navigateByUrl(this.warehouses.current() ? '/' : '/select-warehouse', { replaceUrl: true });
    } catch (e) {
      const err = ApiError.from(e);
      this.error.set(err.kind === 'unauthorized' || err.status === 400 ? 'Email or password is incorrect.' : err.userMessage);
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }
}
