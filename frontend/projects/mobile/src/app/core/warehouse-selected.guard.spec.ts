import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL, AuthService, KEY_VALUE_STORE, WarehouseContextService } from '@wh/shared';
import { warehouseSelectedGuard } from './warehouse-selected.guard';

const API = 'https://api.test/api/v1';

async function signInWith(warehouses: { id: string; code: string; name: string }[]) {
  const kv = new Map<string, string>();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: API },
      { provide: KEY_VALUE_STORE, useValue: { get: (k: string) => kv.get(k) ?? null, set: (k: string, v: string) => kv.set(k, v), remove: (k: string) => kv.delete(k) } },
    ],
  });
  const p = TestBed.inject(AuthService).login('a', 'b');
  TestBed.inject(HttpTestingController)
    .expectOne(`${API}/auth/login`)
    .flush({
      accessToken: 't',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      user: { id: '1', email: 'a', displayName: 'A', roles: [], permissions: [], allWarehouses: false, warehouses },
    });
  await p;
  TestBed.tick();
}

const runGuard = () => TestBed.runInInjectionContext(() => warehouseSelectedGuard({}, []));

describe('warehouseSelectedGuard', () => {
  it('redirects to warehouse selection when the user has several warehouses and none chosen', async () => {
    await signInWith([
      { id: 'w1', code: 'MAIN', name: 'Main' },
      { id: 'w2', code: 'NORTH', name: 'North' },
    ]);
    const result = runGuard();
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/select-warehouse');
  });

  it('lets the user through once a warehouse is selected', async () => {
    await signInWith([
      { id: 'w1', code: 'MAIN', name: 'Main' },
      { id: 'w2', code: 'NORTH', name: 'North' },
    ]);
    TestBed.inject(WarehouseContextService).select('w2');
    expect(runGuard()).toBe(true);
  });

  it('lets the user through when only one warehouse exists (auto-selected)', async () => {
    await signInWith([{ id: 'w1', code: 'MAIN', name: 'Main' }]);
    expect(runGuard()).toBe(true);
  });
});
