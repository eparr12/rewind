using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false, computedColumnSql: "\"ClosedAt\" IS NULL", stored: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDays_IsOpen",
                table: "BusinessDays",
                column: "IsOpen",
                unique: true,
                filter: "\"IsOpen\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessDays");
        }
    }
}
