# Inventory and Dispatch Management System

## 1. Project Overview

Build a web-based Inventory and Dispatch Management System for a warehouse company.

The system will allow the company to manage products, monitor stock levels, record goods received, prepare dispatches, track shipments and maintain a complete history of inventory movements.

The system must include:

* A web application for administrators and warehouse management.
* A mobile application for warehouse staff.
* A centralized backend API.
* A PostgreSQL database.

The application must be designed for production use, with secure authentication, authorization, data integrity, maintainability and automated testing.

## 2. Technology Stack

### Web frontend

* Angular with standalone components.
* PrimeNG for UI components.
* PrimeIcons.
* TypeScript strict mode.
* Reactive Forms.
* Angular Router with lazy-loaded feature routes.
* Angular HttpClient.
* Responsive design.

### Mobile application

* Ionic with Angular.
* Capacitor for Android and iOS.
* Reuse Angular services, models and API communication where practical.
* Camera-based barcode scanning.
* Internet connection required for operational actions.
* No offline inventory operations in the initial release.

### Backend

* ASP.NET Core Web API using C#.
* A supported .NET LTS version.
* Entity Framework Core.
* Npgsql PostgreSQL provider.
* RESTful API under /api/v1.
* Secure authentication and role-based authorization.
* Swagger/OpenAPI.
* Structured logging and centralized error handling.

### Database

* PostgreSQL.
* Entity Framework Core migrations.
* Foreign keys, unique constraints and indexes.
* Database transactions and concurrency protection.
* UTC timestamps.
* Appropriate numeric precision for stock quantities.

Use compatible stable versions of all dependencies. Document the selected versions.

Do not introduce alternative frontend frameworks or replace PrimeNG without explicit approval.

## 3. Business Model

The application is operated by ONE warehouse company.

The warehouse company stores and dispatches products to multiple customer companies.

Important distinctions:

* Warehouse company: the business operating the system.
* Warehouse: a physical storage and dispatch location.
* Customer: a company receiving products from the warehouse.
* Product: an item maintained in inventory.
* Dispatch: a transaction recording products sent to a customer.

The initial deployment has ONE warehouse branch.

The database and application architecture must support multiple branches in the future without requiring a database redesign.

Do not implement a multi-tenant SaaS platform for separate warehouse operators. The initial system serves one warehouse company and its customers.

Customer companies must be separate from warehouse records.

## 4. Multi-Warehouse Readiness

Create a Warehouse entity containing:

* Unique code.
* Name.
* Address.
* Contact details.
* Active/inactive status.
* Created and updated timestamps.

Seed one main warehouse for the initial deployment.

Do not hardcode warehouse IDs or assume only one warehouse exists in application logic.

Inventory balances must be associated with a warehouse and product.

Every receipt, stock adjustment, stock transfer and dispatch must reference the appropriate warehouse.

Every dispatch must record its source warehouse and destination customer.

Warehouse access must be configurable by user. Administrators may access all authorized warehouses, while warehouse staff can be restricted to assigned warehouses.

Design warehouse transfers as a future capability.

Adding another warehouse must not require redesigning existing inventory tables or dispatch records.

## 5. Customer Management

Create a Customer entity with:

* Unique customer reference.
* Company name.
* Contact person.
* Telephone number.
* Email address.
* Billing address, if required.
* One or more delivery addresses.
* Notes.
* Active/inactive status.

Dispatches must reference a customer and, where applicable, a specific delivery address.

Customers do not directly initiate dispatches in the initial release. Authorized internal staff create and process dispatches.

## 6. User Roles

Implement the following roles:

### Administrator

* Manage users and roles.
* Manage customers.
* Manage warehouses.
* Manage products and categories.
* View inventory and dispatch records.
* Configure system settings.
* Access reports and audit logs.

### Warehouse Manager

* Manage inventory operations.
* Record and approve goods receipts as permitted.
* Approve dispatches.
* Manage stock reservations.
* Assign and supervise picking tasks.
* Approve stock adjustments where authorized.
* Review warehouse reports.

### Warehouse Operator

* Search and scan products.
* View authorized inventory balances.
* Execute picking tasks.
* Confirm picked quantities.
* Prepare dispatches.
* Record receipt quantities if authorized.
* Confirm shipment only if granted permission.

Enforce permissions on the backend. Frontend route guards alone are insufficient.

