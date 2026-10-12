# HelloCherie Warehousing — Inventory & Dispatch Management

Web app (Angular + PrimeNG), warehouse mobile app (Ionic + Capacitor), ASP.NET Core API, PostgreSQL.

* Project brief: [`Claude.md`](Claude.md)
* Architecture & design (Phase 1): [`docs/`](docs)
  1. [Architecture](docs/01-architecture.md)
  2. [Data model](docs/02-data-model.md)
  3. [Inventory rules & dispatch workflow](docs/03-inventory-and-dispatch.md)
  4. [Plan & open questions](docs/04-plan-and-open-questions.md)
  5. [Auth API contract](docs/05-auth-api-contract.md)
  6. [Master data (Phase 3)](docs/06-master-data.md)
  7. [Inventory engine (Phase 4)](docs/07-inventory-engine.md)
  8. [Dispatch engine (Phase 5)](docs/08-dispatch-engine.md)
  9. [Web interface (Phase 6)](docs/09-web-interface.md)

## Local development

Only PostgreSQL runs in Docker. The API and the Angular apps run directly from Visual Studio / VS Code.

1. **Database:** `docker compose -f deploy/docker-compose.dev.yml up -d` (PostgreSQL 17 on host port 55432 (avoids clashes with other local PostgreSQL instances), dev database `inventory_dev`).
2. **Secrets (once):** see [backend/README.md](backend/README.md#first-run). In Visual Studio: right-click *Inventory.Api* → *Manage User Secrets*.
3. **API:**
   * Visual Studio 2022 (17.14+) / 2026: open `backend/Inventory.slnx`, start *Inventory.Api* with the **http** profile (port 5080).
   * VS Code: open the repo folder, run **API (.NET)** (F5), or the compound **API + Web app**.
4. **Web app:** `cd frontend`, `npm ci` (once), then `npm start`, or VS Code **Web app (Chrome)**. Open http://localhost:4200. `/api` is proxied to port 5080.
5. **Tests:** Visual Studio Test Explorer, or `dotnet test` in `backend/` and `npm test` in `frontend/`.

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
* Phase 3 (master data): done. Users, warehouses and bins, customers, products with pack sizes, barcodes and images, CSV import.
* Phase 4 (inventory engine): done (backend). Stock balances by bin and lot, append-only ledger, goods receipts, adjustments with approval, bin moves, FEFO reservations, idempotency, reconciliation. 141 backend tests.
* Phase 5 (dispatch engine): done (backend). Dispatch workflow with approval and reservation, barcode-checked picking, close-short or backorder shipping, proof of delivery (photos, name, signature), packing slip and signed delivery note PDFs from editable templates, delivery note emailed to the customer. 170 backend tests.
* Phase 6 (web interface): done. Dashboard, stock, receipts, adjustments, bin moves, the full dispatch workflow (scanner picking, shipping, delivery with photos and signature), reports with CSV, audit log, company settings and template editor. 178 backend and 42 frontend tests; browser smoke test in `tools/e2e/web_smoke.py`.
* Next: Phase 7 (warehouse mobile app).
