using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bifrost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "products");
        }
    }
}
