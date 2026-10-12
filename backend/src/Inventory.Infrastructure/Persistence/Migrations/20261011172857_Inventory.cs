using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_sequences",
                columns: table => new
                {
                    prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_sequences", x => new { x.prefix, x.warehouse_id, x.year });
                    table.CheckConstraint("ck_document_sequences_last_value", "last_value > 0");
                    table.ForeignKey(
                        name: "fk_document_sequences_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    supplier_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipts_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    response_body = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_lots", x => x.id);
                    table.CheckConstraint("ck_product_lots_lot_no_upper", "lot_no = upper(lot_no)");
                    table.ForeignKey(
                        name: "fk_product_lots_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_adjustments_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_from_warehouse_id",
                        column: x => x.from_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_to_warehouse_id",
                        column: x => x.to_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    expected_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                    table.CheckConstraint("ck_goods_receipt_lines_quantity", "quantity > 0 AND entered_quantity > 0");
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_goods_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "goods_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_product_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "product_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_warehouse_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "warehouse_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_hand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reserved = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    available = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false, computedColumnSql: "on_hand - reserved", stored: true),
                    last_movement_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_balances", x => x.id);
                    table.CheckConstraint("ck_inventory_balances_on_hand", "on_hand >= 0");
                    table.CheckConstraint("ck_inventory_balances_reserved", "reserved >= 0 AND reserved <= on_hand");
                    table.ForeignKey(
                        name: "fk_inventory_balances_product_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "product_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_balances_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_balances_warehouse_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "warehouse_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_balances_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustment_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity_delta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustment_lines", x => x.id);
                    table.CheckConstraint("ck_stock_adjustment_lines_delta", "quantity_delta <> 0");
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_stock_adjustments_adjustment_id",
                        column: x => x.adjustment_id,
                        principalTable: "stock_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_warehouse_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "warehouse_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                    table.CheckConstraint("ck_stock_transfer_lines_bins", "from_location_id <> to_location_id");
                    table.CheckConstraint("ck_stock_transfer_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_product_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "product_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_stock_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_warehouse_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "warehouse_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_warehouse_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "warehouse_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    balance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    on_hand_after = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    source_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movements", x => x.id);
                    table.CheckConstraint("ck_inventory_movements_on_hand_after", "on_hand_after >= 0");
                    table.CheckConstraint("ck_inventory_movements_quantity", "quantity <> 0");
                    table.ForeignKey(
                        name: "fk_inventory_movements_inventory_balances_balance_id",
                        column: x => x.balance_id,
                        principalTable: "inventory_balances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_reserved = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_consumed = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_released = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_quantities", "quantity_reserved > 0 AND quantity_consumed >= 0 AND quantity_released >= 0 AND quantity_consumed + quantity_released <= quantity_reserved");
                    table.ForeignKey(
                        name: "fk_stock_reservations_inventory_balances_balance_id",
                        column: x => x.balance_id,
                        principalTable: "inventory_balances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_sequences_warehouse_id",
                table: "document_sequences",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_location_id",
                table: "goods_receipt_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_lot_id",
                table: "goods_receipt_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_product_id",
                table: "goods_receipt_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_receipt_id_line_no",
                table: "goods_receipt_lines",
                columns: new[] { "receipt_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_number",
                table: "goods_receipts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_warehouse_id_created_at",
                table: "goods_receipts",
                columns: new[] { "warehouse_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_created_at",
                table: "idempotency_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_user_id_key",
                table: "idempotency_records",
                columns: new[] { "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_location_id",
                table: "inventory_balances",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_lot_id",
                table: "inventory_balances",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_product_id_warehouse_id",
                table: "inventory_balances",
                columns: new[] { "product_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_warehouse_id_location_id_product_id_lot_",
                table: "inventory_balances",
                columns: new[] { "warehouse_id", "location_id", "product_id", "lot_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_balance_id",
                table: "inventory_movements",
                column: "balance_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_product_id_occurred_at",
                table: "inventory_movements",
                columns: new[] { "product_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_sequence",
                table: "inventory_movements",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_source_type_source_id",
                table: "inventory_movements",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_source_type_source_line_id_type",
                table: "inventory_movements",
                columns: new[] { "source_type", "source_line_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_warehouse_id_occurred_at",
                table: "inventory_movements",
                columns: new[] { "warehouse_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_product_lots_expiry_date",
                table: "product_lots",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_product_lots_product_id_lot_no",
                table: "product_lots",
                columns: new[] { "product_id", "lot_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_adjustment_id_line_no",
                table: "stock_adjustment_lines",
                columns: new[] { "adjustment_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_location_id",
                table: "stock_adjustment_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_product_id",
                table: "stock_adjustment_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_number",
                table: "stock_adjustments",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_status",
                table: "stock_adjustments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_warehouse_id_created_at",
                table: "stock_adjustments",
                columns: new[] { "warehouse_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_balance_id",
                table: "stock_reservations",
                column: "balance_id",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_source_type_source_id",
                table: "stock_reservations",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_from_location_id",
                table: "stock_transfer_lines",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_lot_id",
                table: "stock_transfer_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_product_id",
                table: "stock_transfer_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_to_location_id",
                table: "stock_transfer_lines",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_transfer_id_line_no",
                table: "stock_transfer_lines",
                columns: new[] { "transfer_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_warehouse_id_posted_at",
                table: "stock_transfers",
                columns: new[] { "from_warehouse_id", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_number",
                table: "stock_transfers",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_to_warehouse_id_posted_at",
                table: "stock_transfers",
                columns: new[] { "to_warehouse_id", "posted_at" });

            // The stock ledger is append-only: corrections are new movements, never edits or deletes.
            migrationBuilder.Sql("""
                CREATE FUNCTION inventory_movements_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'inventory_movements is append-only (% not allowed)', TG_OP USING ERRCODE = 'restrict_violation';
                END
                $$;
                CREATE TRIGGER trg_inventory_movements_no_update_delete
                    BEFORE UPDATE OR DELETE ON inventory_movements
                    FOR EACH ROW EXECUTE FUNCTION inventory_movements_append_only();
                CREATE TRIGGER trg_inventory_movements_no_truncate
                    BEFORE TRUNCATE ON inventory_movements
                    FOR EACH STATEMENT EXECUTE FUNCTION inventory_movements_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_inventory_movements_no_truncate ON inventory_movements;
                DROP TRIGGER IF EXISTS trg_inventory_movements_no_update_delete ON inventory_movements;
                DROP FUNCTION IF EXISTS inventory_movements_append_only();
                """);

            migrationBuilder.DropTable(
                name: "document_sequences");

            migrationBuilder.DropTable(
                name: "goods_receipt_lines");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "inventory_movements");

            migrationBuilder.DropTable(
                name: "stock_adjustment_lines");

            migrationBuilder.DropTable(
                name: "stock_reservations");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "goods_receipts");

            migrationBuilder.DropTable(
                name: "stock_adjustments");

            migrationBuilder.DropTable(
                name: "inventory_balances");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropTable(
                name: "product_lots");
        }
    }
}
