using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_supplier_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_stock_qty",
                table: "products",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_unit_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_order_id",
                table: "goods_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_order_line_id",
                table: "goods_receipt_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "low_stock_alert_checked_on",
                table: "company_profile",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "low_stock_alert_emails",
                table: "company_profile",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.CheckConstraint("ck_suppliers_code_upper", "code = upper(code)");
                    table.CheckConstraint("ck_suppliers_lead_time", "lead_time_days IS NULL OR lead_time_days BETWEEN 0 AND 365");
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ordered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ordered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_order_lines_quantity", "quantity > 0 AND entered_quantity > 0");
                    table.CheckConstraint("ck_purchase_order_lines_received", "received_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_default_supplier_id",
                table: "products",
                column: "default_supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_purchase_unit_id",
                table: "products",
                column: "purchase_unit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_max_stock",
                table: "products",
                sql: "max_stock_qty IS NULL OR max_stock_qty >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_purchase_order_id",
                table: "goods_receipts",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_purchase_order_line_id",
                table: "goods_receipt_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_product_id",
                table: "purchase_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_line_no",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_product_id",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_number",
                table: "purchase_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_supplier_id",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_warehouse_id_created_at",
                table: "purchase_orders",
                columns: new[] { "warehouse_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_warehouse_id_status",
                table: "purchase_orders",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_code",
                table: "suppliers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_name",
                table: "suppliers",
                column: "name");

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_purchase_order_line_purchase_order_line",
                table: "goods_receipt_lines",
                column: "purchase_order_line_id",
                principalTable: "purchase_order_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_purchase_orders_purchase_order_id",
                table: "goods_receipts",
                column: "purchase_order_id",
                principalTable: "purchase_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_products_suppliers_default_supplier_id",
                table: "products",
                column: "default_supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_products_units_of_measure_purchase_unit_id",
                table: "products",
                column: "purchase_unit_id",
                principalTable: "units_of_measure",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_purchase_order_line_purchase_order_line",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipts_purchase_orders_purchase_order_id",
                table: "goods_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_products_suppliers_default_supplier_id",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "fk_products_units_of_measure_purchase_unit_id",
                table: "products");

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropIndex(
                name: "ix_products_default_supplier_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_purchase_unit_id",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_max_stock",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_purchase_order_id",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_purchase_order_line_id",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "default_supplier_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "max_stock_qty",
                table: "products");

            migrationBuilder.DropColumn(
                name: "purchase_unit_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "purchase_order_id",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "purchase_order_line_id",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "low_stock_alert_checked_on",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "low_stock_alert_emails",
                table: "company_profile");
        }
    }
}
