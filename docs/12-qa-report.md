# 12 — Quality assurance report (Phase 8)

Date: **2026-10-12**. All results below were produced by running the tests in this repository on that date (PostgreSQL 16, .NET 10, Node 22, Chromium). Items that need real phones are listed separately and are **not** marked as passed.

## 1. Summary

| Area | Result |
|---|---|
| Backend tests (`dotnet test`, real PostgreSQL) | **198 passed**, 0 failed (178 before Phase 8, plus 20 new) |
| Frontend unit tests (`npm test`) | **52 passed** (shared 21, web 16, mobile 15) |
| Builds | `ng build web`, `ng build mobile`, `dotnet build` (0 warnings), `lint:types`: all succeed |
| Browser walkthroughs (Playwright, Chromium) | `web_smoke.py`, `mobile_smoke.py`, `mobile_new_barcode.py`: **all passed** on empty databases |
| Dependency audit | NuGet: **no known vulnerabilities**. npm runtime packages: **0**. npm dev tools: 3 moderate (see §5) |
| Security fixes made in this phase | 4 (see §4) |
| Physical devices | iPhone: the app was installed (Codemagic and Sideloadly) and signed in to the API over Wi-Fi. **Scanning checklist still open** (§6) |

## 2. What the new Phase 8 tests check

### Authorization matrix: `EndpointSecurityTests`

The tests read **every API endpoint (130) from the routing table**, so endpoints added later are checked automatically.

* **Anonymous access:** only `/health`, login, refresh and logout. A new anonymous endpoint fails the build until it has been reviewed.
* **Permission required:** every other endpoint needs a permission, except six reviewed "any signed-in user" endpoints, which scope their own data:
  * own profile (`auth/me`);
  * own warehouses (`warehouses`, `warehouses/{id}`);
  * dashboard, with sections filtered by permission (`dashboard/warehouse`);
  * imports (`imports/{kind}`, `imports/{kind}/template`), which check the permission per import kind in code.
* **No token:** every protected endpoint answers 401.
* **Role matrix:** for the Operator, Manager and Admin roles, each endpoint is called and must return 403 **exactly** when the role lacks the permission, and must not return 403 when it has it. 3 roles × 120 permission-protected endpoints, all as expected.

### Warehouse (branch) isolation: `WarehouseIsolationTests`

A manager assigned only to MAIN has nearly every permission, so only the branch scope can refuse. Against another branch, that manager gets **404/403 for 22 reads**:
* documents: receipts, adjustments, bin moves and dispatches, including the pick list, documents and proof-of-delivery files;
* stock: balances, summary, movements, expiring lots and the reconciliation check;
* reports, including the CSV download, and the dashboard;
* warehouse details, bins and bin lists.

The same manager gets **404/403 for 13 changes**: creating receipts, posting, cancelling and reversing them, adjustments and their approval, bin moves, dispatch creation, submission, cancellation and picking, and creating bins. Afterwards the other branch's data is checked unchanged. The product stock view only lists the branches the user may see.

### Concurrency soak: `StockSoakTests`

Two rounds of simultaneous requests, then the stock accounting is checked:

* **Round 1, all at once:**
  * 12 dispatch approvals competing for the same stock (demand is about 130% of supply on purpose);
  * 3 adjustment approvals;
  * 3 receipts each posted twice with the same idempotency key (a retried request);
  * 3 bin moves.
* **Round 2, all at once:** picking and shipping half of the approved dispatches (each shipment sent twice in parallel), and cancelling the other half.
* **After each round:**
  * the reconciliation check finds **no difference** between balances, the ledger and open reservations;
  * no balance is negative;
  * reserved never exceeds on hand, and available equals on hand minus reserved.
* **At the end:**
  * on hand equals the starting stock, plus receipts, plus adjustments, minus shipments;
  * nothing is left reserved;
  * some approvals were refused for shortage, as expected.

The soak test, together with the existing concurrency tests (competing approvals, concurrent shipments, concurrent receipt posts, concurrent reservations), was run **5 times in a row: all passed**.

### Hardening: `SecurityHardeningTests`

* **Response headers:** every response carries `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` and `Content-Security-Policy: default-src 'none'`. API data responses also carry `Cache-Control: no-store`.
* **CORS:** a configured origin passes the preflight; any other origin gets no CORS headers.
* **Report CSV:** cells can't run as Excel formulas (8 cases, plus an end-to-end check with a product named `=1+2`).

### Mobile: online only

A pick that fails because the connection dropped is **not retried or queued** by the app, even after 5 minutes. Nothing is written to storage. The pick waits for the user to tap again, with the same scan ID, so it can't count twice. The only things the app stores are the refresh token (Keychain/Keystore) and the selected warehouse.

### Already covered by earlier phases, re-run

The earlier tests still pass:
* partial shipments (close short, backorder);
* cancellation, which releases reservations, while shipped dispatches can't be cancelled;
* invalid state transitions;
* idempotency: replay, a key reused for a different request is refused (422), a request already in progress (409);
* repeated scans;
* wrong barcode, bin or lot;
* login: lockout, refresh-token rotation and reuse detection, generic error;
* rate limiting;
* the stock ledger is append-only, and the audit log can't be changed.

