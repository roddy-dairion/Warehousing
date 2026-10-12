import { Component, computed, input } from '@angular/core';
import { IonBadge } from '@ionic/angular';
import { DispatchStatus, dispatchStatusInfo } from '@wh/shared';

const COLORS: Record<string, string> = { secondary: 'medium', info: 'primary', warn: 'warning', success: 'success', danger: 'danger', contrast: 'dark' };

/** Dispatch status as a coloured badge, with the same labels as the web app. */
@Component({
  selector: 'wh-status-badge',
  imports: [IonBadge],
  template: `<ion-badge [color]="color()">{{ info().label }}</ion-badge>`,
})
export class StatusBadge {
  readonly status = input.required<DispatchStatus>();
  protected readonly info = computed(() => dispatchStatusInfo(this.status()));
  protected readonly color = computed(() => COLORS[this.info().severity] ?? 'medium');
}
