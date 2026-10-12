# 13 — Purchasing: suppliers, purchase orders and reordering

Status: **built and tested on 2026-10-12** (backend, web, phone). Added after Phase 8 at the user's request ("add the order/reorder to the project").

## What it does

| Part | What |
|---|---|
| **Suppliers** | Who goods are ordered from, including the company's own production (e.g. "HELLO Cherie production"). Code, name, contact, address, **lead time** (days), notes. Shared by all branches, like products and customers. |
| **Reorder settings** (per product) | **Minimum stock** (existing), **order up to** (new; empty = up to the minimum), **usual supplier**, and **ordered in** (base unit or a pack unit, e.g. cartons). |
| **Reorder suggestions** | Products whose available stock (on hand − reserved) is below their minimum, in the selected warehouse. Suggested quantity = order-up-to − available − already on order, **rounded up to whole purchase units**. Products already covered by open orders still show (quantity 0, "Covered by open orders"). Selected lines become **one draft order per supplier**. |
| **Purchase orders** | Draft → **Ordered** → Partially received → **Received**, or Closed (short) / Cancelled. Ordering never changes stock. |
| **Receiving against an order** | "Receive goods" on an open order creates a draft goods receipt pre-filled with what is still outstanding (in the order's unit when it divides evenly). Posting the receipt adds the stock and records the received quantity on the order line; reversing the receipt takes it off again. |
| **On order** | Shown in the stock overview and used by the suggestions: the outstanding quantity of ordered and partially received orders. |
| **Dashboard** | Purchasing panel: products to reorder, open orders, overdue orders (expected date passed), draft orders. |
| **Daily low-stock email** | Optional. Recipients are set on the Reorder page. Sent once a day from 07:00 company time, only when at least one product needs ordering (not already covered). |

## Rules

* **One line per product** on an order (change the quantity instead of adding a second line).
* **Draft**: everything can change. **Placed**: only the expected date, supplier reference and notes (e.g. when the supplier confirms a date). **Received, closed, cancelled**: nothing.
* **Cancel**: a draft, or a placed order nothing was received against. Once goods arrived, **close** it instead (a reason is required); what was received stays received.
* An order cannot be closed or cancelled while a **draft receipt** for it exists: post or cancel that receipt first.
* A receipt is linked to an order when it is created (from the order, or by choosing the order). Its lines are matched to the order **by product**; products not on the order can still be received (they do not count towards the order). Receiving more than ordered is allowed and recorded.
* Receipts can only be linked to orders of the **same warehouse**; another branch's order answers "not found" (its number is not revealed).
* Posting receipts locks the order row, so simultaneous receipts for one order add up correctly (tested with 4 parallel posts).
* **Changed in this release (bug fix):** a draft goods receipt can now be saved **without a lot number**; the lot is required when posting. The phone and the web already worked this way, but the server refused such drafts.

## Permissions

| Permission | Granted by default to |
|---|---|
| `purchasing.read`: see suppliers, orders and suggestions | Operator, Manager, Admin |
| `purchasing.manage`: suppliers, create / place / close / cancel orders, low-stock email | Manager, Admin |

Receiving against an order also needs `inventory.receive` (and `inventory.receive.post` to post), as before.

**Existing databases:** at start-up, a permission added by a new release is granted **once** to the built-in roles that have it by default, so an installation created before purchasing existed gets it without manual steps. If an administrator later removes it from a role, it is not added back.

## Screens

* **Web:** Purchasing → **Reorder**, **Purchase orders** (list with open-only filter, overdue tag, % received), order page (edit draft, place, receive, change dates, close, cancel, "Copy for email"), **Suppliers** (dialog). Product page: order-up-to, usual supplier, ordered in. Stock overview: "On order" column. Receipt page: link to its order.
* **Phone:** Receive goods → **Expected deliveries** (open orders, overdue marked) → tap one to start counting with the lines pre-filled ("Expected 2 PAL"); correct the counted quantities, add lots, save. An order that already has a draft receipt opens that draft instead. "Delivery without an order" works as before.

## API

| Method | Route | Permission |
|---|---|---|
| GET / POST / PUT | `/api/v1/suppliers`, `/suppliers/{id}`; POST `/suppliers/{id}/activate`, `/deactivate` | read: purchasing.read (list also products.manage); write: purchasing.manage |
| GET | `/api/v1/purchase-orders?warehouseId=&status=&openOnly=&supplierId=&productId=&search=` | purchasing.read |
| GET / POST / PUT | `/api/v1/purchase-orders`, `/purchase-orders/{id}` | read / manage |
| POST | `/purchase-orders/{id}/place`, `/cancel` `{reason?}`, `/close` `{reason}` | purchasing.manage |
| POST | `/purchase-orders/{id}/receipts` → draft goods receipt | inventory.receive **and** purchasing.read |
| POST | `/api/v1/receipts` now accepts `purchaseOrderId` | inventory.receive |
| GET | `/api/v1/purchasing/suggestions?warehouseId=` | purchasing.read |
| GET / PUT | `/api/v1/purchasing/settings` `{lowStockAlertEmails}` | read / manage |

Products: `maxStockQty`, `defaultSupplierId`, `purchaseUnitId` (a unit of measure id: the base unit or one of the product's pack units) on create and update. **On update, leaving them out clears them**: always send the current values. Stock summary rows have `onOrder`. Receipts have `purchaseOrderId`, `purchaseOrderNumber`; receipt lines `purchaseOrderLineId`. The dashboard has a `purchasing` section.

Database: migration `Purchasing` (tables `suppliers`, `purchase_orders`, `purchase_order_lines`; new columns on `products`, `goods_receipts`, `goods_receipt_lines`, `company_profiles`).

## Tests run (2026-10-12)

* `dotnet test`: **205 passed** (198 before + 7 in `PurchasingTests`): the full order cycle with partial and complete receipts and a reversal, editing rules for draft / placed / finished orders, close and cancel rules, suggestion arithmetic (rounding to cartons, minus on order, covered products), the low-stock email (not before 07:00, once a day, not without recipients), operator vs manager permissions, 4 concurrent receipts for one order, and the one-time grant of new permissions to existing roles. `WarehouseIsolationTests` now also covers purchase orders and suggestions (25 reads and 19 changes refused for another branch), and the authorization matrix covers the new endpoints automatically.
* `npm test`: **52 passed**; `ng build web`, `ng build mobile`, `lint:types`: succeed.
* Browser walkthrough `tools/e2e/purchasing_smoke.py` (on top of the demo data): **passed**. Web: dashboard, reorder → draft order → place, receive the demo order → post → order received, new supplier, product reorder settings, stock "on order". Phone: expected deliveries → start a receipt from the order → pre-filled lines → save.
* The earlier walkthroughs (`web_smoke.py`, `mobile_smoke.py`, `mobile_new_barcode.py`) were re-run on empty databases: **all passed**.

## Not included

* Prices, currencies and supplier invoices (no costing yet).
* Sending the order to the supplier by email from the system ("Copy for email" copies a text version to paste).
* Per-warehouse minimum stock (the minimum and order-up-to level are per product and apply in each warehouse).
* Receiving against an order is not shown on the phone's scan screen; it starts from Receive goods.
