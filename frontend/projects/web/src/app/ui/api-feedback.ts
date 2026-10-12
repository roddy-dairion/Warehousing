import { inject, Injectable } from '@angular/core';
import { AbstractControl, FormGroup } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ApiError } from '@wh/shared';

/**
 * One place to turn API results into user feedback:
 * field errors go next to the matching form control, everything else into a toast.
 */
@Injectable({ providedIn: 'root' })
export class ApiFeedback {
  private readonly messages = inject(MessageService);

  success(detail: string): void {
    this.messages.add({ severity: 'success', summary: detail, life: 3000 });
  }

  warn(detail: string): void {
    this.messages.add({ severity: 'warn', summary: detail, life: 5000 });
  }

  /** Shows the error. Returns the user-facing message. */
  error(error: unknown, form?: FormGroup): string {
    const err = ApiError.from(error);
    if (form && err.kind === 'validation') {
      const unmatched = applyFieldErrors(form, err.fieldErrors);
      if (unmatched.length === 0) {
        this.messages.add({ severity: 'warn', summary: 'Check the highlighted fields.', life: 4000 });
        return err.userMessage;
      }
    }
    const summary =
      err.problem.code === 'concurrency_conflict'
        ? 'Someone else changed this record. Reload the page to see their changes, then try again.'
        : err.userMessage;
    this.messages.add({ severity: err.kind === 'conflict' || err.kind === 'validation' || err.status === 422 ? 'warn' : 'error', summary, life: 6000 });
    return summary;
  }
}

/** Puts server messages on matching controls (camelCase names, nested paths with dots). Returns keys it could not place. */
export function applyFieldErrors(form: FormGroup, errors: Record<string, string[]>): string[] {
  const unmatched: string[] = [];
  for (const [key, messages] of Object.entries(errors)) {
    const control: AbstractControl | null = form.get(key);
    if (control && messages[0]) {
      control.setErrors({ ...(control.errors ?? {}), server: messages[0] });
      control.markAsTouched();
    } else {
      unmatched.push(key);
    }
  }
  return unmatched;
}

/** Empty strings become null so optional fields are cleared rather than stored as "". */
export function nullIfBlank<T extends Record<string, unknown>>(value: T): T {
  const copy: Record<string, unknown> = { ...value };
  for (const [k, v] of Object.entries(copy)) {
    if (typeof v === 'string' && v.trim() === '') copy[k] = null;
  }
  return copy as T;
}