## 7. Product Management

Products must support:

* SKU or product code.
* Name and description.
* Category.
* Unit of measure.
* One or more barcodes.
* Optional image.
* Minimum stock threshold.
* Active/inactive status.
* Created and updated timestamps.

Prevent duplicate SKU values within the applicable scope.

Provide search, sorting, filtering and pagination.

## 8. Inventory Management

Track stock by warehouse and product. Support batch or lot tracking if required by the business.

Track:

* On-hand quantity.
* Reserved quantity.
* Available quantity.

Use this invariant:

Available quantity = On-hand quantity - Reserved quantity

All stock changes must be recorded in an immutable InventoryMovement ledger.

Implement:

* Goods receipts.
* Stock adjustments.
* Stock transfers when enabled.
* Stock reservations.
* Dispatch deductions.
* Stock movement history.
* Low-stock alerts.

Do not allow direct editing of stock balances without a corresponding authorized transaction.

Draft receipts must not affect stock. Confirmed receipts must increase stock exactly once.

Prevent negative stock by default.

Use database transactions and concurrency control to prevent overselling, duplicate reservations and duplicate stock deductions.

## 9. Dispatch Workflow

Dispatches are created by authorized internal staff.

Initial workflow:

Draft → Submitted → Approved → Picking → Ready for Dispatch → Dispatched → Delivered

Cancellation is allowed only from valid states and must release applicable reservations.

### Draft

Staff select the customer, source warehouse and products.

No stock is deducted or reserved.

### Submitted

Validate the dispatch details and requested quantities.

No stock is deducted or reserved.

### Approved

Validate available stock and reserve the required quantities atomically.

On-hand stock remains unchanged.

Available stock decreases by the reserved quantity.

### Picking

Warehouse operators scan product barcodes and confirm picked quantities.

Reject unexpected products and unauthorized quantities.

Record discrepancies.

### Ready for Dispatch

Validate picking results and required dispatch information.

### Dispatched

An authorized user confirms shipment.

Deduct the actual shipped quantity from on-hand stock and consume the corresponding reservation.

Perform both changes in a single database transaction.

Prevent duplicate shipment confirmation.

### Delivered

Record delivery confirmation and proof of delivery if required.

Do not deduct stock again.

### Partial Dispatch

Support partial shipment where permitted.

Deduct only the shipped quantity. Retain or release remaining reservations according to the dispatch completion decision.

### Cancellation

Require a cancellation reason.

Release outstanding reservations for eligible unshipped dispatches.

Already-shipped products must be handled through a separate return or reversal workflow.

The backend must enforce all valid state transitions.

Maintain a complete dispatch status history.

## 10. Barcode Scanning

Barcode scanning is required in the warehouse mobile application.

Use a maintained Capacitor-compatible barcode-scanning plugin that supports the required Android and iOS versions.

Implement:

* Product identification by barcode.
* Barcode verification during picking.
* Barcode-based lookup during receiving where appropriate.
* Manual search as a fallback.
* Clear feedback for unknown or inactive products.
* Validation against the current dispatch or receipt.
* Protection against accidental duplicate scans.

A barcode scan must never bypass backend authorization or stock validation.

Verify the chosen scanning library against the actual target devices before committing to it.

## 11. Mobile Application

The mobile application is primarily for warehouse staff.

Prioritize:

1. Secure login.
2. Warehouse selection.
3. Product search.
4. Barcode scanning.
5. Stock lookup.
6. Assigned picking tasks.
7. Quantity confirmation.
8. Dispatch preparation.
9. Shipment confirmation where authorized.
10. Goods receiving where authorized.

Require an internet connection for operational actions.

Do not implement offline stock changes or background synchronization in the initial release.

Display connection errors clearly and require server confirmation before showing a stock-changing operation as successful.

## 12. Web Application

Use PrimeNG for the web interface.

Implement:

* Login.
* Dashboard.
* User management.
* Customer management.
* Warehouse management.
* Product and category management.
* Barcode management.
* Inventory balances.
* Inventory movement history.
* Goods receipts.
* Stock adjustments.
* Stock transfers when enabled.
* Dispatch creation and approval.
* Picking and shipment processing.
* Reports and audit history.

Use server-side pagination, sorting and filtering for large datasets.

Provide loading, empty, validation, success and error states.

## 13. Database and Architecture

