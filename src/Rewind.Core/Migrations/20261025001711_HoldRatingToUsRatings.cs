using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class HoldRatingToUsRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_Rating",
                table: "Movies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_Rating",
                table: "Movies",
                sql: "\"Rating\" IN ('G', 'PG', 'PG-13', 'R', 'NC-17')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_Rating",
                table: "Movies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_Rating",
                table: "Movies",
                sql: "trim(\"Rating\") NOT IN ('', 'NR')");
        }
    }
}
