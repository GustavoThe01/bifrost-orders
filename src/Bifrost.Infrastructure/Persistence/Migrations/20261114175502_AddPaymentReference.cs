using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bifrost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                table: "orders",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_reference",
                table: "orders");
        }
    }
}
