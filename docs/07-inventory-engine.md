# 07 — Inventory Engine (Phase 4)

Status: **Built and tested 2026-10-11.** Backend only. The web screens come in Phase 6 and the mobile screens in Phase 7.

## What it does

| Area | Endpoints (`/api/v1/…`) | Permission |
|---|---|---|
| Stock balances (product × bin × lot) | `GET inventory/balances?warehouseId=` | `inventory.read` |
| Stock per product, with sellable stock and low-stock flag | `GET inventory/summary?warehouseId=&lowStockOnly=` | `inventory.read` |
| One product across the user's warehouses (scan lookup) | `GET inventory/products/{productId}` | `inventory.read` |
| Stock ledger | `GET inventory/movements?warehouseId=&productId=&type=&sourceId=&from=&to=` | `inventory.read` |
| Expiry alerts | `GET inventory/expiring?warehouseId=&days=` | `inventory.read` |
| Reconciliation check | `GET inventory/reconciliation?warehouseId=` | `inventory.adjust.approve` or `reports.read` |
| Lots of a product | `GET products/{id}/lots` | `inventory.read` |
| Release / quarantine / block a lot | `POST lots/{id}/status` | `inventory.adjust.approve` |
| Goods receipts | `GET/POST receipts`, `GET/PUT receipts/{id}`, `POST receipts/{id}/cancel` | `inventory.receive` |
| Post / reverse a receipt | `POST receipts/{id}/post`, `POST receipts/{id}/reverse` | `inventory.receive.post` |
| Adjustments | `GET/POST adjustments`, `PUT adjustments/{id}`, `submit`, `reopen`, `cancel` | `inventory.adjust` |
| Approve / reject an adjustment | `POST adjustments/{id}/approve`, `reject` | `inventory.adjust.approve` |
| Bin moves (put-away) | `GET transfers`, `GET transfers/{id}`, `POST transfers/bin-moves` | `inventory.transfer` (read: `inventory.read`) |

Every query and command checks that the user can access the warehouse. A warehouse, receipt or adjustment outside the user's access returns **404**, so its existence is not revealed.

Document numbers are `GR-` (receipt), `ADJ-` (adjustment) and `MOV-` (bin move), followed by the warehouse code, the year and a 6-digit counter, e.g. `GR-MAIN-2026-000001`. The counter is per type, warehouse and year, and has no gaps.

## How stock changes

All stock changes go through one component, the **stock ledger** (`StockLedger`). For each change it:

