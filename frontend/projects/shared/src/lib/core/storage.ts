import { Injectable, InjectionToken } from '@angular/core';

/** Storage for the refresh token. Only the mobile app stores one; on web it is an HttpOnly cookie. */
export interface TokenStore {
  getRefreshToken(): Promise<string | null>;
  setRefreshToken(token: string): Promise<void>;
  clear(): Promise<void>;
}

/** Web default: nothing is stored client-side. */
@Injectable({ providedIn: 'root' })
export class NoopTokenStore implements TokenStore {
  getRefreshToken(): Promise<string | null> {
    return Promise.resolve(null);
  }
  setRefreshToken(): Promise<void> {
    return Promise.resolve();
  }
  clear(): Promise<void> {
    return Promise.resolve();
  }
}

export const TOKEN_STORE = new InjectionToken<TokenStore>('TOKEN_STORE', {
  providedIn: 'root',
  factory: () => new NoopTokenStore(),
});

/** Small key/value store for non-sensitive UI preferences (e.g. selected warehouse). */
export interface KeyValueStore {
  get(key: string): string | null;
  set(key: string, value: string): void;
  remove(key: string): void;
}

/** localStorage wrapper that never throws (private mode, blocked storage...). */
export class BrowserKeyValueStore implements KeyValueStore {
  get(key: string): string | null {
    try {
      return globalThis.localStorage?.getItem(key) ?? null;
    } catch {
      return null;
    }
  }
  set(key: string, value: string): void {
    try {
      globalThis.localStorage?.setItem(key, value);
    } catch {
      /* ignore */
    }
  }
  remove(key: string): void {
    try {
      globalThis.localStorage?.removeItem(key);
    } catch {
      /* ignore */
    }
  }
}

export const KEY_VALUE_STORE = new InjectionToken<KeyValueStore>('KEY_VALUE_STORE', {
  providedIn: 'root',
  factory: () => new BrowserKeyValueStore(),
});