## 3. Security review checklist

| Topic | Status |
|---|---|
| Passwords | ASP.NET Identity hashing (PBKDF2); 10+ characters with upper and lower case and digits; lockout after 5 failures for 15 min |
| Tokens | 15-minute JWT (HS256, issuer/audience/algorithm pinned, 30 s clock skew). Refresh tokens rotate; reusing an old one revokes the whole family. Web: refresh token in an HttpOnly, Secure, SameSite=Strict cookie limited to `/api/v1/auth`. Mobile: refresh token in Keychain/Keystore |
| Authorization | Fallback policy: everything needs sign-in. Permissions on every endpoint (§2). Branch scope checked on the server; IDs from the client are never trusted |
| Input | FluentValidation on every request; EF Core parameters; the 4 raw SQL statements use parameters, and the one table name comes from a whitelist |
| Uploads | Size limits per endpoint. Image type is detected from the file bytes (JPEG/PNG/WebP), not trusted from the name. CSV imports are limited to 5 MB |
| PDFs and templates | Liquid templates with HTML encoding. Chromium renders with JavaScript off and network requests blocked. Editing templates needs `settings.manage` |
| Errors and logs | ProblemDetails without stack traces. Request logs include the path only, never the query or body. Login audit has no secrets (tested) |
| Transport | HTTPS redirect and HSTS outside development. Forwarded headers are trusted only from configured proxies |
| CORS | Only configured origins (none by default; Capacitor origins in development) |
| Rate limits | Login 10/min and refresh 60/min per client (tested) |
| Secrets | None in the repository (checked). JWT key via user-secrets or an environment variable; the API now refuses to start without a key of at least 32 bytes. SMTP password in secrets |
| Mobile | No offline queue. Plain HTTP only in debug/test builds (`CAP_DEV`). Android backup now off |

## 4. Fixes made in this phase

| # | Finding | Fix | Test |
|---|---|---|---|
| 1 | API responses had no defensive headers (only product images had `nosniff`) | Middleware adding `nosniff`, `DENY` framing, `no-referrer`, a `default-src 'none'` CSP, and `no-store` on API data | `SecurityHardeningTests` |
| 2 | **CSV formula injection.** A product name or note starting with `=`, `+`, `-` or `@` would run as a formula when a report CSV is opened in Excel | Such cells get a leading `'`; numbers like `-24` are left alone | `SecurityHardeningTests` (unit and end-to-end) |
| 3 | A missing or short JWT signing key only failed at the first sign-in (500) | The API refuses to start, with a clear message | Start-up check |
| 4 | Android `allowBackup="true"` allowed app data in device backups | `allowBackup="false"` | Manifest |

## 5. Accepted and deferred items

| Item | Decision |
|---|---|
| npm audit: `uuid` (moderate) used by `xcode` ← `@capacitor/cli` | **Accepted.** These are build-time tools on the developer's machine or CI and are not shipped in the app. The suggested fix downgrades the Capacitor CLI to a version that doesn't match the rest of Capacitor. To be revisited when a Capacitor update fixes it |
| Upload endpoints answer 415 (wrong content type) before 403 for users without permission | Accepted. Nothing runs, and no data is revealed |
| Products, units and categories are shared by all branches | By design (Q1: one company owns all stock). Stock, documents and reports are scoped per branch |
| Swagger UI and the OpenAPI document | Development only |
| `AllowedHosts: *`, production CORS origin, reverse proxy IPs, TLS certificates, security headers on the web app's own host | **Phase 9** (deployment configuration) |
| Database backups, restore drill, log retention, monitoring and alerts | **Phase 9** |
| Penetration test by a third party | Recommended before go-live, outside this project's scope |

## 6. Still to test on real devices (not passed yet)

| Check | Status |
|---|---|
| Install and sign-in on iPhone (Codemagic `ios-test-unsigned`, Sideloadly) | **Done by the user on 2026-10-12**: the app runs and reaches the API over Wi-Fi |
| Camera scan of real EAN-13, carton and bin labels under warehouse lighting, with the torch | Open |
| Camera permission refused, then allowed in settings | Open |
| Delivery photos from the camera and a finger-drawn signature on the phone | Open |
| Wi-Fi off during a pick, then retry (no double count) | Open (covered by unit and API tests, not yet by hand) |
| Android phone or rugged scanner: install the APK (`android-test`); the built-in scanner in keyboard mode with Enter | Open |
| Gloves, screen rotation during a signature | Open |

## 7. How to re-run

```bash
# Backend (needs PostgreSQL; INVENTORY_TEST_DB or the docker-compose database on 55432)
cd backend && dotnet test
# Frontend
cd frontend && npm test && npm run lint:types && npm run build
# Browser walkthroughs (empty database, API on :5080, ng serve web :4200 / mobile :4201)
python tools/e2e/web_smoke.py
python tools/e2e/mobile_smoke.py
python tools/e2e/mobile_new_barcode.py
# Dependency audit
cd backend && dotnet list package --vulnerable --include-transitive
cd frontend && npm audit --omit=dev
```
