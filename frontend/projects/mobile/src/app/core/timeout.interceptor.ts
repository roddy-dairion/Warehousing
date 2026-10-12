import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError, timeout, TimeoutError } from 'rxjs';

/** Normal calls give up after 20 s; uploads (delivery photos) get 90 s on slow mobile data. */
export const REQUEST_TIMEOUT_MS = 20_000;
export const UPLOAD_TIMEOUT_MS = 90_000;

/**
 * Without a limit, a request to an unreachable server (wrong Wi-Fi, PC asleep) can leave a spinner turning for minutes.
 * A timeout is reported like a lost connection (status 0), so the usual "cannot reach the server" message appears and
 * stock actions keep their idempotency key / scan id for a safe retry.
 */
export const timeoutInterceptor: HttpInterceptorFn = (req, next) => {
  const limit = req.body instanceof FormData ? UPLOAD_TIMEOUT_MS : REQUEST_TIMEOUT_MS;
  return next(req).pipe(
    timeout(limit),
    catchError((err: unknown) =>
      throwError(() => (err instanceof TimeoutError ? new HttpErrorResponse({ status: 0, statusText: 'Timeout', url: req.url }) : err)),
    ),
  );
};
