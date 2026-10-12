# 04 — Implementation Plan and Open Business Questions

Status: **Draft for review (Phase 1)**

## Part A — Open business questions

**Decided 2026-10-09:**
* Q1: the warehouse company owns all stock.
* Q2: no customer portal in v1.
* Q3: lots **and** expiry dates are in v1.
* Q4: bin locations are in v1.
* Q5: partial shipments allowed; the shipper chooses close-short or backorder.
* Q9: pack units (e.g. carton = 12) are in v1.
* **Decided 2026-10-12 (Phase 5):**
  * Q13 proof of delivery: **photos, receiver name and a signature captured on the phone or tablet** are all required.
  * Q14 documents: an unsigned **packing slip** is printed at shipment. Once proof of delivery is recorded, a **signed delivery note PDF** (signature + receiver name; photos stay on the dispatch, not in the PDF) is **emailed to the customer** and kept on the dispatch for download.
  * PDF layout is a **template editable in the admin screens** (logo, company details, header/footer, HTML with placeholders, preview before saving).
  * Delivery notes are sent from **dispatch@hellocherie.ch** (Microsoft 365, SMTP; password kept in secrets).
  * Phase 5 defaults (see doc 08): approval required and the creator/submitter cannot approve (Q6); picking assignment optional, any authorised operator may pick (Q7).
* Phase 4 defaults (change any time, see doc 07): the adjustment submitter cannot approve it (Q6/Q8: operators request, managers approve all); minimum remaining shelf life 0 days (Q20); picked goods stay on their bin until shipped (Q19).
* Phase 3 defaults (change any time): CSV import with a downloadable template; barcodes of any format, optional; bins can be imported and carry a label barcode; one optional image per product.

The remaining questions can be decided by the phase noted in Part B.

| # | Question | Why it matters | Proposed default |
|---|---|---|---|
| **Q1** | **Who owns the stock?** Does the warehouse company own and sell it (distributor), or does it store goods on behalf of customers (3PL), where Customer A's stock may only go to Customer A? | Decides whether `owner_customer_id` is part of every balance, reservation and receipt line. | Own stock (no owner column), but keep the nullable column ready |
| **Q2** | Do customers need a **login** (read-only portal for their dispatches/documents/stock) in v1? | Adds a Customer-user role and customer-scoped data isolation (the "Client Administrator" in the project brief). | No portal in v1; design allows it later |
| **Q3** | **Batch/lot and expiry tracking** needed for any products? | Adds lot to the balance key, FEFO picking, lot capture at receipt. | Not in v1; flag per product reserved |
| **Q4** | **Bin/shelf locations** needed, or stock per warehouse only? | Adds location to the balance key, put-away and pick-path. | Warehouse-level only in v1 |
| **Q5** | **Partial shipments:** allowed? If yes, *close short* vs *backorder* (see doc 03 §B2)? | Shapes ship logic and reports. | Allowed; shipper chooses close or backorder |
| Q6 | Is dispatch **approval** always required? Must the approver be a different person from the creator? | Segregation of duties. | Always required; different person (configurable) |
| Q7 | Is picking **assigned** to specific operators, or does anyone pick any approved dispatch? | Need for `DispatchPickingTask`. | Optional assignment; any authorized operator may pick |
| Q8 | Which **adjustments** need manager approval (all, or above a quantity/value)? Can operators request them? | Adjustment workflow. | Operators request; managers approve all |
| Q9 | **Units:** one unit per product, or packs/cartons (e.g. 1 carton = 12 units)? Decimal quantities (kg, m)? | UoM conversions and quantity precision. | One base unit per product; decimals per unit |
| Q10 | **Devices:** ordinary Android/iPhone phones, or rugged scanners (Zebra/Honeywell)? Which barcode types are on your products (EAN-13, Code 128, QR, GS1)? Do all products already carry barcodes, or must the system print labels? | Library verification; label printing scope. | Phones with camera + wedge support; no label printing in v1 |
| Q11 | Do receipts come from **suppliers** you need to record? Are receipts expected in advance (purchase orders/ASN)? | Supplier entity; expected vs received. | Optional supplier + reference; no PO module |
| Q12 | **Login:** local accounts in the app, or **Microsoft 365 / Entra ID** sign-in? | Identity design. | Local accounts (JWT); Entra later if wanted |
| Q13 | **Proof of delivery:** who records it (driver, office)? Signature, photo, or the signed delivery note scanned? | POD screen and storage. | Office/driver uploads photo/PDF + receiver name |
| Q14 | **Documents:** must the system produce a printable **delivery note / packing list** PDF? | PDF generation in Phase 5/6. | Yes, simple delivery note PDF |
| Q15 | Is any **price/value** tracked (stock valuation, adjustment value thresholds)? | Money columns, valuation reports. | No financial values in v1 |
| Q16 | UI **language(s):** English, French, both? | i18n set-up from the start is cheaper. | English, i18n-ready |
| Q17 | **Hosting:** where will the API and DB run (your own Linux servers, Azure, other)? Is a Mac available for iOS builds? | Deployment and CI design. | Linux VM + Docker; Mac/cloud Mac for iOS |
| Q19 | Should picked goods be **moved to a dispatch dock location** in the system, or stay on their bin as "reserved" until shipped? | Accuracy of bin counts during picking. | Stay on bin until shipped (v1) |
| Q20 | **Minimum remaining shelf life** at dispatch (e.g. ≥ 30 days)? One global value or per customer/product? | FEFO allocation filter. | Global setting, default 0 days |
| Q21 | How many **bins** roughly, and do bin labels with barcodes exist already? | Location seed/import, label printing. | CSV import of locations; labels printed outside the app |
| Q22 | **Mobile app identity:** app name and bundle ID (e.g. `mu.<company>.warehouse`). The ID is permanent once published to the stores. | Store publishing. | Placeholder `com.warehousing.mobile` until confirmed |
| Q18 | Existing **data to import** (products, barcodes, customers, opening stock)? Format? | Import tools; opening-balance movement type. | CSV import for products/customers + opening stock adjustment |

