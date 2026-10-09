# Warehousing — Inventory & Dispatch Management

Web app (Angular + PrimeNG), warehouse mobile app (Ionic + Capacitor), ASP.NET Core API, PostgreSQL.

* Project brief: [`Claude.md`](Claude.md)
* Architecture & design (Phase 1): [`docs/`](docs)
  1. [Architecture](docs/01-architecture.md)
  2. [Data model](docs/02-data-model.md)
  3. [Inventory rules & dispatch workflow](docs/03-inventory-and-dispatch.md)
  4. [Plan & open questions](docs/04-plan-and-open-questions.md)
  5. [Auth API contract](docs/05-auth-api-contract.md)

## Quick start (development)

```powershell
docker compose -f deploy/docker-compose.dev.yml up -d   # PostgreSQL
cd backend; dotnet run --project src/Inventory.Api --launch-profile http   # API on :5080 (see backend/README.md for secrets)
cd frontend; npm ci; npm start                         # web app on :4200
```

## Layout

| Folder | Contents |
|---|---|
| `frontend/` | Angular workspace: `shared` library, `web` app (PrimeNG), `mobile` app (Ionic + Capacitor). See [frontend/README.md](frontend/README.md). |
| `backend/` | ASP.NET Core 10 API, EF Core + PostgreSQL. See [backend/README.md](backend/README.md). |
| `deploy/` | `docker-compose.dev.yml`: local PostgreSQL 17 for development and tests. |
| `docs/` | Architecture and design documents. |

## Status

* Phase 1 (architecture): done.
* Phase 2 (foundation): done. Login, refresh, logout, permissions and warehouse access, web and mobile shells, 64 automated tests.
* Next: Phase 3 (master data: users, warehouses and bins, customers, products, barcodes).
