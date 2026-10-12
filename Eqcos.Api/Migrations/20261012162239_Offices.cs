using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class Offices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OfficeId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "Bookings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "OfficeId",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Offices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Open = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Close = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    BreakStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    BreakEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offices", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Offices",
                columns: new[] { "Id", "BreakEnd", "BreakStart", "Close", "Name", "Open" },
                values: new object[,]
                {
                    { 1, new TimeOnly(13, 0, 0), new TimeOnly(11, 0, 0), new TimeOnly(16, 30, 0), "Главный корпус", new TimeOnly(8, 0, 0) },
                    { 2, new TimeOnly(13, 0, 0), new TimeOnly(11, 0, 0), new TimeOnly(16, 30, 0), "Лабораторный корпус", new TimeOnly(8, 0, 0) },
                    { 3, new TimeOnly(13, 0, 0), new TimeOnly(11, 0, 0), new TimeOnly(16, 30, 0), "Второй корпус", new TimeOnly(8, 0, 0) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Offices");

            migrationBuilder.DropColumn(
                name: "OfficeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OfficeId",
                table: "Bookings");
        }
    }
}
