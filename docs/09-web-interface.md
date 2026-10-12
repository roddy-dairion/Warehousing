# 09 — Web interface (Phase 6)

Status: **Done 2026-10-12.** Angular 21 + PrimeNG 21 screens for every inventory and dispatch workflow built in Phases 4–5, plus dashboards, reports and the audit log. The mobile app follows in Phase 7.

## Screens

| Route | Screen | Permission (menu + route guard; the API enforces it again) |
|---|---|---|
| `/` | **Dashboard** for the selected warehouse: dispatch counts by stage, "needs your attention" list, stock figures (on hand, available, reserved, low stock, expiring lots), draft receipts, adjustments awaiting approval, recent movements, failed documents/emails | any signed-in user (each section is only returned when the user holds its permission) |
| `/dispatches` | List with status, customer, date filters and server-side paging | `dispatches.read` |
| `/dispatches/new`, `/:id/edit` | Draft editor: customer autocomplete, delivery address (customer's default or another), product lines (base unit or pack), **Save** / **Save and submit** | `dispatches.create` |
| `/dispatches/:id` | Detail and every workflow command: submit, return to draft, approve (shows shortages), start picking, **scanner box** (bin label → product/pack barcode → lot → qty), pick per task, pick from another bin, report a problem, undo recent picks, picking done (partial confirm), ship (carrier, tracking, close short / backorder), **record delivery** (receiver name, photos from camera or file, signature drawn with finger/pen/mouse), cancel, documents and emails (open, download, regenerate, re-send delivery note), proof-of-delivery images, history timeline | read: `dispatches.read`; each button follows the server's `actions` flags |
| `/receipts`, `/receipts/:id` (`new`) | Goods receipt list and editor: lines with pack unit, expected qty, bin, lot, expiry; save draft, post (confirm), reverse with reason, cancel draft | `inventory.receive` or `inventory.receive.post` |
| `/inventory` | **Stock**: tabs By product (on hand / reserved / available / sellable, low-stock filter), By bin and lot, Movements (ledger with filters), Expiring, Check (reconciliation). A product drawer shows bins, lots and lets approvers change a lot status | `inventory.read` |
| `/adjustments`, `/adjustments/:id` | Stock adjustments with reason; submit, approve / reject (approver ≠ submitter) | `inventory.adjust` |
| `/transfers` | Bin moves: pick the source balance, destination bin, quantity; detail drawer | `inventory.transfer` |
| `/reports` | 8 reports (stock on hand, stock summary, movements, goods received, adjustments, transfers, dispatches, short shipments and discrepancies) with date range; preview up to 500 rows, CSV download up to 50 000 rows | `reports.read` |
| `/audit` | Audit log: search, area filter (action prefix), date range, details dialog with the JSON payload | `audit.read` |
| `/settings/company` | Company details printed on documents, logo upload, time zone, delivery-note email subject/body | `settings.manage` |
| `/settings/templates` | Template editor per document type: versions, PDF preview with sample data, save as new version, restore an old version, placeholder chips | `settings.manage` |

Master-data screens (products, customers, warehouses and bins, categories and units, users, import) are from Phase 3.

## Backend added in this phase

* `GET /api/v1/dashboard/warehouse?warehouseId=` — sections filtered by permission (`DashboardService`).
* `GET /api/v1/reports`, `GET /api/v1/reports/{kind}?warehouseId=&from=&to=`, `GET /api/v1/reports/{kind}/csv` — `reports.read`; CSV with UTF-8 BOM so Excel opens accents correctly.
* `GET /api/v1/audit-logs?action=&entityType=&entityId=&warehouseId=&userId=&from=&to=` — `audit.read`; `action` ending with `.` is a prefix; entries tied to a warehouse the user cannot access are hidden.
* Integration tests: `ReportingTests` (dashboard by permission, every report + CSV, audit filters and permission).

## Frontend conventions

* `@wh/shared` holds the typed DTOs (`models/operations.ts`) and API clients (`api/operations.api.ts`). Stock-changing commands send an `Idempotency-Key`; the key is kept until the call succeeds, so a retry after a network error cannot post twice.
* Server-side paging, sorting and filtering on every list (PrimeNG lazy tables).
* API errors are shown with the server's ProblemDetails message; field errors are attached to the form controls; 409 shortages are listed per product.
* Dates show as `dd.MM.yyyy HH:mm` in the browser's time zone; report CSVs use UTC (column says so).
* Images behind authentication (POD photos, signature, logo) load through `wh-auth-image` with the bearer token.
* `wh-signature-pad` uses pointer events (mouse, pen, touch) and keeps the drawing when the pad is resized (rotation, dialog animation).
* A receipt cannot be posted until lot and expiry are filled for lot/expiry-tracked products (checked in the browser before the confirm dialog, and again by the API).

## Tests run

* `dotnet test`: 178 passed (backend, real PostgreSQL).
* `ng test`: 42 passed (shared 21, web 16, mobile 5). `ng build web` and `lint:types` pass.
* Browser smoke test `tools/e2e/web_smoke.py` (Playwright, Chromium) on an empty database: receipt → stock → dispatch → approval refused for shortage → return to draft and fix → approve → pick with bin and carton barcodes → partial picking → ship short with backorder → record delivery with photo and drawn signature → dashboard, templates, company, reports, audit, bin move → dispatch page at phone width. Passed; the only console error is the expected 409 from the refused approval.

Bugs found and fixed by the smoke test: the POD/logo image reloaded endlessly; the signature pad lost the drawing when its size changed and drew offset during the dialog animation; "Delivered at" was blank because its max date was fixed when the page opened; missing spaces in the customer name/reference and address; the template version date used US format.

## Scanning in the web app

The web app works with a **USB or Bluetooth barcode scanner** (keyboard wedge: it types the code and presses Enter). No setup is needed. Click into the field and scan:
* any product field (receipts, dispatches, adjustments, bin moves): searching by barcode finds the product;
* the **Products** list search;
* the scanner box on the dispatch page (bin label, then product or carton barcode).

Webcam scanning in the browser is not included. Camera scanning is in the mobile app.

## Known limitations

* The PDF preview uses the browser's PDF viewer (headless test browsers show a blank frame; normal Chrome, Edge, Firefox and Safari show the PDF).
* "Start picking" assigns the dispatch to the current user. Assigning it to another operator is supported by the API but not yet offered in the web UI.
