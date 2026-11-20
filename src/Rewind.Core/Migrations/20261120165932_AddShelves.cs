using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddShelves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shelves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MemberId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    FromMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    FromDay = table.Column<int>(type: "INTEGER", nullable: true),
                    ToMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    ToDay = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shelves", x => x.Id);
                    table.CheckConstraint("CK_Shelves_From", "\"FromMonth\" BETWEEN 1 AND 12 AND \"FromDay\" BETWEEN 1 AND CASE \"FromMonth\" WHEN 1 THEN 31 WHEN 2 THEN 29 WHEN 3 THEN 31 WHEN 4 THEN 30 WHEN 5 THEN 31 WHEN 6 THEN 30 WHEN 7 THEN 31 WHEN 8 THEN 31 WHEN 9 THEN 30 WHEN 10 THEN 31 WHEN 11 THEN 30 WHEN 12 THEN 31 END");
                    table.CheckConstraint("CK_Shelves_Kind", "(\"MemberId\" IS NOT NULL AND \"Name\" IS NULL AND \"FromMonth\" IS NULL AND \"FromDay\" IS NULL AND \"ToMonth\" IS NULL AND \"ToDay\" IS NULL AND \"IsActive\") OR (\"MemberId\" IS NULL AND \"Name\" IS NOT NULL AND \"FromMonth\" IS NOT NULL AND \"FromDay\" IS NOT NULL AND \"ToMonth\" IS NOT NULL AND \"ToDay\" IS NOT NULL)");
                    table.CheckConstraint("CK_Shelves_Name", "trim(\"Name\") <> ''");
                    table.CheckConstraint("CK_Shelves_To", "\"ToMonth\" BETWEEN 1 AND 12 AND \"ToDay\" BETWEEN 1 AND CASE \"ToMonth\" WHEN 1 THEN 31 WHEN 2 THEN 29 WHEN 3 THEN 31 WHEN 4 THEN 30 WHEN 5 THEN 31 WHEN 6 THEN 30 WHEN 7 THEN 31 WHEN 8 THEN 31 WHEN 9 THEN 30 WHEN 10 THEN 31 WHEN 11 THEN 30 WHEN 12 THEN 31 END");
                    table.ForeignKey(
                        name: "FK_Shelves_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShelfMovies",
                columns: table => new
                {
                    ShelfId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShelfMovies", x => new { x.ShelfId, x.MovieId });
                    table.ForeignKey(
                        name: "FK_ShelfMovies_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShelfMovies_Shelves_ShelfId",
                        column: x => x.ShelfId,
                        principalTable: "Shelves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShelfMovies_MovieId",
                table: "ShelfMovies",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_MemberId",
                table: "Shelves",
                column: "MemberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_Name",
                table: "Shelves",
                column: "Name",
                unique: true);

            // Added by hand, after the tables and their indexes: every member has a Staff Picks shelf from the save
            // that adds them (ADR-0015), so each member already in the database gets theirs here, active or not, in
            // the members' order. It moves data and holds no rule, which raw SQL in a migration may still do
            // (ADR-0009): the rules it meets, such as one shelf for each member, are in the model, and IsActive is 1
            // because CK_Shelves_Kind holds a member's shelf to it.
            migrationBuilder.Sql(
                "INSERT INTO \"Shelves\" (\"MemberId\", \"IsActive\") SELECT \"Id\", 1 FROM \"Members\" ORDER BY \"Id\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShelfMovies");

            migrationBuilder.DropTable(
                name: "Shelves");
        }
    }
}
