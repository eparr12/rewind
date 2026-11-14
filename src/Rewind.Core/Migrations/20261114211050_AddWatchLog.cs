using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "RentalItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WatchLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RentalItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    MemberId = table.Column<int>(type: "INTEGER", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchLog", x => x.Id);
                    table.CheckConstraint("CK_WatchLog_Stars", "\"Stars\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_WatchLog_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WatchLog_RentalItems_RentalItemId",
                        column: x => x.RentalItemId,
                        principalTable: "RentalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RentalItems_CaseId",
                table: "RentalItems",
                column: "CaseId",
                unique: true,
                filter: "\"ReturnedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WatchLog_MemberId",
                table: "WatchLog",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchLog_RentalItemId_MemberId",
                table: "WatchLog",
                columns: new[] { "RentalItemId", "MemberId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchLog");

            migrationBuilder.DropIndex(
                name: "IX_RentalItems_CaseId",
                table: "RentalItems");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "RentalItems");
        }
    }
}
