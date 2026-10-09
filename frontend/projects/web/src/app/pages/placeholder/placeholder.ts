import { Component, input } from '@angular/core';

/** Stand-in for screens built in later phases. Route data supplies the title and phase. */
@Component({
  selector: 'wh-placeholder',
  template: `
    <h1>{{ title() }}</h1>
    <p class="muted">This screen is built in phase {{ phase() }} of the plan.</p>
  `,
  styles: `
    h1 { margin: 0 0 0.5rem; font-size: 1.5rem; font-weight: 600; }
    .muted { color: var(--wh-muted); }
  `,
})
export class PlaceholderPage {
  readonly title = input('');
  readonly phase = input(6);
}
