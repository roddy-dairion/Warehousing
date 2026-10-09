# 05 — Auth API Contract

Status: **Implemented on the frontend (`@wh/shared`); the backend must match it.**

All routes are under `/api/v1/auth`. JSON uses camelCase, timestamps are ISO-8601 UTC, and errors use ProblemDetails (RFC 9457) with an optional `code`.

## POST /auth/login

Request:
```json
{ "email": "op@company.mu", "password": "…", "client": "web" | "mobile" }
```

200 response (`AuthResponse`):
```json
{
  "accessToken": "<JWT, ~15 min>",
  "accessTokenExpiresAt": "2026-10-09T14:30:00Z",
  "refreshToken": "<only when client = mobile>",
  "user": {
    "id": "uuid",
    "email": "op@company.mu",
    "displayName": "Anil R.",
    "roles": ["WH_OPERATOR"],
    "permissions": ["inventory.read", "dispatches.pick"],
    "allWarehouses": false,
    "warehouses": [{ "id": "uuid", "code": "MAIN", "name": "Main warehouse" }]
  }
}
```

* For `client = web`: the API sets the refresh token as a cookie `wh_rt` (`HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`). `refreshToken` in the body is null.
* For `client = mobile`: the refresh token is returned in the body and never set as a cookie.
* Wrong credentials return **401**, and the response does not say which field was wrong. Too many attempts return **429** (rate limited per IP and per email). A locked account also returns 401, with a generic message.
* `warehouses` holds the active warehouses the user may act in. For administrators (`allWarehouses = true`) it is every active warehouse.

## POST /auth/refresh

Request body: `{ "refreshToken": "<mobile only, null for web>" }`; web sends the cookie.

200 returns an `AuthResponse` with a **rotated** refresh token; the old one is revoked. Reusing a revoked token revokes the whole token family (theft detection) and returns 401.

## POST /auth/logout

Request body: `{ "refreshToken": "<mobile only>" }`. The API revokes the token, clears the cookie and returns 204. It is idempotent.

## GET /auth/me

Requires a bearer token. Returns the `user` object above, for screens that need fresh permissions.

## Rules for all other endpoints

* `Authorization: Bearer <accessToken>`.
* Stock-changing commands accept `Idempotency-Key: <uuid>`. Replaying the same key and body returns the stored response; the same key with a different body returns 422.
* The frontend treats **401** as "try one refresh, then sign out", **403** as "no permission", **409** as a business conflict whose `detail` is shown to the user, and **400/422** with `errors` as field validation.
* Permission codes are listed in `projects/shared/src/lib/auth/permissions.ts` and in the backend `Permissions` class; the two lists must stay identical.
