import { Component } from '@angular/core';
import { IonApp, IonRouterOutlet } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { close } from 'ionicons/icons';
import { ScannerOverlay } from './core/scanner-overlay';

@Component({
  selector: 'wh-root',
  imports: [IonApp, IonRouterOutlet, ScannerOverlay],
  // The scanner overlay is outside <ion-app> so it stays visible while the app is hidden behind the camera preview.
  template: `<ion-app><ion-router-outlet /></ion-app><wh-scanner-overlay />`,
})
export class App {
  constructor() {
    addIcons({ close });
  }
}
