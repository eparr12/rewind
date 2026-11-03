using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class HoldBagToItsCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Members_Bag",
                table: "Members",
                sql: "length(\"Bag\") <= 4 AND \"Bag\" NOT GLOB '*[^CO]*' AND \"Bag\" NOT GLOB '*O*O*'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Members_Bag",
                table: "Members");
        }
    }
}
