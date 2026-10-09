import { Injectable } from '@angular/core';
import { SecureStorage } from '@aparajita/capacitor-secure-storage';
import { TokenStore } from '@wh/shared';

const REFRESH_TOKEN_KEY = 'wh.refreshToken';

/**
 * Keeps the refresh token in the iOS Keychain / Android Keystore.
 * In a desktop browser (development) the plugin falls back to localStorage — never ship that.
 */
@Injectable({ providedIn: 'root' })
export class SecureTokenStore implements TokenStore {
  async getRefreshToken(): Promise<string | null> {
    try {
      return await SecureStorage.getItem(REFRESH_TOKEN_KEY);
    } catch {
      return null;
    }
  }

  async setRefreshToken(token: string): Promise<void> {
    await SecureStorage.setItem(REFRESH_TOKEN_KEY, token);
  }

  async clear(): Promise<void> {
    try {
      await SecureStorage.removeItem(REFRESH_TOKEN_KEY);
    } catch {
      /* nothing stored */
    }
  }
}
