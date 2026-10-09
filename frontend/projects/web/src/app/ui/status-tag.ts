import { Component, input } from '@angular/core';
import { Tag } from 'primeng/tag';

@Component({
  selector: 'wh-status-tag',
  imports: [Tag],
  template: `<p-tag [value]="active() ? 'Active' : 'Inactive'" [severity]="active() ? 'success' : 'secondary'" />`,
})
export class StatusTag {
  readonly active = input.required<boolean>();
}
