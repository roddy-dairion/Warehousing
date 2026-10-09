import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'wh-not-found',
  imports: [RouterLink],
  template: `
    <main class="nf">
      <h1>Page not found</h1>
      <p>Check the address, or go back to the dashboard.</p>
      <a routerLink="/">Go to the dashboard</a>
    </main>
  `,
  styles: `.nf { padding: 3rem 1.5rem; } h1 { margin: 0 0 0.5rem; font-size: 1.5rem; font-weight: 600; }`,
})
export class NotFoundPage {}
