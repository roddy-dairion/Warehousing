import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL, AUTH_CLIENT } from '../core/tokens';
import { TOKEN_STORE } from '../core/storage';
import { skipAuth } from '../http/context';
import { AuthResponse, CurrentUser, LoginRequest } from '../models/auth';
import { Permission } from './permissions';

/** Refresh this long before the access token actually expires. */
const EXPIRY_SKEW_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly client = inject(AUTH_CLIENT);
  private readonly tokenStore = inject(TOKEN_STORE);

  private readonly _user = signal<CurrentUser | null>(null);
  private readonly _accessToken = signal<string | null>(null);
  private accessTokenExpiresAt = 0;
  private refreshInFlight: Promise<boolean> | null = null;

  /** The signed-in user, or null. */
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  /** Access token kept in memory only. */
  readonly accessToken = this._accessToken.asReadonly();

  async login(email: string, password: string): Promise<CurrentUser> {
    const body: LoginRequest = { email: email.trim(), password, client: this.client };
    const res = await firstValueFrom(
      this.http.post<AuthResponse>(`${this.baseUrl}/auth/login`, body, {
        context: skipAuth(),
        withCredentials: this.client === 'web',
      }),
    );
    await this.apply(res);
    return res.user;
  }

  /**
   * Obtains a new access token using the refresh token (cookie on web, secure storage on mobile).
   * Concurrent callers share one request. Resolves false (and signs out locally) on failure.
   */
  refresh(): Promise<boolean> {
    this.refreshInFlight ??= this.doRefresh().finally(() => (this.refreshInFlight = null));
    return this.refreshInFlight;
  }

  /** Called on app start: restores the session if a valid refresh token exists. */
  restoreSession(): Promise<boolean> {
    return this.isAuthenticated() ? Promise.resolve(true) : this.refresh();
  }

  async logout(): Promise<void> {
    try {
      const refreshToken = await this.tokenStore.getRefreshToken();
      await firstValueFrom(
        this.http.post<void>(
          `${this.baseUrl}/auth/logout`,
          { refreshToken },
          { context: skipAuth(), withCredentials: this.client === 'web' },
        ),
      );
    } catch {
      // Best effort: the local session is cleared regardless.
    } finally {
      await this.clearLocal();
    }
  }

  isAccessTokenExpiring(now: number = Date.now()): boolean {
    return this._accessToken() !== null && now >= this.accessTokenExpiresAt - EXPIRY_SKEW_MS;
  }

  hasPermission(permission: Permission | string): boolean {
    return this._user()?.permissions.includes(permission) ?? false;
  }

  hasAnyPermission(...permissions: (Permission | string)[]): boolean {
    return permissions.some((p) => this.hasPermission(p));
  }

  private async doRefresh(): Promise<boolean> {
    try {
      let refreshToken: string | null = null;
      if (this.client === 'mobile') {
        refreshToken = await this.tokenStore.getRefreshToken();
        if (!refreshToken) {
          await this.clearLocal();
          return false;
        }
      }
      const res = await firstValueFrom(
        this.http.post<AuthResponse>(
          `${this.baseUrl}/auth/refresh`,
          { refreshToken },
          { context: skipAuth(), withCredentials: this.client === 'web' },
        ),
      );
      await this.apply(res);
      return true;
    } catch {
      await this.clearLocal();
      return false;
    }
  }

  private async apply(res: AuthResponse): Promise<void> {
    this._accessToken.set(res.accessToken);
    this.accessTokenExpiresAt = Date.parse(res.accessTokenExpiresAt) || 0;
    if (res.refreshToken) {
      await this.tokenStore.setRefreshToken(res.refreshToken);
    }
    this._user.set(res.user);
  }

  private async clearLocal(): Promise<void> {
    this._accessToken.set(null);
    this.accessTokenExpiresAt = 0;
    this._user.set(null);
    await this.tokenStore.clear();
  }
}
