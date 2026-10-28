using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rewind.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Members_CardBarcode",
                table: "Members",
                sql: "\"CardBarcode\" GLOB 'MEM-[0-9][0-9][0-9]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Members_Name",
                table: "Members",
                sql: "trim(\"Name\") <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Members_CardBarcode",
                table: "Members");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Members_Name",
                table: "Members");
        }
    }
}
