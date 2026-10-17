using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eqcos.Api.Migrations
{
    /// <inheritdoc />
    public partial class Fixes24b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DataLocked",
                table: "Users",
                newName: "Blocked");

            migrationBuilder.AddColumn<DateTime>(
                name: "DataUntil",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Warning",
                table: "Notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Warning",
                table: "Notifications");

            migrationBuilder.RenameColumn(
                name: "Blocked",
                table: "Users",
                newName: "DataLocked");
        }
    }
}
