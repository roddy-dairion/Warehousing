import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../core/tokens';
import { whHttpInterceptors } from '../http/interceptors';
import { TEST_API } from '../testing';
import { DispatchesApi, InventoryApi, ReceiptsApi, ReportingApi } from './operations.api';

describe('operations API clients', () => {
  let ctrl: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors(whHttpInterceptors)),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: TEST_API },
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
  });

  afterEach(() => ctrl.verify());

  it('sends list filters as query parameters and skips empty ones', async () => {
    const p = TestBed.inject(InventoryApi).balances({ warehouseId: 'w1', productId: 'p1', search: '', onlyWithStock: false, page: 2 });
    const req = ctrl.expectOne((r) => r.url === `${TEST_API}/inventory/balances`);
    expect(req.request.params.get('warehouseId')).toBe('w1');
    expect(req.request.params.get('productId')).toBe('p1');
    expect(req.request.params.get('onlyWithStock')).toBe('false');
    expect(req.request.params.has('search')).toBe(false);
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [], page: 2, pageSize: 25, totalCount: 0 });
    await p;
  });

  it('posts receipts with an idempotency key that callers can reuse on retry', async () => {
    const api = TestBed.inject(ReceiptsApi);
    const first = api.postReceipt('r1', { key: 'k-1' });
    const req = ctrl.expectOne(`${TEST_API}/receipts/r1/post`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('k-1');
    req.flush({});
    await first;

    const generated = api.postReceipt('r1');
    const req2 = ctrl.expectOne(`${TEST_API}/receipts/r1/post`);
    expect(req2.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    req2.flush({});
    await generated;
  });

  it('ships with the short decision and an idempotency key', async () => {
    const p = TestBed.inject(DispatchesApi).ship('d1', { carrier: 'Van', trackingNumber: null, shortDecision: 'backorder' }, { key: 'ship-1' });
    const req = ctrl.expectOne(`${TEST_API}/dispatches/d1/ship`);
    expect(req.request.body).toEqual({ carrier: 'Van', trackingNumber: null, shortDecision: 'backorder' });
    expect(req.request.headers.get('Idempotency-Key')).toBe('ship-1');
    req.flush({});
    await p;
  });

  it('sends proof of delivery as multipart form data', async () => {
    const signature = new Blob(['png'], { type: 'image/png' });
    const photo = new File(['jpg'], 'goods.jpg', { type: 'image/jpeg' });
    const p = TestBed.inject(DispatchesApi).deliver('d1', { receiverName: 'John', notes: 'Back door', signature, photos: [photo] });
    const req = ctrl.expectOne(`${TEST_API}/dispatches/d1/deliver`);
    const form = req.request.body as FormData;
    expect(form.get('receiverName')).toBe('John');
    expect(form.get('notes')).toBe('Back door');
    expect(form.get('signature')).toBeInstanceOf(Blob);
    expect(form.getAll('photos')).toHaveLength(1);
    expect(form.has('deliveredAt')).toBe(false);
    req.flush({});
    await p;
  });

  it('downloads report CSVs as blobs with the filters', async () => {
    const p = TestBed.inject(ReportingApi).reportCsv('movements', { warehouseId: 'w1', from: '2026-10-01', to: '2026-10-12' });
    const req = ctrl.expectOne((r) => r.url === `${TEST_API}/reports/movements/csv`);
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('from')).toBe('2026-10-01');
    req.flush(new Blob(['a,b']));
    expect(await p).toBeInstanceOf(Blob);
  });
});
