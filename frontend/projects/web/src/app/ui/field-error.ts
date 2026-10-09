import { Component, input } from '@angular/core';
import { AbstractControl } from '@angular/forms';

/** Shows the first error of a control once it has been touched. */
@Component({
  selector: 'wh-field-error',
  template: `
    @if (message(); as m) {
      <small class="field-error" role="alert">{{ m }}</small>
    }
  `,
})
export class FieldError {
  readonly control = input.required<AbstractControl | null>();
  /** Message for "required". */
  readonly requiredText = input('This field is required.');
  /** Message for "pattern". */
  readonly patternText = input('This value has an invalid format.');
  /** Re-evaluated on every change detection: control state (touched, errors) is not a signal. */
  protected message(): string | null {
    const c = this.control();
    if (!c || !c.touched || !c.errors) return null;
    const e = c.errors;
    if (e['server']) return String(e['server']);
    if (e['required']) return this.requiredText();
    if (e['email']) return 'Enter a valid email address.';
    if (e['maxlength']) return `At most ${e['maxlength'].requiredLength} characters.`;
    if (e['minlength']) return `At least ${e['minlength'].requiredLength} characters.`;
    if (e['pattern']) return this.patternText();
    if (e['min']) return `Must be at least ${e['min'].min}.`;
    return 'Invalid value.';
  }
}
