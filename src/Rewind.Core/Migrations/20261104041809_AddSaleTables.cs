using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Rentals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MemberId = table.Column<int>(type: "INTEGER", nullable: false),
                    BusinessDayId = table.Column<int>(type: "INTEGER", nullable: false),
                    RentedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalCents = table.Column<int>(type: "INTEGER", nullable: false),
                    CashTenderedCents = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeCents = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rentals", x => x.Id);
                    table.CheckConstraint("CK_Rentals_CashTenderedCents", "\"CashTenderedCents\" >= \"TotalCents\"");
                    table.CheckConstraint("CK_Rentals_ChangeCents", "\"ChangeCents\" = \"CashTenderedCents\" - \"TotalCents\"");
                    table.CheckConstraint("CK_Rentals_TotalCents", "\"TotalCents\" >= 0");
                    table.ForeignKey(
                        name: "FK_Rentals_BusinessDays_BusinessDayId",
                        column: x => x.BusinessDayId,
                        principalTable: "BusinessDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rentals_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RentalId = table.Column<int>(type: "INTEGER", nullable: false),
                    CaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    PriceCents = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalItems", x => x.Id);
                    table.CheckConstraint("CK_RentalItems_PriceCents", "\"PriceCents\" BETWEEN 0 AND 9999");
                    table.ForeignKey(
                        name: "FK_RentalItems_CaseMovies_CaseId_MovieId",
                        columns: x => new { x.CaseId, x.MovieId },
                        principalTable: "CaseMovies",
                        principalColumns: new[] { "CaseId", "MovieId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RentalItems_Rentals_RentalId",
                        column: x => x.RentalId,
                        principalTable: "Rentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RentalId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PriceCents = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcessionItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    LateFeeDrawId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.CheckConstraint("CK_SaleLines_Name", "trim(\"Name\") <> '' AND length(\"Name\") <= 217");
                    table.CheckConstraint("CK_SaleLines_PriceCents", "\"PriceCents\" BETWEEN 0 AND 9999");
                    table.CheckConstraint("CK_SaleLines_Source", "(\"ConcessionItemId\" IS NULL) <> (\"LateFeeDrawId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_SaleLines_ConcessionItems_ConcessionItemId",
                        column: x => x.ConcessionItemId,
                        principalTable: "ConcessionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_LateFeeDraws_LateFeeDrawId",
                        column: x => x.LateFeeDrawId,
                        principalTable: "LateFeeDraws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_Rentals_RentalId",
                        column: x => x.RentalId,
                        principalTable: "Rentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RentalItems_CaseId_MovieId",
                table: "RentalItems",
                columns: new[] { "CaseId", "MovieId" });

            migrationBuilder.CreateIndex(
                name: "IX_RentalItems_RentalId_CaseId",
                table: "RentalItems",
                columns: new[] { "RentalId", "CaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_BusinessDayId",
                table: "Rentals",
                column: "BusinessDayId");

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_MemberId",
                table: "Rentals",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ConcessionItemId",
                table: "SaleLines",
                column: "ConcessionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_LateFeeDrawId",
                table: "SaleLines",
                column: "LateFeeDrawId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_RentalId",
                table: "SaleLines",
                column: "RentalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RentalItems");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "Rentals");
        }
    }
}
