import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL, AUTH_CLIENT } from '../core/tokens';
import { TOKEN_STORE, TokenStore } from '../core/storage';
import { whHttpInterceptors } from '../http/interceptors';
import { AuthService } from './auth.service';
import { authResponse, TEST_API } from '../testing';

class MemoryTokenStore implements TokenStore {
  token: string | null = null;
  async getRefreshToken() {
    return this.token;
  }
  async setRefreshToken(t: string) {
    this.token = t;
  }
  async clear() {
    this.token = null;
  }
}

function setup(client: 'web' | 'mobile') {
  const store = new MemoryTokenStore();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors(whHttpInterceptors)),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: TEST_API },
      { provide: AUTH_CLIENT, useValue: client },
      { provide: TOKEN_STORE, useValue: store },
    ],
  });
  return { auth: TestBed.inject(AuthService), http: TestBed.inject(HttpTestingController), store };
}

const flushMicrotasks = () => new Promise((r) => setTimeout(r, 0));

describe('AuthService', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('web login sends credentials with cookies and keeps the access token in memory only', async () => {
    const { auth, http, store } = setup('web');
    const p = auth.login(' op@test.local ', 'secret');
    const req = http.expectOne(`${TEST_API}/auth/login`);
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.body).toEqual({ email: 'op@test.local', password: 'secret', client: 'web' });
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(authResponse());
    await p;
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('access-1');
    expect(store.token).toBeNull();
  });

  it('mobile login stores the refresh token in the token store', async () => {
    const { auth, http, store } = setup('mobile');
    const p = auth.login('op@test.local', 'secret');
    http.expectOne(`${TEST_API}/auth/login`).flush(authResponse({ refreshToken: 'rt-1' }));
    await p;
    expect(store.token).toBe('rt-1');
  });

  it('concurrent refresh calls share a single request', async () => {
    const { auth, http } = setup('web');
    const a = auth.refresh();
    const b = auth.refresh();
    await flushMicrotasks();
    http.expectOne(`${TEST_API}/auth/refresh`).flush(authResponse({ accessToken: 'access-2' }));
    expect(await a).toBe(true);
    expect(await b).toBe(true);
    expect(auth.accessToken()).toBe('access-2');
  });

  it('failed refresh signs the user out locally', async () => {
    const { auth, http, store } = setup('mobile');
    store.token = 'rt-old';
    const p = auth.refresh();
    await flushMicrotasks();
    http.expectOne(`${TEST_API}/auth/refresh`).flush({ title: 'Invalid token' }, { status: 401, statusText: 'Unauthorized' });
    expect(await p).toBe(false);
    expect(auth.isAuthenticated()).toBe(false);
    expect(store.token).toBeNull();
  });

  it('mobile refresh without a stored token does not call the API', async () => {
    const { auth } = setup('mobile');
    expect(await auth.refresh()).toBe(false);
  });

  it('hasPermission reflects the user permissions', async () => {
    const { auth, http } = setup('web');
    const p = auth.login('a', 'b');
    http.expectOne(`${TEST_API}/auth/login`).flush(authResponse());
    await p;
    expect(auth.hasPermission('dispatches.pick')).toBe(true);
    expect(auth.hasPermission('dispatches.approve')).toBe(false);
    expect(auth.hasAnyPermission('dispatches.approve', 'inventory.read')).toBe(true);
  });
});
