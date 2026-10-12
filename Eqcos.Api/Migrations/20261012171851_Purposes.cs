using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class Purposes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurposeId",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Purposes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    AdminOnly = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purposes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Purposes",
                columns: new[] { "Id", "AdminOnly", "Minutes", "Name" },
                values: new object[,]
                {
                    { 1, false, 5, "Подпись заявления на уход с пар" },
                    { 2, false, 5, "Направление на исправление оценки" },
                    { 3, false, 5, "Форма №6" },
                    { 4, true, 30, "Профилактическая беседа (вызов)" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Purposes");

            migrationBuilder.DropColumn(
                name: "PurposeId",
                table: "Bookings");
        }
    }
}
