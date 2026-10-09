import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap, throwError, catchError } from 'rxjs';
import { API_BASE_URL } from '../core/tokens';
import { AuthService } from '../auth/auth.service';
import { ApiError } from './api-error';
import { IDEMPOTENCY_KEY, SKIP_AUTH } from './context';

function isApiRequest(req: HttpRequest<unknown>, baseUrl: string): boolean {
  return req.url.startsWith(baseUrl);
}

/** Converts every HTTP failure into an {@link ApiError}. Register first (outermost). */
export const apiErrorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((err: unknown) => throwError(() => ApiError.from(err))));

/** Adds the `Idempotency-Key` header when the request context carries one. */
export const idempotencyInterceptor: HttpInterceptorFn = (req, next) => {
  const key = req.context.get(IDEMPOTENCY_KEY);
  return next(key ? req.clone({ setHeaders: { 'Idempotency-Key': key } }) : req);
};

/**
 * Attaches the bearer token to API calls, refreshes an expired token first,
 * and retries once after a 401 if a refresh succeeds.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const baseUrl = inject(API_BASE_URL);
  if (!isApiRequest(req, baseUrl) || req.context.get(SKIP_AUTH)) {
    return next(req);
  }
  const auth = inject(AuthService);

  const withToken = (r: HttpRequest<unknown>) => {
    const token = auth.accessToken();
    return token ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : r;
  };

  const send = () =>
    next(withToken(req)).pipe(
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 401 && auth.accessToken()) {
          return from(auth.refresh()).pipe(
            switchMap((ok) => (ok ? next(withToken(req)) : throwError(() => err))),
          );
        }
        return throwError(() => err);
      }),
    );

  if (auth.isAccessTokenExpiring()) {
    return from(auth.refresh()).pipe(switchMap(() => send()));
  }
  return send();
};

/** Interceptors in the order they must be registered. */
export const whHttpInterceptors = [apiErrorInterceptor, idempotencyInterceptor, authInterceptor];
