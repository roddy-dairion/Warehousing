# 08 — Dispatch Engine (Phase 5)

Status: **Built and tested 2026-10-12.** Backend only. The web screens come in Phase 6 and the mobile screens (picking, signature pad, photos) in Phase 7.

## Workflow

**Draft → Submitted → Approved → Picking → Ready for dispatch → Dispatched → Delivered**, or **Cancelled** at any point before shipping.

Status changes only through these commands (`/api/v1/dispatches/{id}/…`). Every change writes a history row and an audit entry in the same transaction.

| Command | From → to | Permission | Stock effect |
|---|---|---|---|
| `POST /dispatches`, `PUT /dispatches/{id}` | Draft | `dispatches.create` | — |
| `submit` | Draft → Submitted | `dispatches.create` | — (copies the delivery address) |
| `return` (reason) | Submitted → Draft | `dispatches.approve` | — |
| `approve` | Submitted → Approved | `dispatches.approve` | **Reserves** every line (earliest expiry first), all or none |
| `picking` (optional assignee), `assign` | Approved → Picking | `dispatches.pick` / `dispatches.approve` | — |
| `picks`, `picks/{id}/undo`, `reallocate`, `discrepancies` | Approved or Picking | `dispatches.pick` | Picks: none. Reallocate moves the reservation |
| `ready` (`allowShort`) | Picking → Ready | `dispatches.pick` | — |
| `reopen-picking` | Ready → Picking | `dispatches.approve` | — |
| `ship` (carrier, tracking, `shortDecision`) | Ready → Dispatched | `dispatches.ship` | **Deducts** the picked quantities and uses up the reservations; the rest is released or moved to a backorder |
| `deliver` (form: name, signature, photos) | Dispatched → Delivered | `dispatches.deliver` | None |
| `cancel` (reason) | any status before Dispatched → Cancelled | `dispatches.cancel` | Releases the reservations |

Default roles:
- **Operators** create, submit, pick and deliver.
- **Managers** also approve, ship, cancel and reopen picking.

Dispatches are scoped to the warehouses the user can access. A dispatch in another warehouse returns 404.

## Rules

* **Submit:** at least one line; each product at most once; products active; customer active with an active delivery address (the chosen one, or the default). Pack quantities are converted to base units (2 CTN = 24 EA).
* **Approve:**
  * **The creator or submitter cannot approve** their own dispatch (`Dispatch:RequireDifferentApprover`, Q6).
  * Stock is re-checked and reserved for every line in one transaction.
  * If any line is short, nothing is reserved and the API returns 409 `stock.shortage` with a `shortages` list.
  * Only stock in active pickable bins, from released and unexpired lots, is used.
* **Pick:**
  * The device sends the bin (code or label barcode), the product or pack barcode (or the product, for manual entry), the lot and the quantity.
  * The server resolves the barcode itself; a pack barcode counts packs.
  * It checks that the product is on the dispatch (`pick.not_on_dispatch`), that the bin and lot match an allocation (`pick.not_allocated`, with where it is allocated), and that the quantity is not more than what remains (`pick.over_pick`).
  * Each scan carries a `scanId`. Sending the same scan id again returns the first result and is **not counted twice**; this was tested with 5 identical scans sent at once.
  * Picking does not move stock: the goods stay on their bin, shown as reserved, until shipment.
  * **Undo** records a negative pick, so history is kept. **Reallocate** moves the unpicked part of an allocation to another eligible bin or lot.
* **Ready:** every line fully picked, or `allowShort: true` (`Dispatch:AllowPartialShipment`, Q5). Nothing picked at all → cancel instead.
* **Ship:**
  * Ships exactly what was picked, in one transaction: on-hand stock goes down, the reservations are used up, and a `Dispatch` stock movement is written for each allocation.
  * If any line is short, the shipper must choose:
    * **closeShort:** the rest is released.
    * **backorder:** a new dispatch, already Approved, takes over the rest of the reservation, so the stock stays held for it.
  * A second confirmation returns 409, so stock can't be deducted twice. This was tested with 6 parallel confirmations, and also with replays using an `Idempotency-Key`.
* **Deliver (Q13):**
  * Required: the receiver's name, a **signature** (PNG, JPEG or WebP up to 1 MB, drawn on the phone or tablet) and **at least one photo** (up to 10, 10 MB each). The limits are configurable.
  * Every file is checked by its actual content, not its extension.
  * Delivery never touches stock.
  * The signature and photos can be downloaded with `GET pod/files/{fileId}`.
* **Cancel:** a reason is required, and any reservations are released. A shipped dispatch can't be cancelled; goods that come back will be booked through a future returns document.

## Documents and email (Q14)

* **Packing slip:** produced when the dispatch ships. It is unsigned and meant to be printed and travel with the goods.
* **Signed delivery note:** produced after proof of delivery. It shows the receiver's name, the signature and the date and time; photos stay on the dispatch. It is **emailed to the customer** (the customer's email address) with the PDF attached, and stays downloadable on the dispatch.
* Documents are generated right after the transaction commits. If that fails (for example, the PDF browser is unavailable), the document stays *Pending*, the background worker retries, and staff can regenerate with `POST dispatches/{id}/documents/{packingSlip|deliveryNote}`.
* **Email outbox:**
  * The email is queued in the database and sent by the background worker, with retries at 1, 2, 4 … minutes, up to 8 attempts.
  * A mail-server outage never blocks a delivery.
  * Sending status shows on the dispatch, and `POST dispatches/{id}/delivery-note/send` resends it, optionally to another address.
  * If the customer has no email address, nothing is queued and an audit entry records why.
