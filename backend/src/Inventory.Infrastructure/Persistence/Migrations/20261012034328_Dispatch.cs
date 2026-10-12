using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    registration_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    vat_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    footer_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    delivery_email_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    delivery_email_body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    delivery_email_bcc = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profile", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_profile_stored_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "dispatches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_address_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    backorder_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ship_to_label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ship_to_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ship_to_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ship_to_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ship_to_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ship_to_contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ship_to_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    assigned_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    picking_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_by = table.Column<Guid>(type: "uuid", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    short_decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispatches_customer_addresses_delivery_address_id",
                        column: x => x.delivery_address_id,
                        principalTable: "customer_addresses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispatches_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispatches_dispatches_backorder_of_id",
                        column: x => x.backorder_of_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispatches_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    html = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    bcc = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    related_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    related_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attachment_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispatch_discrepancies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatch_discrepancies", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispatch_discrepancies_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispatch_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_reserved = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_picked = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_shipped = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatch_lines", x => x.id);
                    table.CheckConstraint("ck_dispatch_lines_quantities", "quantity > 0 AND quantity_reserved >= 0 AND quantity_picked >= 0 AND quantity_picked <= quantity_reserved AND quantity_shipped >= 0 AND quantity_shipped <= quantity_picked AND quantity_reserved <= quantity");
                    table.ForeignKey(
                        name: "fk_dispatch_lines_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dispatch_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispatch_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    to_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatch_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispatch_status_history_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proofs_of_delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signature_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proofs_of_delivery", x => x.id);
                    table.ForeignKey(
                        name: "fk_proofs_of_delivery_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_proofs_of_delivery_stored_files_signature_file_id",
                        column: x => x.signature_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispatch_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    email_to_customer = table.Column<bool>(type: "boolean", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatch_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispatch_documents_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispatch_documents_document_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "document_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispatch_documents_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pick_confirmations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    undo_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_confirmations", x => x.id);
                    table.CheckConstraint("ck_pick_confirmations_quantity", "quantity <> 0");
                    table.ForeignKey(
                        name: "fk_pick_confirmations_dispatch_lines_line_id",
                        column: x => x.line_id,
                        principalTable: "dispatch_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_confirmations_dispatches_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_confirmations_stock_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "stock_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_confirmations_warehouse_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "warehouse_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_profile_logo_file_id",
                table: "company_profile",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_discrepancies_dispatch_id",
                table: "dispatch_discrepancies",
                column: "dispatch_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_documents_dispatch_id_type",
                table: "dispatch_documents",
                columns: new[] { "dispatch_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_documents_file_id",
                table: "dispatch_documents",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_documents_status",
                table: "dispatch_documents",
                column: "status",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_documents_template_id",
                table: "dispatch_documents",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_lines_dispatch_id_line_no",
                table: "dispatch_lines",
                columns: new[] { "dispatch_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_lines_dispatch_id_product_id",
                table: "dispatch_lines",
                columns: new[] { "dispatch_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_lines_product_id",
                table: "dispatch_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_status_history_dispatch_id_at",
                table: "dispatch_status_history",
                columns: new[] { "dispatch_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_assigned_to_id",
                table: "dispatches",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_backorder_of_id",
                table: "dispatches",
                column: "backorder_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_customer_id_created_at",
                table: "dispatches",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_delivery_address_id",
                table: "dispatches",
                column: "delivery_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_number",
                table: "dispatches",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_warehouse_id_status_created_at",
                table: "dispatches",
                columns: new[] { "warehouse_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_document_templates_type",
                table: "document_templates",
                column: "type",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_document_templates_type_version",
                table: "document_templates",
                columns: new[] { "type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_next_attempt_at",
                table: "email_messages",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_related_type_related_id",
                table: "email_messages",
                columns: new[] { "related_type", "related_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_confirmations_dispatch_id_scan_id",
                table: "pick_confirmations",
                columns: new[] { "dispatch_id", "scan_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pick_confirmations_line_id",
                table: "pick_confirmations",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_confirmations_location_id",
                table: "pick_confirmations",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_confirmations_reservation_id",
                table: "pick_confirmations",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_confirmations_undo_of_id",
                table: "pick_confirmations",
                column: "undo_of_id",
                unique: true,
                filter: "undo_of_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_proofs_of_delivery_dispatch_id",
                table: "proofs_of_delivery",
                column: "dispatch_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proofs_of_delivery_signature_file_id",
                table: "proofs_of_delivery",
                column: "signature_file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_profile");

            migrationBuilder.DropTable(
                name: "dispatch_discrepancies");

            migrationBuilder.DropTable(
                name: "dispatch_documents");

            migrationBuilder.DropTable(
                name: "dispatch_status_history");

            migrationBuilder.DropTable(
                name: "email_messages");

            migrationBuilder.DropTable(
                name: "pick_confirmations");

            migrationBuilder.DropTable(
                name: "proofs_of_delivery");

            migrationBuilder.DropTable(
                name: "document_templates");

            migrationBuilder.DropTable(
                name: "dispatch_lines");

            migrationBuilder.DropTable(
                name: "dispatches");
        }
    }
}
