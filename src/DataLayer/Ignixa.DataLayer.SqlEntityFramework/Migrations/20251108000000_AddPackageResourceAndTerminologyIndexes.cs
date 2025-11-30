using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageResourceAndTerminologyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create PackageResource table for FHIR NPM package conformance resources (ADR-2532)
            migrationBuilder.CreateTable(
                name: "PackageResource",
                schema: "dbo",
                columns: table => new
                {
                    PackageResourceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PackageVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Canonical = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FhirVersion = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LoadedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageResource", x => x.PackageResourceId);
                });

            // PackageResource indexes
            // Unique constraint on PackageId + PackageVersion + ResourceType + ResourceId
            // Multiple resources can share the same canonical URL within a package
            migrationBuilder.CreateIndex(
                name: "UQ_PackageResource_Identity",
                schema: "dbo",
                table: "PackageResource",
                columns: new[] { "PackageId", "PackageVersion", "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageResource_Canonical_Version",
                schema: "dbo",
                table: "PackageResource",
                columns: new[] { "Canonical", "Version" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PackageResource_ResourceType_Canonical",
                schema: "dbo",
                table: "PackageResource",
                columns: new[] { "ResourceType", "Canonical" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PackageResource_Package",
                schema: "dbo",
                table: "PackageResource",
                columns: new[] { "PackageId", "PackageVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageResource_LoadedDate",
                schema: "dbo",
                table: "PackageResource",
                column: "LoadedDate");

            // Strategic Terminology Indexes on TokenSearchParam (ADR-2531)
            // These indexes may already exist from previous migrations
            // Check and create only if they don't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenSearchParam_SearchParamId_SystemId_Code' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
                BEGIN
                    CREATE INDEX [IX_TokenSearchParam_SearchParamId_SystemId_Code]
                    ON [dbo].[TokenSearchParam] ([SearchParamId], [SystemId], [Code])
                    INCLUDE ([ResourceTypeId], [ResourceSurrogateId])
                    WHERE [SystemId] IS NOT NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenSearchParam_SystemId_Code' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
                BEGIN
                    CREATE INDEX [IX_TokenSearchParam_SystemId_Code]
                    ON [dbo].[TokenSearchParam] ([SystemId], [Code])
                    INCLUDE ([ResourceTypeId], [ResourceSurrogateId])
                    WHERE [SystemId] IS NOT NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenSearchParam_ResourceTypeId_SearchParamId' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
                BEGIN
                    CREATE INDEX [IX_TokenSearchParam_ResourceTypeId_SearchParamId]
                    ON [dbo].[TokenSearchParam] ([ResourceTypeId], [SearchParamId])
                    INCLUDE ([SystemId], [Code]);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop terminology indexes
            migrationBuilder.DropIndex(
                name: "IX_TokenSearchParam_ResourceTypeId_SearchParamId",
                schema: "dbo",
                table: "TokenSearchParam");

            migrationBuilder.DropIndex(
                name: "IX_TokenSearchParam_SystemId_Code",
                schema: "dbo",
                table: "TokenSearchParam");

            migrationBuilder.DropIndex(
                name: "IX_TokenSearchParam_SearchParamId_SystemId_Code",
                schema: "dbo",
                table: "TokenSearchParam");

            // Drop PackageResource table
            migrationBuilder.DropTable(
                name: "PackageResource",
                schema: "dbo");
        }
    }
}
#pragma warning restore CA1861
