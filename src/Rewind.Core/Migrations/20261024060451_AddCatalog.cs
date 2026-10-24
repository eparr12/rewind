using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreLabel = table.Column<string>(type: "TEXT", nullable: false),
                    FactoryUpc = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                    table.CheckConstraint("CK_Cases_FactoryUpc", "length(\"FactoryUpc\") IN (12, 13) AND \"FactoryUpc\" NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_Cases_Status", "\"Status\" IN ('OnShelf', 'CheckedOut', 'Retired')");
                    table.CheckConstraint("CK_Cases_StoreLabel", "\"StoreLabel\" GLOB 'RV-[0-9][0-9][0-9][0-9][0-9]'");
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                    table.CheckConstraint("CK_Genres_Name", "trim(\"Name\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    Rating = table.Column<string>(type: "TEXT", nullable: true),
                    Director = table.Column<string>(type: "TEXT", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    PosterFile = table.Column<string>(type: "TEXT", nullable: true),
                    TmdbFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.CheckConstraint("CK_Movies_Director", "trim(\"Director\") <> ''");
                    table.CheckConstraint("CK_Movies_Overview", "trim(\"Overview\") <> ''");
                    table.CheckConstraint("CK_Movies_PosterFile", "trim(\"PosterFile\") <> ''");
                    table.CheckConstraint("CK_Movies_Rating", "trim(\"Rating\") NOT IN ('', 'NR')");
                    table.CheckConstraint("CK_Movies_RuntimeMinutes", "\"RuntimeMinutes\" > 0");
                    table.CheckConstraint("CK_Movies_Title", "trim(\"Title\") <> ''");
                    table.CheckConstraint("CK_Movies_Year", "\"Year\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "UpcLookups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Upc = table.Column<string>(type: "TEXT", nullable: false),
                    ProductTitle = table.Column<string>(type: "TEXT", nullable: true),
                    LookedUpAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpcLookups", x => x.Id);
                    table.CheckConstraint("CK_UpcLookups_ProductTitle", "trim(\"ProductTitle\") <> ''");
                    table.CheckConstraint("CK_UpcLookups_Upc", "length(\"Upc\") IN (12, 13) AND \"Upc\" NOT GLOB '*[^0-9]*'");
                });

            migrationBuilder.CreateTable(
                name: "CaseMovies",
                columns: table => new
                {
                    CaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseMovies", x => new { x.CaseId, x.MovieId });
                    table.ForeignKey(
                        name: "FK_CaseMovies_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaseMovies_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovieCast",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BillingOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieCast", x => x.Id);
                    table.CheckConstraint("CK_MovieCast_Name", "trim(\"Name\") <> ''");
                    table.ForeignKey(
                        name: "FK_MovieCast_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovieGenres",
                columns: table => new
                {
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    GenreId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieGenres", x => new { x.MovieId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_MovieGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovieGenres_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseMovies_MovieId",
                table: "CaseMovies",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_FactoryUpc",
                table: "Cases",
                column: "FactoryUpc");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_StoreLabel",
                table: "Cases",
                column: "StoreLabel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_TmdbId",
                table: "Genres",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieCast_MovieId_BillingOrder",
                table: "MovieCast",
                columns: new[] { "MovieId", "BillingOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieGenres_GenreId",
                table: "MovieGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UpcLookups_Upc",
                table: "UpcLookups",
                column: "Upc",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseMovies");

            migrationBuilder.DropTable(
                name: "MovieCast");

            migrationBuilder.DropTable(
                name: "MovieGenres");

            migrationBuilder.DropTable(
                name: "UpcLookups");

            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Movies");
        }
    }
}