Use a maintainable layered .NET architecture.

Suggested backend projects:

* Inventory.Domain
* Inventory.Application
* Inventory.Infrastructure
* Inventory.Api
* Inventory.Tests
* Inventory.IntegrationTests

Suggested entities:

* User
* Role
* Warehouse
* UserWarehouseAccess
* Customer
* CustomerAddress
* Product
* ProductCategory
* UnitOfMeasure
* ProductBarcode
* InventoryBalance
* InventoryMovement
* GoodsReceipt
* GoodsReceiptLine
* StockReservation
* StockAdjustment
* StockTransfer
* StockTransferLine
* Dispatch
* DispatchLine
* DispatchStatusHistory
* Attachment
* AuditLog

Adapt the model to confirmed business requirements. Do not introduce unnecessary entities.

Use request and response DTOs rather than exposing EF Core entities directly.

Maintain referential integrity and historical records.

Create EF Core migrations for schema changes.

## 14. API Standards

Use versioned endpoints under /api/v1.

Suggested route groups:

* /api/v1/auth
* /api/v1/users
* /api/v1/customers
* /api/v1/warehouses
* /api/v1/products
* /api/v1/product-barcodes
* /api/v1/inventory/balances
* /api/v1/inventory/movements
* /api/v1/receipts
* /api/v1/adjustments
* /api/v1/transfers
* /api/v1/dispatches
* /api/v1/reports

Use explicit endpoints for dispatch approval, picking, shipment, delivery and cancellation.

Validate all requests on the server.

Use consistent ProblemDetails error responses, suitable HTTP status codes and Swagger documentation.

Never trust user IDs, warehouse IDs or quantities supplied by the client without validating authorization and business rules.

## 15. Security

Implement:

* Secure password hashing.
* Authentication and authorization.
* Warehouse-specific access control.
* HTTPS.
* Restrictive CORS.
* Safe file-upload validation.
* Secret management outside source control.
* Rate limiting where appropriate.
* Structured logging with sensitive information redacted.
* Audit trails for inventory and dispatch operations.
* Database backup and recovery documentation.

## 16. Testing Requirements

Use xUnit for backend tests and an Angular-compatible frontend testing framework.

Test at least:

1. Receiving stock increases on-hand quantity exactly once.
2. Approving a dispatch reserves stock without reducing on-hand stock.
3. Shipping a dispatch deducts actual shipped quantities and consumes reservations.
4. Marking a dispatch delivered does not deduct stock again.
5. Cancelling an eligible dispatch releases outstanding reservations.
6. Concurrent reservations cannot allocate more than available stock.
7. Duplicate shipment requests cannot deduct stock twice.
8. Incorrect barcodes cannot cause invalid stock movements.
9. Unauthorized users cannot perform restricted operations.
10. Adding another warehouse does not affect existing inventory or dispatch history.

## 17. Development Workflow

Work incrementally.

Phase 1: Inspect the repository, clarify business rules, document the architecture, database design, stock invariants and dispatch workflow.

Phase 2: Create the project structure, database connection, migrations, authentication and authorization.

Phase 3: Implement customers, warehouses, products, categories and barcodes.

Phase 4: Implement inventory balances, receipts, movements, adjustments, reservations and concurrency protections.

Phase 5: Implement dispatch creation, approval, picking, barcode verification, shipping, partial dispatch and delivery.

Phase 6: Implement the web interface with Angular and PrimeNG.

Phase 7: Implement the warehouse mobile application with Ionic, Angular and Capacitor.

Phase 8: Run automated tests, resolve failures and verify security and stock accounting.

Phase 9: Document deployment, monitoring, database backup, recovery and mobile distribution.

After each meaningful change:

* Build the affected projects.
* Run relevant tests.
* Fix errors before proceeding.
* Update documentation.
* Report what was completed and what remains.

Never claim tests or builds succeeded unless they were actually executed.

Do not deploy to production without explicit approval.

## 18. Initial Task

Start by inspecting the repository and development environment.

Do not immediately generate the full application.

First produce:

1. A concise architecture proposal.
2. A database ER diagram.
3. A database entity and relationship list.
4. Inventory accounting rules.
5. The dispatch state machine.
6. The recommended barcode-scanning library.
7. A phased implementation plan.
8. A list of unresolved business questions.

Wait for review before implementing the full system.
