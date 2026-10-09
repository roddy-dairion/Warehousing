import { HttpContext, HttpContextToken } from '@angular/common/http';

/** Request must not carry the bearer token or trigger a refresh (login/refresh/logout calls). */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

/** Idempotency key sent as `Idempotency-Key` for stock-changing commands. */
export const IDEMPOTENCY_KEY = new HttpContextToken<string | null>(() => null);

export function newIdempotencyKey(): string {
  return globalThis.crypto.randomUUID();
}

/**
 * Context for a stock-changing command. Generate the key ONCE per user action and reuse it
 * when retrying the same action, so the API can recognise the retry.
 */
export function idempotent(key: string = newIdempotencyKey(), context: HttpContext = new HttpContext()): HttpContext {
  return context.set(IDEMPOTENCY_KEY, key);
}

export function skipAuth(context: HttpContext = new HttpContext()): HttpContext {
  return context.set(SKIP_AUTH, true);
}
