# 06 — Master Data (Phase 3)

Status: **Built and tested 2026-10-09.**

## Screens (web)

| Screen | Who | What |
|---|---|---|
| Products | read: `products.read`; edit: `products.manage` | Paged list (search SKU/name or exact barcode, category, status). Edit page: details, stock rules, pack sizes, barcodes, image. |
| Categories and units | `products.read` / `products.manage` | Category tree (parent/child, no cycles); units with allowed decimals. |
| Customers | read: `customers.manage` or dispatch staff; edit: `customers.manage` | Company details, billing address, several delivery addresses with one default. |
| Warehouses and bins | `warehouses.manage` (bins readable with `inventory.read`) | Branch details; bins with zone, type, label barcode, pickable flag. |
| Users | `users.manage` | Create, edit roles and warehouse access, reset password, unlock, deactivate. |
| Import | the matching manage permission | CSV templates; check the file, then import. |

## Business rules implemented

* **Codes** (SKU, customer reference, warehouse/bin/category/unit codes) are stored in capitals and are unique. SKU and references cannot be changed after creation.
* **Quantities** are stored in the product's base unit. Pack sizes say how many base units one pack contains (e.g. CTN = 48 EA). The base unit's *decimals* setting limits precision (EA = 0, KG = 3).
* **Expiry requires lot tracking**, because expiry dates are recorded per lot. Shelf life applies only with expiry.
* **Barcodes are unique across all products and bin labels**, so a scan always identifies one thing. A barcode can identify a single unit or a pack. Each unit has one primary barcode.
* **Bins:** only *Storage* bins can be pickable. Every warehouse has built-in RECEIVING, DOCK and QUARANTINE areas that cannot be deactivated or retyped.
* **Nothing is deleted.** Records are deactivated, and history stays linked.
* **Concurrent edits:** every edit sends the version it loaded. If someone else saved in the meantime, the API returns 409 and the screen asks you to reload.
* **Users:**
  * You cannot deactivate yourself, or remove your own admin role or all-warehouse access.
  * At least one active administrator with all-warehouse access must remain.
  * Deactivating a user, resetting their password or changing their roles/access signs them out everywhere.
* **Images:** JPEG, PNG or WebP up to 2 MB, checked by content (not file name). They are stored under random names in `Storage:RootPath` (default `App_Data/files`) and served only to signed-in users.
* **Audit:** every create, update, activate/deactivate and import writes an audit entry.

## CSV import

* Comma, semicolon or tab separated; UTF-8 (Excel "CSV UTF-8" or "CSV (semicolon)"). Decimal comma accepted (`2,5`). Yes/no columns accept yes/no, oui/non, true/false, 1/0.
* **All or nothing:** every row is validated first. If any row has an error, nothing is saved and the errors are listed by row and column.
* **Blank cells keep existing values** when a row updates an existing record.
* Products: one row per product, upsert by SKU. Pack size and barcodes can be given on the same row.
* Customers: one row per delivery address. Repeat the reference to add more addresses.
* Bins: upsert by warehouse code + bin code. Rows for warehouses you cannot access are rejected.
* Limits: 5 MB, 10,000 rows per file.

## API additions

`/api/v1/warehouses` (CRUD, activate/deactivate, `/{id}/locations`), `/api/v1/locations/{id}`, `/api/v1/customers` (+ `/addresses`), `/api/v1/categories`, `/api/v1/units`, `/api/v1/products` (+ `/units`, `/barcodes`, `/image`), `/api/v1/product-barcodes/{value}` (scan lookup), `/api/v1/users`, `/api/v1/roles`, `/api/v1/permissions`, `/api/v1/imports/{products|customers|locations}` (+ `/template`). Full details in Swagger (`/swagger`).

## Tests

* Backend: 96 (34 unit, 62 integration on real PostgreSQL), including authorization per role, warehouse scoping (404 outside scope), uniqueness, version conflicts, image type checks, user safety rules, session revocation, and import validation/atomicity.
* Frontend: 31 unit tests, plus a browser walkthrough of every screen against the real API.