1. requires a database transaction (the caller's document change commits in the same transaction);
2. locks the affected balance rows (`SELECT … FOR UPDATE`, in ascending id order so two requests never deadlock), creating missing rows when stock is added;
3. applies the change and checks **on hand ≥ 0** and **0 ≤ reserved ≤ on hand**. A failed check returns 409, naming the SKU, lot and bin (for example *"SOAP-100 lot L-001 in bin A-01: Not enough stock: 2 on hand, 5 needed."*);
4. appends one **movement** per on-hand change.

The database backs this up:

* CHECK constraints on `inventory_balances` (`on_hand >= 0`, `reserved BETWEEN 0 AND on_hand`); `available` is a generated column (`on_hand - reserved`).
* `inventory_movements` is **append-only**: a trigger rejects UPDATE, DELETE and TRUNCATE. Corrections are new movements.
* A unique key on (source type, source line, movement type) means a document line can post only once, even if every other check were bypassed.
* A unique index on (warehouse, bin, product, lot) with `NULLS NOT DISTINCT`, so non-lot products also have exactly one row per bin.

| Operation | On hand | Reserved | Movement |
|---|---|---|---|
| Receipt post | + received, in the line's bin (RECEIVING if none was given) | — | `Receipt` |
| Receipt reverse (whole receipt) | − received; fails if the stock was used or reserved | — | `ReceiptReversal` |
| Adjustment approve | ± per line | — | `AdjustmentIn` / `AdjustmentOut` |
| Bin move | − at source bin, + at target bin; only unreserved stock moves | — | `MoveOut` + `MoveIn` |
| Reserve (Phase 5 approval) | — | + | none (reservation rows) |
| Release (Phase 5 cancel / close short) | — | − outstanding | none |
| Consume (Phase 5 ship) | − shipped | − shipped | `Dispatch` (source line = reservation id) |

## Goods receipts

* **Draft → Posted → Reversed**, or **Draft → Cancelled**. Only drafts can be edited (send the `version` you loaded).
* Quantities can be entered in a pack unit (e.g. 2 CTN); the API stores base units (24 EA) and rejects fractions the unit doesn't allow.
* A line without a bin goes to the warehouse's **RECEIVING** area, which is not pickable. Stock there must be put away (bin move) before it can be reserved. Receiving into the DOCK is not allowed.
* **Lots:** lot-tracked products need a lot number (stored in capitals). The first receipt of a lot sets its expiry date. Later receipts of the same lot may leave it empty, but if they give one it must match. Products with expiry dates need an expiry for every new lot.
* **Post exactly once:** posting locks the receipt row and re-checks its status, so repeated or simultaneous posts add stock once (tested with 8 parallel requests). The second post returns 409 `receipt.already_posted`.
* **Reverse** needs a reason. It fails with 409 if any of the received stock has since been reserved, moved or shipped. In that case, use an adjustment instead.

## Adjustments

* **Draft → Pending approval → Posted**, or **Rejected → (reopen) → Draft**, or **Cancelled**. Posted adjustments cannot be cancelled; correct them with a new adjustment.
* A reason is required: count correction, damage, loss, found, expired, or other (other needs a note). Lines carry a signed quantity: positive adds stock, negative removes it.
* Operators (`inventory.adjust`) create and submit adjustments. Managers (`inventory.adjust.approve`) approve or reject them. **The submitter cannot approve their own adjustment**; this can be switched off with `Inventory:RequireDifferentApprover`.
* Approving applies every line in one transaction, or none of them. A line that would make stock negative, or push it below what is reserved, fails the whole approval. The adjustment then stays pending and nothing changes.
* An adjustment can create a new lot (positive line with lot and expiry). Removing stock needs an existing lot.
* The detail view shows the current on-hand quantity for each line while the adjustment is a draft or pending, so the approver can judge the change.

## Bin moves

A bin move is created and posted in one step: one MoveOut and one MoveIn movement per line. Only unreserved stock can move. Moving reserved stock returns 409; the reservation must be swapped or released first (Phase 5). Moves between warehouses (transfers) are a later feature. The data model already supports them (`StockTransfer.Type`, `TransferOut` and `TransferIn` movements).

## Reservations (engine ready, used by dispatch approval in Phase 5)

* **FEFO:** earliest expiry first, then the oldest lot, then the oldest balance row. Lots without expiry come last.
* **Eligible stock only:** active, pickable storage bins (not RECEIVING, DOCK or QUARANTINE); lots that are *Released*, and whose expiry is on or after the dispatch date plus `Inventory:MinShelfLifeDays` (default 0).
* **All lines or none:** if any line is short, nothing is reserved and the API returns 409 `stock.shortage` with a `shortages` list (SKU, requested, available).
* **Concurrency:** candidate balances are locked before allocating, so simultaneous approvals cannot reserve the same stock. In testing, 10 parallel requests of 3 against 10 in stock gave exactly 3 successes.
* **Idempotent per document:** reserving again for a document that already holds reservations returns the existing ones.
* Quarantining or blocking a lot does not cancel existing reservations. New reservations skip the lot. Phase 5 will flag affected dispatches.

## Idempotency

`POST receipts/{id}/post`, `receipts/{id}/reverse`, `adjustments/{id}/approve` and `transfers/bin-moves` accept an `Idempotency-Key` header (up to 100 characters; a UUID is recommended).

* The first successful response is stored per **user + key**. A retry with the same key and request returns the stored response, with the header `Idempotent-Replayed: true`, and does not run again.
* The same key used for a different request returns 422 `idempotency.key_reused`. A retry while the first request is still running returns 409 `idempotency.in_progress`.
* Failed requests (4xx/5xx) are not stored, so the client can retry with the same key.
* Records are kept for `Inventory:IdempotencyRetentionHours` (default 48 h), then deleted by the maintenance job.
* Without the header, the natural checks still apply (for example "already posted").

## Reconciliation and maintenance

A background job runs hourly. It deletes expired idempotency records. Every `Inventory:ReconciliationIntervalHours` (default 24 h) it also checks every balance:

* **on hand = sum of its movements**;
* **reserved = sum of its open reservations**.

Differences are logged as errors and listed by `GET inventory/reconciliation`. The job **never changes data**. A difference means a bug or a manual database edit, and needs investigation. Set `Inventory:MaintenanceEnabled=false` to switch the job off (the integration tests do this).

## Rules added to master data

* A bin that holds stock cannot be deactivated (409 `location.has_stock`).
* A warehouse that holds stock cannot be deactivated (409 `warehouse.has_stock`).
* A product's base unit, lot tracking and expiry tracking cannot change once it has stock movements (409 `product.has_stock_history`).

## Settings (`appsettings.json`, section `Inventory`)

| Setting | Default | Meaning |
|---|---|---|
| `RequireDifferentApprover` | `true` | The submitter of an adjustment cannot approve it |
| `ExpiryAlertDays` | `30` | Default window for `inventory/expiring` |
| `MinShelfLifeDays` | `0` | Minimum remaining shelf life for a lot to be reserved (Q20) |
| `IdempotencyRetentionHours` | `48` | How long idempotent responses are kept |
| `MaintenanceEnabled` | `true` | Run the background job |
| `ReconciliationIntervalHours` | `24` | How often the reconciliation check runs |

## Tests

These all run against real PostgreSQL:

* **Receipts:** posting adds stock once, including 8 concurrent posts.
* **Units and lots:** pack-unit conversion and precision; lot required; one expiry per lot.
* **Reversal:** removes stock, but is blocked when the stock is reserved.
* **Editing and access:** version conflicts on drafts; operators can't post; warehouse scoping returns 404.
* **Idempotency:** replay, a key reused for a different request, failed requests not stored, keys scoped per user.
* **Adjustments:** approval flow, self-approval blocked, all-or-nothing approval, new lots, reserved stock protected.
* **Bin moves:** reserved stock can't move; idempotent replay.
* **Database guards:** master-data rules; ledger UPDATE and DELETE rejected by the trigger; CHECK constraints.
* **Reservations:** FEFO order, skipping expired, blocked, non-pickable and RECEIVING stock, and minimum shelf life; concurrent reservations never over-allocate; idempotent per document; consume and release.
* **Reconciliation and queries:** reconciliation detects a tampered balance; summary and low stock; product stock across warehouses; expiring lots; movement filters.

Domain unit tests cover the balance invariants, the reservation states, lot rules, quantity precision, receipt and adjustment state machines, and bin-move validation.
