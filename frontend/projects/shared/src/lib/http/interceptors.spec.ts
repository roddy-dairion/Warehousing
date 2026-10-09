import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/tokens';
import { AuthService } from '../auth/auth.service';
import { whHttpInterceptors } from './interceptors';
import { ApiError } from './api-error';
import { idempotent } from './context';
import { authResponse, TEST_API } from '../testing';

const flushMicrotasks = () => new Promise((r) => setTimeout(r, 0));

describe('HTTP interceptors', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let auth: AuthService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors(whHttpInterceptors)),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: TEST_API },
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    const p = auth.login('a', 'b');
    ctrl.expectOne(`${TEST_API}/auth/login`).flush(authResponse());
    await p;
  });

  afterEach(() => ctrl.verify());

  it('adds the bearer token to API requests only', async () => {
    const api = firstValueFrom(http.get(`${TEST_API}/products`));
    const other = firstValueFrom(http.get('https://elsewhere.test/x'));
    const apiReq = ctrl.expectOne(`${TEST_API}/products`);
    const otherReq = ctrl.expectOne('https://elsewhere.test/x');
    expect(apiReq.request.headers.get('Authorization')).toBe('Bearer access-1');
    expect(otherReq.request.headers.has('Authorization')).toBe(false);
    apiReq.flush([]);
    otherReq.flush({});
    await Promise.all([api, other]);
  });

  it('adds the Idempotency-Key header when requested', async () => {
    const p = firstValueFrom(http.post(`${TEST_API}/dispatches/1/ship`, {}, { context: idempotent('key-123') }));
    const req = ctrl.expectOne(`${TEST_API}/dispatches/1/ship`);
    expect(req.request.headers.get('Idempotency-Key')).toBe('key-123');
    req.flush({});
    await p;
  });

  it('on 401 refreshes once and retries with the new token', async () => {
    const p = firstValueFrom(http.get<{ ok: boolean }>(`${TEST_API}/products`));
    ctrl.expectOne(`${TEST_API}/products`).flush(null, { status: 401, statusText: 'Unauthorized' });
    await flushMicrotasks();
    ctrl.expectOne(`${TEST_API}/auth/refresh`).flush(authResponse({ accessToken: 'access-2' }));
    await flushMicrotasks();
    const retry = ctrl.expectOne(`${TEST_API}/products`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush({ ok: true });
    expect(await p).toEqual({ ok: true });
  });

  it('maps a network failure to an ApiError of kind "network"', async () => {
    const p = firstValueFrom(http.get(`${TEST_API}/products`));
    ctrl.expectOne(`${TEST_API}/products`).error(new ProgressEvent('error'), { status: 0 });
    const err = await p.catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).kind).toBe('network');
    expect((err as ApiError).userMessage).toContain('Nothing was saved');
  });

  it('maps ProblemDetails validation errors', async () => {
    const p = firstValueFrom(http.post(`${TEST_API}/products`, {}));
    ctrl
      .expectOne(`${TEST_API}/products`)
      .flush({ title: 'Validation failed', errors: { sku: ['SKU already exists'] } }, { status: 400, statusText: 'Bad Request' });
    const err = (await p.catch((e: unknown) => e)) as ApiError;
    expect(err.kind).toBe('validation');
    expect(err.fieldErrors['sku']).toEqual(['SKU already exists']);
  });

  it('maps 409 conflicts with the API detail message', async () => {
    const p = firstValueFrom(http.post(`${TEST_API}/dispatches/1/approve`, {}));
    ctrl
      .expectOne(`${TEST_API}/dispatches/1/approve`)
      .flush({ title: 'Conflict', detail: 'Insufficient stock for SKU-1', code: 'stock.insufficient' }, { status: 409, statusText: 'Conflict' });
    const err = (await p.catch((e: unknown) => e)) as ApiError;
    expect(err.kind).toBe('conflict');
    expect(err.userMessage).toBe('Insufficient stock for SKU-1');
    expect(err.problem.code).toBe('stock.insufficient');
  });
});
