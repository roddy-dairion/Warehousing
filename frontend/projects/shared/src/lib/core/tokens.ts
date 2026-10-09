import { InjectionToken } from '@angular/core';

/** Base URL of the API including the version prefix, e.g. `https://api.example.com/api/v1`. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

/**
 * Which client is calling the API.
 * - `web`: the refresh token lives in an HttpOnly cookie set by the API (never readable by JS).
 * - `mobile`: the refresh token is returned in the body and kept in native secure storage.
 */
export type AuthClientKind = 'web' | 'mobile';
export const AUTH_CLIENT = new InjectionToken<AuthClientKind>('AUTH_CLIENT', {
  providedIn: 'root',
  factory: () => 'web',
});

/** Routes the auth guards redirect to. */
export interface AuthRoutes {
  login: string;
  forbidden: string;
}
export const AUTH_ROUTES = new InjectionToken<AuthRoutes>('AUTH_ROUTES', {
  providedIn: 'root',
  factory: () => ({ login: '/login', forbidden: '/forbidden' }),
});
