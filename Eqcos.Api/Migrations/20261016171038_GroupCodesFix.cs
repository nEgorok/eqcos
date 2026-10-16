using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class GroupCodesFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupCodes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "GroupCodes",
                columns: new[] { "Id", "Code" },
                values: new object[,]
                {
                    { 1, "А" },
                    { 2, "Б" },
                    { 3, "Бп" },
                    { 4, "В" },
                    { 5, "К" },
                    { 6, "Кп" },
                    { 7, "Л" },
                    { 8, "Лг" },
                    { 9, "М" },
                    { 10, "Н" },
                    { 11, "Нп" },
                    { 12, "П" },
                    { 13, "Св" },
                    { 14, "Тда" },
                    { 15, "Тэ" },
                    { 16, "Х" },
                    { 17, "Э" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupCodes");
        }
    }
}
