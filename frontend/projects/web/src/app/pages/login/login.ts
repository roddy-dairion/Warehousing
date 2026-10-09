import { Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { Message } from 'primeng/message';
import { ApiError, AuthService } from '@wh/shared';

@Component({
  selector: 'wh-login',
  imports: [ReactiveFormsModule, Button, InputText, Password, Message],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Bound from the `returnUrl` query parameter. */
  readonly returnUrl = input<string | undefined>();

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();
    try {
      await this.auth.login(email, password);
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch (e) {
      const err = ApiError.from(e);
      this.error.set(
        err.kind === 'unauthorized' || err.status === 400
          ? 'Email or password is incorrect.'
          : err.status === 429
            ? 'Too many sign-in attempts. Wait a minute and try again.'
            : err.userMessage,
      );
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }

  /** Only allow internal paths, never an absolute URL (open-redirect protection). */
  private safeReturnUrl(): string {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') ? url : '/';
  }
}
