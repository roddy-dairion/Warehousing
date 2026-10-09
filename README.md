# Warehousing — Inventory & Dispatch Management

Web app (Angular + PrimeNG), warehouse mobile app (Ionic + Capacitor), ASP.NET Core API, PostgreSQL.

* Project brief: [`Claude.md`](Claude.md)
* Architecture & design (Phase 1): [`docs/`](docs)
  1. [Architecture](docs/01-architecture.md)
  2. [Data model](docs/02-data-model.md)
  3. [Inventory rules & dispatch workflow](docs/03-inventory-and-dispatch.md)
  4. [Plan & open questions](docs/04-plan-and-open-questions.md)
  5. [Auth API contract](docs/05-auth-api-contract.md)

## Layout

| Folder | Contents |
|---|---|
| `frontend/` | Angular workspace: `shared` library, `web` app (PrimeNG), `mobile` app (Ionic + Capacitor). See [frontend/README.md](frontend/README.md). |
| `backend/` | ASP.NET Core 10 solution (Phase 2, pending). |
| `docs/` | Architecture and design documents. |

## Status

* Phase 1 (architecture): done.
* Phase 2 (foundation): frontend done (login, app shells, auth, guards, tests). Backend pending.