* **Sender:** `dispatch@hellocherie.ch`, with optional internal copies set in the company settings.

### Templates (editable in admin)

| Endpoint (`settings.manage`) | What |
|---|---|
| `GET/PUT /settings/company`, `POST/DELETE/GET /settings/company/logo` | Company name, address, phone, email, website, registration and VAT numbers, footer, logo, time zone for printed dates, delivery-email subject, text and copy-to |
| `GET /document-templates`, `GET /document-templates/{type}?version=` | Current template, version history and the list of placeholders |
| `PUT /document-templates/{type}` | Saves a **new version**. It is checked against sample data first; errors return 422 with the line and column |
| `POST /document-templates/{type}/versions/{n}/restore` | Restores an old version (as a new version) |
| `POST /document-templates/{type}/preview` | PDF preview of the given HTML (or the current template), with sample data or a real `dispatchId` |

* Templates are HTML with **Liquid** placeholders, for example `{{ customer.name }}` and `{% for line in lines %}…{% endfor %}`. The available fields are listed by the API.
* Every value is **HTML-encoded** automatically, so customer data cannot inject markup.
* Every generated document records which template version produced it.
* The default templates are seeded as version 1. They are in `backend/src/Inventory.Application/Documents/Templates`.
* PDFs are A4, with page numbers in the footer.

### PDF rendering

* PDFs are rendered by a headless Chromium-based browser through PuppeteerSharp. While rendering, **JavaScript is disabled and all network access is blocked**, so templates can only use inline CSS and embedded images.
* The browser is found automatically: Microsoft Edge or Chrome on Windows (no install needed on the development PC), and `chromium` or `google-chrome` on Linux.
* Set `Pdf:BrowserPath` to choose a specific browser. Set `Pdf:NoSandbox=true` when running as root in a container.
* On the production server, install Chromium (for example `apt install chromium`). This is noted for Phase 9.

### Time zones

The company time zone accepts IANA names (`Indian/Mauritius`, `Europe/Zurich`) or Windows names (`Mauritius Standard Time`). This relies on ICU: the backend no longer runs in invariant-globalization mode. Windows includes ICU; Linux servers need the `libicu` package, which most distributions and the .NET Docker images already have.

### Email configuration (`Email` section)

| Setting | Development | Production |
|---|---|---|
| `Mode` | `Pickup`: emails are written as `.eml` files to `App_Data/mail` | `Smtp` |
| `From` / `FromName` | `dispatch@hellocherie.ch` / `HelloCherie Dispatch` | same |
| `SmtpHost` / `SmtpPort` / `SmtpSecurity` | — | `smtp.office365.com` / `587` / `StartTls` |
| `SmtpUsername` / `SmtpPassword` | — | the `dispatch@hellocherie.ch` mailbox. **Keep the password in secrets, never in appsettings** |

For Microsoft 365, the mailbox needs **Authenticated SMTP** enabled (Microsoft 365 admin centre → user or shared mailbox → Mail → Manage email apps), or the tenant must allow SMTP AUTH. If the tenant blocks SMTP AUTH, sending through the Microsoft Graph API can be added instead.

## Settings

| Section | Setting | Default |
|---|---|---|
| `Dispatch` | `RequireDifferentApprover` | `true` |
| | `AllowPartialShipment` | `true` |
| | `MinDeliveryPhotos` / `MaxDeliveryPhotos` / `MaxPhotoMegabytes` | `1` / `10` / `10` |
| `Documents` | `MaxAttempts` | `5` |
| `Pdf` | `BrowserPath`, `NoSandbox`, `TimeoutSeconds`, `MaxConcurrent` | auto, `false`, `30`, `2` |
| `Email` | `Mode`, `PollSeconds`, `WorkerEnabled` | `Disabled` (development: `Pickup`), `15`, `true` |

## Data

New tables:

* `dispatches`: the delivery address snapshot is stored as `ship_to_*` columns.
* `dispatch_lines`: CHECK constraint `picked ≤ reserved ≤ requested` and `shipped ≤ picked`.
* `dispatch_status_history`.
* `pick_confirmations`: unique (dispatch, scan id); an undo is linked to its pick.
* `dispatch_discrepancies`.
* `proofs_of_delivery`.
* `document_templates`: versioned, one current version per type.
* `dispatch_documents`.
* `company_profile`.
* `email_messages`: the outbox.

Migration: `Dispatch`.

## Tests

29 new tests: 18 integration tests against real PostgreSQL that create real PDFs, and 11 unit tests.

* **Full flow:** reserve at approval, deduct once at shipment, history; packing slip PDF; reconciliation clean afterwards.
* **Approval:** self-approval blocked; shortage reserves nothing (with the shortage list).
* **Validation:** empty dispatch, duplicate product, missing address; return to draft; edit version conflicts.
* **Picking:** unknown barcode, wrong product, wrong bin, carton barcode counting, repeated scan id, over-pick, undo; discrepancy report.
* **Reallocation:** to another bin, then ship.
* **Short shipments:** close short; backorder keeps the reservation and ships later.
* **Cancel and access:** cancel releases stock; shipped dispatches can't be cancelled; warehouse scoping; assignee checks; list filters.
* **Concurrency:** 4 approvals competing for the same stock → exactly 2 succeed; 6 parallel shipments → stock deducted once; idempotent ship replay; 5 identical scans counted once.
* **Delivery:** signature, name, photo count and file type checks; delivery creates the signed note and the email; the email is sent with the PDF attached; resend; no email address → nothing queued.
* **Templates and settings:** invalid template rejected, versions, preview PDF, documents use the current version, restore and regenerate; output is HTML-encoded; company settings and logo.
