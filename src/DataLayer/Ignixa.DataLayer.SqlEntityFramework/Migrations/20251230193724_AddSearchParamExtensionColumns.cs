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
            migrationBuilder.DropIndex(
                name: "IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1",
                table: "TokenQuantityCompositeSearchParam");

            migrationBuilder.RenameColumn(
                name: "SingleValue",
                table: "TokenQuantityCompositeSearchParam",
                newName: "SingleValue2");

            migrationBuilder.RenameColumn(
                name: "QuantityCodeId",
                table: "TokenQuantityCompositeSearchParam",
                newName: "QuantityCodeId2");

            migrationBuilder.RenameColumn(
                name: "LowValue",
                table: "TokenQuantityCompositeSearchParam",
                newName: "LowValue2");

            migrationBuilder.RenameColumn(
                name: "HighValue",
                table: "TokenQuantityCompositeSearchParam",
                newName: "HighValue2");

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

            migrationBuilder.CreateIndex(
                name: "IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1",
                table: "TokenQuantityCompositeSearchParam",
                columns: new[] { "SearchParamId", "Code1" })
                .Annotation("SqlServer:Include", new[] { "SystemId1", "SystemId2", "QuantityCodeId2", "SingleValue2", "LowValue2", "HighValue2" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1",
                table: "TokenQuantityCompositeSearchParam");

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

            migrationBuilder.RenameColumn(
                name: "SingleValue2",
                table: "TokenQuantityCompositeSearchParam",
                newName: "SingleValue");

            migrationBuilder.RenameColumn(
                name: "QuantityCodeId2",
                table: "TokenQuantityCompositeSearchParam",
                newName: "QuantityCodeId");

            migrationBuilder.RenameColumn(
                name: "LowValue2",
                table: "TokenQuantityCompositeSearchParam",
                newName: "LowValue");

            migrationBuilder.RenameColumn(
                name: "HighValue2",
                table: "TokenQuantityCompositeSearchParam",
                newName: "HighValue");

            migrationBuilder.CreateIndex(
                name: "IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1",
                table: "TokenQuantityCompositeSearchParam",
                columns: new[] { "SearchParamId", "Code1" })
                .Annotation("SqlServer:Include", new[] { "SystemId1", "SystemId2", "QuantityCodeId", "SingleValue", "LowValue", "HighValue" });
        }
    }
}