## Part B — Phased plan

| Phase | Scope | Output / exit criteria |
|---|---|---|
| **1. Architecture** ✅ draft | These four documents | Reviewed; Q1–Q5 answered |
| **2. Foundation** ✅ 2026-10-09 | `git init`; .NET 10 solution (6 projects); EF Core + Npgsql; docker-compose PostgreSQL; Identity + JWT + refresh tokens; permissions and warehouse access; ProblemDetails, Serilog, rate limiting, Swagger; Angular workspace (shared/web/mobile) with login + shell; CI build/test scripts | `dotnet build/test` and `ng build/test` pass; login works on web; migration `Initial` |
| **3. Master data** ✅ 2026-10-09 | Warehouses + bin locations, users/roles/access, customers + addresses, categories, units + product pack units, products, barcodes, lots; CSV import (products, locations, customers) | CRUD APIs + web screens; authorization tests (warehouse access, permission denials) |
| **4. Inventory engine** ✅ 2026-10-11 (backend; screens in Phase 6) | Balances (bin × lot), append-only ledger (triggers), receipts with lot/expiry (draft/post/reverse), put-away and bin moves, adjustments with approval, FEFO reservation allocator, expiry alerts, idempotency middleware, reconciliation job | Integration tests #1, #6, #8, #10 from `Claude.md` §16 on real PostgreSQL, including parallel-request tests |
| **5. Dispatch engine** ✅ 2026-10-12 (backend; screens in Phases 6–7) | Dispatch CRUD, state machine, approve/reserve, pick + barcode validation, discrepancies, ready, ship (close/backorder), deliver + POD, cancel, history, delivery-note PDF | Integration tests #2–#5, #7, #9 incl. concurrent ship / double-submit |
| **6. Web UI** ✅ 2026-10-12 (see doc 09) | Dashboards, inventory, movements, receipts, adjustments, dispatch approval/picking/shipping, reports, audit views | Vitest unit tests; Playwright smoke tests |
| **7. Mobile app** | Spike: scanner on real devices **first**. Then login, warehouse select, search/scan, stock lookup, picking, receiving, ship confirm, recent activity | Android + iOS builds; physical-device scan test report |
| **8. QA** | Full test run; security review (authz matrix, upload, CORS); concurrency soak test; offline behaviour check | Test report with actual results |
| **9. Deployment** | Prod config & secrets (incl. SMTP password for dispatch@hellocherie.ch), Chromium on the server for PDFs, migrations, backups/restore drill, HTTPS, monitoring, app-store signing/distribution, handover guide | Runbook; no production deploy without explicit approval |

Each phase ends with: build + tests run, docs updated, and a short report of what was done and what remains.

## Part C — Immediate next steps (Phase 2 kick-off, after review)

1. Answer Q12 (login method) and Q17 (hosting, Mac for iOS) if known. Neither blocks the start of Phase 2.
2. Decide the repo location (recommend moving out of OneDrive) and create the GitHub repository if wanted.
3. Install on the dev machine: .NET 10 SDK, Node 22 LTS, Docker Desktop (or PostgreSQL 17), Android Studio.
