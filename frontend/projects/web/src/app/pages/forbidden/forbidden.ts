import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'wh-forbidden',
  imports: [RouterLink],
  template: `
    <h1>You don't have access to this page</h1>
    <p>Your role doesn't include this area. If you need it, ask an administrator to update your permissions.</p>
    <a routerLink="/">Go to the dashboard</a>
  `,
  styles: `h1 { margin: 0 0 0.5rem; font-size: 1.5rem; font-weight: 600; }`,
})
export class ForbiddenPage {}
