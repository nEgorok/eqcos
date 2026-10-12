using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenamePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Purposes",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Профилактическая беседа");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Purposes",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Профилактическая беседа (вызов)");
        }
    }
}
