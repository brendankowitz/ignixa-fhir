using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <summary>
    /// Adds IdentifierTypeSystemId and IdentifierTypeCode columns to TokenSearchParam table
    /// to support the FHIR identifier:of-type search modifier.
    /// </summary>
    public partial class AddIdentifierTypeColumnsToTokenSearchParam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add IdentifierTypeSystemId column - nullable int FK to System table
            migrationBuilder.AddColumn<int>(
                name: "IdentifierTypeSystemId",
                schema: "dbo",
                table: "TokenSearchParam",
                type: "int",
                nullable: true);

            // Add IdentifierTypeCode column - nullable varchar(256)
            migrationBuilder.AddColumn<string>(
                name: "IdentifierTypeCode",
                schema: "dbo",
                table: "TokenSearchParam",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true);

            // Create index for efficient of-type queries
            // Index on (IdentifierTypeSystemId, IdentifierTypeCode, Code) to support:
            // - Full system|code|value search
            // - Type code only search (when system is NULL)
            migrationBuilder.CreateIndex(
                name: "IX_TokenSearchParam_IdentifierType",
                schema: "dbo",
                table: "TokenSearchParam",
                columns: new[] { "ResourceTypeId", "SearchParamId", "IdentifierTypeSystemId", "IdentifierTypeCode", "Code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TokenSearchParam_IdentifierType",
                schema: "dbo",
                table: "TokenSearchParam");

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
