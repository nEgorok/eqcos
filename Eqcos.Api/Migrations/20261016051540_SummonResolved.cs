using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class SummonResolved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Resolved",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Resolved",
                table: "Bookings");
        }
    }
}
