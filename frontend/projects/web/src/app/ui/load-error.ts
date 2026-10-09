import { Component, input, output } from '@angular/core';
import { Button } from 'primeng/button';

/** Inline error block with a retry button, for failed page loads. */
@Component({
  selector: 'wh-load-error',
  imports: [Button],
  template: `
    <div class="load-error" role="alert">
      <i class="pi pi-exclamation-triangle" aria-hidden="true"></i>
      <span>{{ message() }}</span>
      <p-button label="Try again" [text]="true" size="small" (onClick)="retry.emit()" />
    </div>
  `,
})
export class LoadError {
  readonly message = input.required<string>();
  readonly retry = output<void>();
}
