import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { ConfirmationService, MessageService } from 'primeng/api';
import { API_BASE_URL, AUTH_CLIENT, whHttpInterceptors } from '@wh/shared';
import { environment } from '../environments/environment';
import { HarbourPreset } from './core/theme';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors(whHttpInterceptors)),
    providePrimeNG({
      ripple: false,
      theme: {
        preset: HarbourPreset,
        options: {
          darkModeSelector: '.wh-dark',
          cssLayer: { name: 'primeng', order: 'base, primeng, app' },
        },
      },
    }),
    MessageService,
    ConfirmationService,
    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    { provide: AUTH_CLIENT, useValue: 'web' },
  ],
};
