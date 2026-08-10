// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql.Generators;
using Shouldly;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards against <c>src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/*.sql</c> -- the DDL
/// <see cref="SqlCatalogGenerator"/> reads to derive column widths the SQL compiler trusts (see
/// <see cref="Ignixa.Search.Sql.TokenColumnEquality"/> and <c>SearchParamColumnWidths</c>) -- silently
/// drifting from <c>src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql</c>, the script
/// that actually provisions the database. Nothing else checks the two agree: the generator only parses
/// whichever DDL it is pointed at, so a column renamed or re-widened in one file and not the other would
/// build clean and silently emit SQL against the wrong shape.
/// </summary>
/// <remarks>
/// <c>97.sql</c> is the base schema; a handful of columns arrive afterward via EF migrations that alter
/// tables already in <c>97.sql</c> (<see cref="KnownExtensionColumns"/>), and a handful of whole tables
/// arrive only via migrations that were never folded into <c>97.sql</c> (<see
/// cref="TablesNotInBaseSchema"/>). Both lists are closed sets tied to a specific migration file -- a
/// table or column showing up in <c>Tables/*.sql</c> outside those lists, unexplained by either baseline,
/// fails loud here instead of shipping unverified.
/// </remarks>
public class DdlSchemaParityGuardTests
{
    /// <summary>
    /// Columns present in <c>Tables/*.sql</c> for a table that also exists in <c>97.sql</c>, added after
    /// the base schema by a migration that alters the existing table. See
    /// <c>Migrations/20251230193724_AddSearchParamExtensionColumns.cs</c>.
    /// </summary>
    private static readonly HashSet<(string Table, string Column)> KnownExtensionColumns =
    [
        ("UriSearchParam", "Fragment"),
        ("UriSearchParam", "Version"),
        ("TokenSearchParam", "IdentifierTypeCode"),
        ("TokenSearchParam", "IdentifierTypeSystemId"),
    ];

    /// <summary>
    /// Catalog-filtered tables that exist only in <c>Tables/*.sql</c> because a migration created them
    /// outright -- <c>97.sql</c> predates them and was never regenerated. See
    /// <c>Migrations/20251104055142_AddBackgroundJobs.cs</c>,
    /// <c>20251108000000_AddPackageResourceAndTerminologyIndexes.cs</c>,
    /// <c>20251118050351_AddTerminologyImportTracking.cs</c>, and
    /// <c>20251223154537_AddSourceEventsTable.cs</c>.
    /// </summary>
    private static readonly HashSet<string> TablesNotInBaseSchema =
    [
        "BackgroundJobs",
        "PackageResource",
        "SourceEvents",
        "TermCodeSystem",
        "TermConcept",
        "TermConceptMap",
        "TermConceptMapElement",
        "TermValueSet",
        "TermValueSetExpansion",
    ];

    [Fact]
    public void GivenTheDecomposedTableDdl_WhenComparedToTheDatabaseProvisioningScript_ThenSharedTablesAgreeColumnForColumn()
    {
        // Arrange
        var repoRoot = RepoRoot.Find();
        var baseSchema = IndexByTable(ParseFile(
            repoRoot, "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql"));
        var catalogTablesDir = Path.Combine(
            repoRoot, "src", "DataLayer", "Ignixa.DataLayer.SqlServer.Database", "Tables");
        var decomposedDdl = string.Join(
            "\n",
            Directory.GetFiles(catalogTablesDir, "*.sql").Select(File.ReadAllText));
        var decomposed = IndexByTable(DdlTableParser.ParseTables(decomposedDdl, SqlCatalogTableFilter.IsCatalogTable));

        decomposed.ShouldNotBeEmpty();
        baseSchema.ShouldNotBeEmpty();

        // Act & Assert -- every catalog table not explicitly known to be migration-only must exist in the
        // base schema, and for those that exist in both, every column not on the known-extensions
        // allowlist must match exactly (type, width, nullability). A mismatch here means the compiler is
        // deriving widths from a file that no longer describes the deployed database.
        var mismatches = new List<string>();

        foreach (var (tableName, decomposedTable) in decomposed)
        {
            if (!baseSchema.TryGetValue(tableName, out var baseTable))
            {
                if (!TablesNotInBaseSchema.Contains(tableName))
                {
                    mismatches.Add(
                        $"{tableName}: present in Tables/*.sql but not in 97.sql, and not on the known " +
                        $"migration-created-table allowlist. Add it to {nameof(TablesNotInBaseSchema)} " +
                        "if a migration genuinely created it, or fix the drift otherwise.");
                }

                continue;
            }

            var baseColumns = baseTable.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            var decomposedColumns = decomposedTable.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

            foreach (var (columnName, baseColumn) in baseColumns)
            {
                if (!decomposedColumns.TryGetValue(columnName, out var decomposedColumn))
                {
                    mismatches.Add($"{tableName}.{columnName}: in 97.sql but missing from Tables/*.sql.");
                    continue;
                }

                if (!ColumnsMatch(baseColumn, decomposedColumn))
                {
                    mismatches.Add(
                        $"{tableName}.{columnName}: 97.sql has {Describe(baseColumn)}, " +
                        $"Tables/*.sql has {Describe(decomposedColumn)}.");
                }
            }

            foreach (var columnName in decomposedColumns.Keys.Except(baseColumns.Keys))
            {
                if (!KnownExtensionColumns.Contains((tableName, columnName)))
                {
                    mismatches.Add(
                        $"{tableName}.{columnName}: in Tables/*.sql but not in 97.sql, and not on the " +
                        $"known-extension-columns allowlist. Add it to {nameof(KnownExtensionColumns)} " +
                        "if a migration genuinely added it, or fix the drift otherwise.");
                }
            }
        }

        mismatches.ShouldBeEmpty(string.Join("\n", mismatches));
    }

    private static IReadOnlyList<DdlTable> ParseFile(string repoRoot, string relativePath) =>
        DdlTableParser.ParseTables(
            File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))),
            SqlCatalogTableFilter.IsCatalogTable);

    private static Dictionary<string, DdlTable> IndexByTable(IReadOnlyList<DdlTable> tables) =>
        tables.ToDictionary(t => t.TableName, StringComparer.Ordinal);

    private static bool ColumnsMatch(DdlColumn expected, DdlColumn actual) =>
        expected.SqlType == actual.SqlType
            && expected.MaxLength == actual.MaxLength
            && expected.IsNullable == actual.IsNullable;

    private static string Describe(DdlColumn column) =>
        $"{column.SqlType}({column.MaxLength?.ToString() ?? "n/a"}), nullable={column.IsNullable}";
}
