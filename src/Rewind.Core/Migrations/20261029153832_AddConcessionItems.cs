using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddConcessionItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConcessionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    PriceCents = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConcessionItems", x => x.Id);
                    table.CheckConstraint("CK_ConcessionItems_Code", "\"Code\" GLOB 'CON-?*' AND \"Code\" NOT GLOB 'CON-*[^A-Z]*' AND length(\"Code\") <= 24");
                    table.CheckConstraint("CK_ConcessionItems_Name", "trim(\"Name\") <> ''");
                    table.CheckConstraint("CK_ConcessionItems_PriceCents", "\"PriceCents\" BETWEEN 0 AND 9999");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConcessionItems_Code",
                table: "ConcessionItems",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConcessionItems");
        }
    }
}
