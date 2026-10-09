import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { PreloadAllModules, provideRouter, RouteReuseStrategy, withComponentInputBinding, withPreloading } from '@angular/router';
import { IonicRouteStrategy, provideIonicAngular } from '@ionic/angular';
import { API_BASE_URL, AUTH_CLIENT, TOKEN_STORE, whHttpInterceptors } from '@wh/shared';
import { environment } from '../environments/environment';
import { SecureTokenStore } from './core/secure-token-store';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Ionic relies on NgZone, so the mobile app keeps zone-based change detection.
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideIonicAngular({ mode: 'md' }),
    { provide: RouteReuseStrategy, useClass: IonicRouteStrategy },
    provideRouter(routes, withPreloading(PreloadAllModules), withComponentInputBinding()),
    provideHttpClient(withInterceptors(whHttpInterceptors)),
    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    { provide: AUTH_CLIENT, useValue: 'mobile' },
    { provide: TOKEN_STORE, useExisting: SecureTokenStore },
  ],
};
