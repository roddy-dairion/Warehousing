import { Component, effect, input, OnDestroy, signal, untracked } from '@angular/core';

/**
 * Displays an image that needs the bearer token (fetched as a blob through the API client).
 * Reloads only when the `load` function identity changes, so pass a stable function (not one created in the template).
 */
@Component({
  selector: 'wh-auth-image',
  template: `
    @if (url(); as u) {
      <img [src]="u" [alt]="alt()" [style.max-height]="maxHeight()" />
    } @else if (failed()) {
      <span class="muted">Image unavailable</span>
    } @else {
      <i class="pi pi-spin pi-spinner muted" aria-label="Loading"></i>
    }
  `,
  styles: `
    img { max-width: 100%; display: block; border-radius: 4px; }
  `,
})
export class AuthImage implements OnDestroy {
  readonly load = input.required<() => Promise<Blob>>();
  readonly alt = input('');
  readonly maxHeight = input('12rem');

  protected readonly url = signal<string | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    effect((onCleanup) => {
      const loader = this.load();
      untracked(() => {
        this.revoke();
        this.failed.set(false);
      });
      let current = true;
      loader()
        .then((blob) => {
          if (current) this.url.set(URL.createObjectURL(blob));
        })
        .catch(() => current && this.failed.set(true));
      onCleanup(() => (current = false));
    });
  }

  ngOnDestroy(): void {
    this.revoke();
  }

  private revoke(): void {
    const u = this.url();
    if (u) URL.revokeObjectURL(u);
    this.url.set(null);
  }
}
