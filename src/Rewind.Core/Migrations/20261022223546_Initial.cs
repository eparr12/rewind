using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CardBarcode = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreName = table.Column<string>(type: "TEXT", nullable: false),
                    RentalPriceCents = table.Column<int>(type: "INTEGER", nullable: false),
                    LateFeeCents = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.Id);
                    table.CheckConstraint("CK_StoreSettings_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Members_CardBarcode",
                table: "Members",
                column: "CardBarcode",
                unique: true);

            // Written by hand rather than with HasData, so the row isn't part of the model and
            // no later migration can update or delete it: the prices belong to the Store Manager.
            migrationBuilder.InsertData(
                table: "StoreSettings",
                columns: new[] { "Id", "StoreName", "RentalPriceCents", "LateFeeCents" },
                values: new object[] { 1, "Rewind Video", 200, 100 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "StoreSettings");
        }
    }
}
