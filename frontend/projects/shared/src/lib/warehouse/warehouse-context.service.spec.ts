import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../core/tokens';
import { KEY_VALUE_STORE, KeyValueStore } from '../core/storage';
import { AuthService } from '../auth/auth.service';
import { WarehouseContextService } from './warehouse-context.service';
import { authResponse, TEST_API, testUser } from '../testing';

class MemoryKv implements KeyValueStore {
  data = new Map<string, string>();
  get(k: string) {
    return this.data.get(k) ?? null;
  }
  set(k: string, v: string) {
    this.data.set(k, v);
  }
  remove(k: string) {
    this.data.delete(k);
  }
}

async function signIn(warehouses = testUser().warehouses, kv = new MemoryKv()) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: TEST_API },
      { provide: KEY_VALUE_STORE, useValue: kv },
    ],
  });
  const auth = TestBed.inject(AuthService);
  const ctx = TestBed.inject(WarehouseContextService);
  const p = auth.login('a', 'b');
  TestBed.inject(HttpTestingController)
    .expectOne(`${TEST_API}/auth/login`)
    .flush(authResponse({ user: testUser({ warehouses }) }));
  await p;
  TestBed.tick();
  return { ctx, kv };
}

describe('WarehouseContextService', () => {
  it('uses the only available warehouse without a selection step', async () => {
    const { ctx } = await signIn();
    expect(ctx.current()?.code).toBe('MAIN');
    expect(ctx.needsSelection()).toBe(false);
  });

  it('asks for a selection when several warehouses are available', async () => {
    const { ctx } = await signIn([
      { id: 'w1', code: 'MAIN', name: 'Main' },
      { id: 'w2', code: 'NORTH', name: 'North' },
    ]);
    expect(ctx.needsSelection()).toBe(true);
    expect(ctx.select('w2')).toBe(true);
    expect(ctx.current()?.code).toBe('NORTH');
  });

  it('refuses a warehouse the user is not allowed to use', async () => {
    const { ctx } = await signIn([
      { id: 'w1', code: 'MAIN', name: 'Main' },
      { id: 'w2', code: 'NORTH', name: 'North' },
    ]);
    expect(ctx.select('w-other')).toBe(false);
    expect(ctx.current()).toBeNull();
  });

  it('ignores a stored selection that is no longer allowed', async () => {
    const kv = new MemoryKv();
    kv.set('wh.selectedWarehouseId', 'w-removed');
    const { ctx } = await signIn(
      [
        { id: 'w1', code: 'MAIN', name: 'Main' },
        { id: 'w2', code: 'NORTH', name: 'North' },
      ],
      kv,
    );
    expect(ctx.current()).toBeNull();
  });
});
