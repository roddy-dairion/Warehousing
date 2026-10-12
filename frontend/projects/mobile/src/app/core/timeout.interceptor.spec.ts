import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { apiErrorInterceptor, ApiError } from '@wh/shared';
import { REQUEST_TIMEOUT_MS, timeoutInterceptor } from './timeout.interceptor';

describe('timeoutInterceptor', () => {
  it('turns a request that never answers into a network error', async () => {
    vi.useFakeTimers();
    try {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(withInterceptors([apiErrorInterceptor, timeoutInterceptor])), provideHttpClientTesting()],
      });
      const result = firstValueFrom(TestBed.inject(HttpClient).get('/api/v1/health')).catch((e: unknown) => e);
      TestBed.inject(HttpTestingController).expectOne('/api/v1/health');
      await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS + 10);
      const err = (await result) as ApiError;
      expect(err).toBeInstanceOf(ApiError);
      expect(err.kind).toBe('network');
    } finally {
      vi.useRealTimers();
    }
  });
});
