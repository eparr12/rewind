using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddLateFeeDraws : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bag",
                table: "Members",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "LateFeeDraws",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MemberId = table.Column<int>(type: "INTEGER", nullable: false),
                    BusinessDayId = table.Column<int>(type: "INTEGER", nullable: false),
                    Card = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorMovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    DrawnAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LateFeeDraws", x => x.Id);
                    table.CheckConstraint("CK_LateFeeDraws_Card", "\"Card\" IN ('Clear', 'Overdue')");
                    table.CheckConstraint("CK_LateFeeDraws_FlavorMovie", "\"Card\" = 'Overdue' OR \"FlavorMovieId\" IS NULL");
                    table.ForeignKey(
                        name: "FK_LateFeeDraws_BusinessDays_BusinessDayId",
                        column: x => x.BusinessDayId,
                        principalTable: "BusinessDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LateFeeDraws_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LateFeeDraws_Movies_FlavorMovieId",
                        column: x => x.FlavorMovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LateFeeDraws_BusinessDayId",
                table: "LateFeeDraws",
                column: "BusinessDayId");

            migrationBuilder.CreateIndex(
                name: "IX_LateFeeDraws_FlavorMovieId",
                table: "LateFeeDraws",
                column: "FlavorMovieId");

            migrationBuilder.CreateIndex(
                name: "IX_LateFeeDraws_MemberId_BusinessDayId",
                table: "LateFeeDraws",
                columns: new[] { "MemberId", "BusinessDayId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LateFeeDraws");

            migrationBuilder.DropColumn(
                name: "Bag",
                table: "Members");
        }
    }
}
