using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // Prefer static readonly fields over constant array arguments (auto-generated migration code)
#pragma warning disable IDE0161 // Use file-scoped namespace (auto-generated migration code)

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminologyImportTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: 97.sql already creates SearchParam table with correct schema (Uri=128, Status, LastUpdated, PKC_SearchParam)
            // We only need to verify the schema matches our expectations, not recreate it

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                schema: "dbo",
                table: "PackageResource",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImportCompletedDate",
                schema: "dbo",
                table: "PackageResource",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportErrorMessage",
                schema: "dbo",
                table: "PackageResource",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImportStartDate",
                schema: "dbo",
                table: "PackageResource",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportedConceptCount",
                schema: "dbo",
                table: "PackageResource",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminologyImportStatus",
                schema: "dbo",
                table: "PackageResource",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // NOTE: PKC_SearchParam already exists from 97.sql - no need to recreate

            migrationBuilder.CreateTable(
                name: "TermCodeSystem",
                schema: "dbo",
                columns: table => new
                {
                    TermCodeSystemId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageResourceId = table.Column<long>(type: "bigint", nullable: false),
                    SystemId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConceptCount = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsHierarchical = table.Column<bool>(type: "bit", nullable: false),
                    CaseSensitive = table.Column<bool>(type: "bit", nullable: false),
                    Compositional = table.Column<bool>(type: "bit", nullable: false),
                    ImportedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermCodeSystem", x => x.TermCodeSystemId);
                    table.ForeignKey(
                        name: "FK_TermCodeSystem_PackageResource",
                        column: x => x.PackageResourceId,
                        principalSchema: "dbo",
                        principalTable: "PackageResource",
                        principalColumn: "PackageResourceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TermCodeSystem_System",
                        column: x => x.SystemId,
                        principalTable: "System",
                        principalColumn: "SystemId");
                });

            migrationBuilder.CreateTable(
                name: "TermConceptMap",
                schema: "dbo",
                columns: table => new
                {
                    TermConceptMapId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageResourceId = table.Column<long>(type: "bigint", nullable: false),
                    Canonical = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceCanonical = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TargetCanonical = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ImportedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermConceptMap", x => x.TermConceptMapId);
                    table.ForeignKey(
                        name: "FK_TermConceptMap_PackageResource",
                        column: x => x.PackageResourceId,
                        principalSchema: "dbo",
                        principalTable: "PackageResource",
                        principalColumn: "PackageResourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TermValueSet",
                schema: "dbo",
                columns: table => new
                {
                    TermValueSetId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageResourceId = table.Column<long>(type: "bigint", nullable: false),
                    Canonical = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Immutable = table.Column<bool>(type: "bit", nullable: false),
                    IsExpanded = table.Column<bool>(type: "bit", nullable: false),
                    LastExpansionDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpansionCodeCount = table.Column<int>(type: "int", nullable: true),
                    IsPartialExpansion = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PartialExpansionReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ImportedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermValueSet", x => x.TermValueSetId);
                    table.ForeignKey(
                        name: "FK_TermValueSet_PackageResource",
                        column: x => x.PackageResourceId,
                        principalSchema: "dbo",
                        principalTable: "PackageResource",
                        principalColumn: "PackageResourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TermConcept",
                schema: "dbo",
                columns: table => new
                {
                    TermConceptId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TermCodeSystemId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Display = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Definition = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ParentConceptId = table.Column<long>(type: "bigint", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PropertiesJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermConcept", x => x.TermConceptId);
                    table.ForeignKey(
                        name: "FK_TermConcept_CodeSystem",
                        column: x => x.TermCodeSystemId,
                        principalSchema: "dbo",
                        principalTable: "TermCodeSystem",
                        principalColumn: "TermCodeSystemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TermConcept_Parent",
                        column: x => x.ParentConceptId,
                        principalSchema: "dbo",
                        principalTable: "TermConcept",
                        principalColumn: "TermConceptId");
                });

            migrationBuilder.CreateTable(
                name: "TermConceptMapElement",
                schema: "dbo",
                columns: table => new
                {
                    TermConceptMapElementId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TermConceptMapId = table.Column<long>(type: "bigint", nullable: false),
                    SourceSystemId = table.Column<int>(type: "int", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceDisplay = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetSystemId = table.Column<int>(type: "int", nullable: true),
                    TargetCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TargetDisplay = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Equivalence = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GroupIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermConceptMapElement", x => x.TermConceptMapElementId);
                    table.ForeignKey(
                        name: "FK_TermConceptMapElement_ConceptMap",
                        column: x => x.TermConceptMapId,
                        principalSchema: "dbo",
                        principalTable: "TermConceptMap",
                        principalColumn: "TermConceptMapId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TermConceptMapElement_SourceSystem",
                        column: x => x.SourceSystemId,
                        principalTable: "System",
                        principalColumn: "SystemId");
                    table.ForeignKey(
                        name: "FK_TermConceptMapElement_TargetSystem",
                        column: x => x.TargetSystemId,
                        principalTable: "System",
                        principalColumn: "SystemId");
                });

            migrationBuilder.CreateTable(
                name: "TermValueSetExpansion",
                schema: "dbo",
                columns: table => new
                {
                    TermValueSetExpansionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TermValueSetId = table.Column<long>(type: "bigint", nullable: false),
                    SystemId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Display = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SystemVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermValueSetExpansion", x => x.TermValueSetExpansionId);
                    table.ForeignKey(
                        name: "FK_TermValueSetExpansion_System",
                        column: x => x.SystemId,
                        principalTable: "System",
                        principalColumn: "SystemId");
                    table.ForeignKey(
                        name: "FK_TermValueSetExpansion_ValueSet",
                        column: x => x.TermValueSetId,
                        principalSchema: "dbo",
                        principalTable: "TermValueSet",
                        principalColumn: "TermValueSetId",
                        onDelete: ReferentialAction.Cascade);
                });

            // NOTE: UQ_SearchParam_SearchParamId already exists from 97.sql
            // NOTE: UQ_PackageResource_Identity already exists from 20251108000000_AddPackageResourceAndTerminologyIndexes.cs

            migrationBuilder.CreateIndex(
                name: "IX_TermCodeSystem_PackageResourceId",
                schema: "dbo",
                table: "TermCodeSystem",
                column: "PackageResourceId");

            migrationBuilder.CreateIndex(
                name: "UQ_TermCodeSystem_System_Version",
                schema: "dbo",
                table: "TermCodeSystem",
                columns: new[] { "SystemId", "Version" },
                unique: true,
                filter: "[Version] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TermConcept_CodeSystem_Code_Active",
                schema: "dbo",
                table: "TermConcept",
                columns: new[] { "TermCodeSystemId", "Code", "IsActive" })
                .Annotation("SqlServer:Include", new[] { "Display", "Definition" });

            migrationBuilder.CreateIndex(
                name: "IX_TermConcept_Display",
                schema: "dbo",
                table: "TermConcept",
                column: "Display",
                filter: "[Display] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "TermCodeSystemId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_TermConcept_Parent",
                schema: "dbo",
                table: "TermConcept",
                columns: new[] { "ParentConceptId", "Level" },
                filter: "[ParentConceptId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Code", "Display" });

            migrationBuilder.CreateIndex(
                name: "UQ_TermConcept_CodeSystem_Code",
                schema: "dbo",
                table: "TermConcept",
                columns: new[] { "TermCodeSystemId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TermConceptMap_PackageResourceId",
                schema: "dbo",
                table: "TermConceptMap",
                column: "PackageResourceId");

            migrationBuilder.CreateIndex(
                name: "UQ_TermConceptMap_Canonical_Version",
                schema: "dbo",
                table: "TermConceptMap",
                columns: new[] { "Canonical", "Version" },
                unique: true,
                filter: "[Version] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TermConceptMapElement_Source",
                schema: "dbo",
                table: "TermConceptMapElement",
                columns: new[] { "SourceSystemId", "SourceCode" })
                .Annotation("SqlServer:Include", new[] { "TermConceptMapId", "TargetSystemId", "TargetCode", "Equivalence" });

            migrationBuilder.CreateIndex(
                name: "IX_TermConceptMapElement_Target",
                schema: "dbo",
                table: "TermConceptMapElement",
                columns: new[] { "TargetSystemId", "TargetCode" },
                filter: "[TargetSystemId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "TermConceptMapId", "SourceSystemId", "SourceCode", "Equivalence" });

            migrationBuilder.CreateIndex(
                name: "IX_TermConceptMapElement_TermConceptMapId",
                schema: "dbo",
                table: "TermConceptMapElement",
                column: "TermConceptMapId");

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSet_Canonical",
                schema: "dbo",
                table: "TermValueSet",
                column: "Canonical")
                .Annotation("SqlServer:Include", new[] { "Version", "IsExpanded" });

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSet_Expanded",
                schema: "dbo",
                table: "TermValueSet",
                column: "IsExpanded",
                filter: "[IsExpanded] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSet_PackageResourceId",
                schema: "dbo",
                table: "TermValueSet",
                column: "PackageResourceId");

            migrationBuilder.CreateIndex(
                name: "UQ_TermValueSet_Canonical_Version",
                schema: "dbo",
                table: "TermValueSet",
                columns: new[] { "Canonical", "Version" },
                unique: true,
                filter: "[Version] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSetExpansion_Display",
                schema: "dbo",
                table: "TermValueSetExpansion",
                column: "Display",
                filter: "[Display] IS NOT NULL AND [IsActive] = 1")
                .Annotation("SqlServer:Include", new[] { "TermValueSetId", "SystemId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSetExpansion_SystemId",
                schema: "dbo",
                table: "TermValueSetExpansion",
                column: "SystemId");

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSetExpansion_ValueSet_Ordinal",
                schema: "dbo",
                table: "TermValueSetExpansion",
                columns: new[] { "TermValueSetId", "Ordinal" },
                filter: "[IsActive] = 1")
                .Annotation("SqlServer:Include", new[] { "SystemId", "Code", "Display" });

            migrationBuilder.CreateIndex(
                name: "IX_TermValueSetExpansion_ValueSet_System_Code",
                schema: "dbo",
                table: "TermValueSetExpansion",
                columns: new[] { "TermValueSetId", "SystemId", "Code" },
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TermConcept",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TermConceptMapElement",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TermValueSetExpansion",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TermCodeSystem",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TermConceptMap",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TermValueSet",
                schema: "dbo");

            // NOTE: SearchParam table and its constraints are base schema from 97.sql - don't modify
            // NOTE: UQ_PackageResource_Identity index belongs to 20251108000000_AddPackageResourceAndTerminologyIndexes.cs - don't drop here
            // NOTE: LastUpdated and Status columns already exist in SearchParam from 97.sql - don't drop

            migrationBuilder.DropColumn(
                name: "ContentHash",
                schema: "dbo",
                table: "PackageResource");

            migrationBuilder.DropColumn(
                name: "ImportCompletedDate",
                schema: "dbo",
                table: "PackageResource");

            migrationBuilder.DropColumn(
                name: "ImportErrorMessage",
                schema: "dbo",
                table: "PackageResource");

            migrationBuilder.DropColumn(
                name: "ImportStartDate",
                schema: "dbo",
                table: "PackageResource");

            migrationBuilder.DropColumn(
                name: "ImportedConceptCount",
                schema: "dbo",
                table: "PackageResource");

            migrationBuilder.DropColumn(
                name: "TerminologyImportStatus",
                schema: "dbo",
                table: "PackageResource");

            // NOTE: SearchParam schema (Uri length, PK, indexes) is base schema from 97.sql - don't alter
        }
    }
}
#pragma warning restore CA1861
#pragma warning restore IDE0161
