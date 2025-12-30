using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchParamExtensionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add extension columns to UriSearchParam for :above and :below modifier support
            migrationBuilder.AddColumn<string>(
                name: "Fragment",
                schema: "dbo",
                table: "UriSearchParam",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                schema: "dbo",
                table: "UriSearchParam",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Add extension columns to TokenSearchParam for :of-type modifier support
            migrationBuilder.AddColumn<string>(
                name: "IdentifierTypeCode",
                schema: "dbo",
                table: "TokenSearchParam",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdentifierTypeSystemId",
                schema: "dbo",
                table: "TokenSearchParam",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fragment",
                schema: "dbo",
                table: "UriSearchParam");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "dbo",
                table: "UriSearchParam");

            migrationBuilder.DropColumn(
                name: "IdentifierTypeCode",
                schema: "dbo",
                table: "TokenSearchParam");

            migrationBuilder.DropColumn(
                name: "IdentifierTypeSystemId",
                schema: "dbo",
                table: "TokenSearchParam");
        }
    }
}
