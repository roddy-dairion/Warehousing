import { CurrentUser, AuthResponse } from './models/auth';

export const TEST_API = 'https://api.test/api/v1';

export function testUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'u1',
    email: 'op@test.local',
    displayName: 'Operator',
    roles: ['WH_OPERATOR'],
    permissions: ['inventory.read', 'dispatches.pick'],
    allWarehouses: false,
    warehouses: [{ id: 'w1', code: 'MAIN', name: 'Main warehouse' }],
    ...overrides,
  };
}

export function authResponse(overrides: Partial<AuthResponse> = {}, expiresInMs = 15 * 60_000): AuthResponse {
  return {
    accessToken: 'access-1',
    accessTokenExpiresAt: new Date(Date.now() + expiresInMs).toISOString(),
    refreshToken: null,
    user: testUser(),
    ...overrides,
  };
}
