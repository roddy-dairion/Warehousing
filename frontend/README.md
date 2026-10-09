# Frontend workspace

One Angular CLI workspace with three projects:

| Project | What it is | Path |
|---|---|---|
| `shared` | Library used by both apps: auth service, HTTP interceptors, API error model, permissions, warehouse context, guards. Imported as `@wh/shared` (compiled from source, no separate build). | `projects/shared` |
| `web` | Admin and warehouse-management web app (Angular + PrimeNG). | `projects/web` |
| `mobile` | Warehouse staff app (Ionic + Angular + Capacitor). | `projects/mobile` |

## Requirements

* Node.js 22 LTS (≥ 22.12) or 24 LTS
* npm 10+

## Commands

```bash
npm ci                 # install exact locked versions
npm start              # web app on http://localhost:4200 (proxies /api to http://localhost:5080)
npm run start:mobile   # mobile app in the browser on http://localhost:4201
npm test               # unit tests for shared, web and mobile (Vitest)
npm run lint:types     # strict TypeScript check of both apps
npm run build          # production builds into dist/web and dist/mobile
```

Native mobile builds (Phase 7): `npx cap add android` / `npx cap add ios`, then `npm run build:mobile`. Native projects are created under `projects/mobile/android` and `projects/mobile/ios`. iOS builds need a Mac with Xcode.

## Key decisions

* **Angular 21 + PrimeNG 21.1.10 (MIT).** PrimeNG 22 moved to a commercial "PrimeUI" licence that needs a licence key, so we stay on the last MIT release. Do not upgrade PrimeNG past 21.x without a licence decision.
* **Auth:** access token in memory only. Web uses an HttpOnly refresh cookie; mobile keeps the refresh token in the iOS Keychain / Android Keystore (`@aparajita/capacitor-secure-storage`).
* **Errors:** every HTTP failure becomes an `ApiError` (ProblemDetails). A network failure says that nothing was saved.
* **Idempotency:** stock-changing calls pass `context: idempotent(key)` so retries are recognised by the API.
* **Permissions in the UI are for navigation only.** The API enforces every rule.
* The web app is zoneless. The mobile app keeps zone.js because Ionic relies on NgZone.
