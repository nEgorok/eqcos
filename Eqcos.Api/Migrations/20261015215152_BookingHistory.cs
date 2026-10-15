using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class BookingHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Minutes",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Bookings",
                type: "text",
                nullable: false,
                defaultValue: "");
            // старым записям: статус «действует», длительность — из их цели визита
            migrationBuilder.Sql("UPDATE \"Bookings\" SET \"Status\" = 'Active'");
            migrationBuilder.Sql("UPDATE \"Bookings\" b SET \"Minutes\" = p.\"Minutes\" FROM \"Purposes\" p WHERE p.\"Id\" = b.\"PurposeId\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Minutes",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Bookings");
        }
    }
}
