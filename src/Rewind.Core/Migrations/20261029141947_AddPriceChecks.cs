using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_LateFeeCents",
                table: "StoreSettings",
                sql: "\"LateFeeCents\" BETWEEN 0 AND 9999");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_RentalPriceCents",
                table: "StoreSettings",
                sql: "\"RentalPriceCents\" BETWEEN 0 AND 9999");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_LateFeeCents",
                table: "StoreSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_RentalPriceCents",
                table: "StoreSettings");
        }
    }
}
