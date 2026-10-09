export interface WarehouseSummary {
  id: string;
  code: string;
  name: string;
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  permissions: string[];
  /** True when the user may act in every warehouse (administrators). */
  allWarehouses: boolean;
  /** Warehouses the user may act in, as resolved by the API. */
  warehouses: WarehouseSummary[];
}

export interface LoginRequest {
  email: string;
  password: string;
  client: 'web' | 'mobile';
}

export interface AuthResponse {
  accessToken: string;
  /** ISO-8601 UTC. */
  accessTokenExpiresAt: string;
  /** Only returned to the mobile client; the web client gets an HttpOnly cookie. */
  refreshToken?: string | null;
  user: CurrentUser;
}
