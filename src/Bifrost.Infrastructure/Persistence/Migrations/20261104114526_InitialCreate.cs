using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bifrost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    customer_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    shipping_street = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    shipping_number = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    shipping_city = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    shipping_state = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    shipping_zip_code = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    cancellation_reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sku = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    stock_quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    price_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.CheckConstraint("ck_products_stock", "stock_quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    product_name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    unit_price_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => new { x.order_id, x.product_id });
                    table.CheckConstraint("ck_order_items_quantity", "quantity BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_product_id",
                table: "order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_sku",
                table: "products",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
