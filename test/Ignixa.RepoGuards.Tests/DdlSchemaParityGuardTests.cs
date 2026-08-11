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
/// fails loud here instead of shipping unverified. <see cref="KnownExtensionColumns"/>' values are the
/// column shape the migration actually declares, not just a name to suppress -- so a re-widened extension
/// column is still caught, not silently allowlisted away.
/// <para>
/// The comparison itself (<see cref="FindMismatches"/>) is exercised directly against hand-written
/// fixtures in <see cref="GivenAColumnTypeMismatchNotOnTheAllowlist_WhenCompared_ThenItIsReported"/> and
/// its siblings, so a bug in the comparison logic (an inverted condition, an allowlist check that never
/// fires) has a failing test of its own instead of relying solely on the real-file test, which only ever
/// exercises the current, already-agreeing repo state.
/// </para>
/// </remarks>
public class DdlSchemaParityGuardTests
{
    /// <summary>
    /// The column shape <c>Tables/*.sql</c> declares for a column that also exists on a table in
    /// <c>97.sql</c>, added after the base schema by a migration that alters the existing table. Values
    /// come from the migration's own <c>AddColumn</c> arguments, not just copied from
    /// <c>Tables/*.sql</c> -- so a column re-widened in the decomposed DDL without a matching migration
    /// still fails here. See <c>Migrations/20251230193724_AddSearchParamExtensionColumns.cs</c>.
    /// </summary>
    private static readonly Dictionary<(string Table, string Column), DdlColumn> KnownExtensionColumns = new()
    {
        [("UriSearchParam", "Fragment")] = Column("Fragment", "nvarchar", 128, isNullable: true),
        [("UriSearchParam", "Version")] = Column("Version", "nvarchar", 64, isNullable: true),
        [("TokenSearchParam", "IdentifierTypeCode")] = Column("IdentifierTypeCode", "nvarchar", 256, isNullable: true),
        [("TokenSearchParam", "IdentifierTypeSystemId")] = Column("IdentifierTypeSystemId", "int", null, isNullable: true),
    };

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

        // TablesNotInBaseSchema only ever suppresses -- FindMismatches never asserts these parsed at all, so
        // a table dropped or renamed to something DdlTableParser can't parse (e.g. a bracketed identifier)
        // would silently vanish from `decomposed` and the guard would still pass. Assert presence directly.
        TablesNotInBaseSchema.ShouldBeSubsetOf(decomposed.Keys);

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, KnownExtensionColumns, TablesNotInBaseSchema);

        // Assert -- a mismatch here means the compiler is deriving widths from a file that no longer
        // describes the deployed database.
        mismatches.ShouldBeEmpty(string.Join("\n", mismatches));
    }

    [Fact]
    public void GivenAColumnTypeMismatchNotOnTheAllowlist_WhenCompared_ThenItIsReported()
    {
        // Arrange -- Code is varchar(256) in the base schema but nvarchar(256) in the decomposed DDL.
        var baseSchema = Schema(Table("TokenSearchParam", Column("Code", "varchar", 256, isNullable: false)));
        var decomposed = Schema(Table("TokenSearchParam", Column("Code", "nvarchar", 256, isNullable: false)));

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, [], []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("TokenSearchParam.Code");
    }

    [Fact]
    public void GivenACollationMismatchNotOnTheAllowlist_WhenCompared_ThenItIsReported()
    {
        // Arrange -- same type and width, different collation. This is exactly the kind of drift the
        // guard exists to catch: token/string comparisons are case-sensitive or not depending on
        // collation, so a silently-changed collation changes search results, not just storage.
        var baseSchema = Schema(Table(
            "TokenSearchParam",
            Column("Code", "varchar", 256, isNullable: false, collation: "Latin1_General_100_CS_AS")));
        var decomposed = Schema(Table(
            "TokenSearchParam",
            Column("Code", "varchar", 256, isNullable: false, collation: "Latin1_General_100_CI_AI")));

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, [], []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("TokenSearchParam.Code");
    }

    [Fact]
    public void GivenAColumnMissingFromTheDecomposedDdl_WhenCompared_ThenItIsReported()
    {
        // Arrange -- the base schema has SystemId; the decomposed DDL dropped it.
        var baseSchema = Schema(Table(
            "TokenSearchParam",
            Column("Code", "varchar", 256, isNullable: false),
            Column("SystemId", "int", null, isNullable: true)));
        var decomposed = Schema(Table("TokenSearchParam", Column("Code", "varchar", 256, isNullable: false)));

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, [], []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("TokenSearchParam.SystemId");
        mismatches[0].ShouldContain("missing from Tables");
    }

    [Fact]
    public void GivenAnUnlistedExtraColumn_WhenCompared_ThenItIsReported()
    {
        // Arrange -- the decomposed DDL has a column the base schema doesn't, and it's not on either
        // allowlist.
        var baseSchema = Schema(Table("TokenSearchParam", Column("Code", "varchar", 256, isNullable: false)));
        var decomposed = Schema(Table(
            "TokenSearchParam",
            Column("Code", "varchar", 256, isNullable: false),
            Column("MysteryColumn", "int", null, isNullable: true)));

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, [], []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("TokenSearchParam.MysteryColumn");
        mismatches[0].ShouldContain(nameof(KnownExtensionColumns));
    }

    [Fact]
    public void GivenAKnownExtensionColumnReWidenedInTheDecomposedDdl_WhenCompared_ThenItIsReported()
    {
        // Arrange -- Fragment is on the allowlist by name, but its shape here no longer matches what the
        // migration actually declares. Being on the allowlist must not suppress this.
        var baseSchema = Schema(Table("UriSearchParam", Column("Uri", "varchar", 256, isNullable: false)));
        var decomposed = Schema(Table(
            "UriSearchParam",
            Column("Uri", "varchar", 256, isNullable: false),
            Column("Fragment", "nvarchar", 512, isNullable: true)));

        // Act
        var mismatches = FindMismatches(
            baseSchema,
            decomposed,
            knownExtensionColumns: new Dictionary<(string, string), DdlColumn>
            {
                [("UriSearchParam", "Fragment")] = Column("Fragment", "nvarchar", 128, isNullable: true),
            },
            tablesNotInBaseSchema: []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("UriSearchParam.Fragment");
    }

    [Fact]
    public void GivenAnUnlistedTableOnlyInTheDecomposedDdl_WhenCompared_ThenItIsReported()
    {
        // Arrange -- a whole table exists only in the decomposed DDL and isn't on the allowlist.
        var baseSchema = Schema();
        var decomposed = Schema(Table("MysteryTable", Column("Id", "int", null, isNullable: false)));

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, [], []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("MysteryTable");
        mismatches[0].ShouldContain(nameof(TablesNotInBaseSchema));
    }

    [Fact]
    public void GivenATableOnlyInTheBaseSchema_WhenCompared_ThenItIsReported()
    {
        // Arrange -- a catalog table exists in 97.sql but was dropped from Tables/*.sql. There's no
        // allowlist for this direction: Tables/*.sql is a superset of 97.sql (base schema plus known
        // migrations), so a table 97.sql has and Tables/*.sql doesn't is always a real gap -- exactly
        // what SqlCatalog.Table("...") would fail to look up at runtime.
        var baseSchema = Schema(Table("ResourceType", Column("Name", "nvarchar", 50, isNullable: false)));
        var decomposed = Schema();

        // Act
        var mismatches = FindMismatches(baseSchema, decomposed, [], []);

        // Assert
        mismatches.ShouldHaveSingleItem();
        mismatches[0].ShouldContain("ResourceType");
    }

    [Fact]
    public void GivenDifferencesCoveredByBothAllowlists_WhenCompared_ThenNothingIsReported()
    {
        // Arrange -- mirrors the real Tables/*.sql vs 97.sql relationship: an extension column on a
        // shared table, plus a whole table that only exists via migration.
        var baseSchema = Schema(Table("UriSearchParam", Column("Uri", "varchar", 256, isNullable: false)));
        var decomposed = Schema(
            Table(
                "UriSearchParam",
                Column("Uri", "varchar", 256, isNullable: false),
                Column("Fragment", "nvarchar", 128, isNullable: true)),
            Table("SourceEvents", Column("Id", "int", null, isNullable: false)));

        // Act
        var mismatches = FindMismatches(
            baseSchema,
            decomposed,
            knownExtensionColumns: new Dictionary<(string, string), DdlColumn>
            {
                [("UriSearchParam", "Fragment")] = Column("Fragment", "nvarchar", 128, isNullable: true),
            },
            tablesNotInBaseSchema: ["SourceEvents"]);

        // Assert
        mismatches.ShouldBeEmpty(string.Join("\n", mismatches));
    }

    /// <summary>
    /// Compares <paramref name="baseSchema"/> (the DB provisioning script) against
    /// <paramref name="decomposed"/> (the source generator's DDL) table by table, treating both allowlists
    /// as closed sets -- anything outside them is reported. Pulled out of the real-file test so the
    /// comparison logic itself has fixture-driven coverage independent of the current repo state.
    /// </summary>
    private static List<string> FindMismatches(
        IReadOnlyDictionary<string, DdlTable> baseSchema,
        IReadOnlyDictionary<string, DdlTable> decomposed,
        Dictionary<(string Table, string Column), DdlColumn> knownExtensionColumns,
        HashSet<string> tablesNotInBaseSchema)
    {
        var mismatches = new List<string>();

        foreach (var tableName in baseSchema.Keys.Except(decomposed.Keys))
        {
            mismatches.Add($"{tableName}: present in 97.sql but missing entirely from Tables/*.sql.");
        }

        foreach (var (tableName, decomposedTable) in decomposed)
        {
            if (!baseSchema.TryGetValue(tableName, out var baseTable))
            {
                if (!tablesNotInBaseSchema.Contains(tableName))
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
                var decomposedColumn = decomposedColumns[columnName];

                if (!knownExtensionColumns.TryGetValue((tableName, columnName), out var expectedColumn))
                {
                    mismatches.Add(
                        $"{tableName}.{columnName}: in Tables/*.sql but not in 97.sql, and not on the " +
                        $"known-extension-columns allowlist. Add it to {nameof(KnownExtensionColumns)} " +
                        "if a migration genuinely added it, or fix the drift otherwise.");
                    continue;
                }

                if (!ColumnsMatch(expectedColumn, decomposedColumn))
                {
                    mismatches.Add(
                        $"{tableName}.{columnName}: known extension column expected to be " +
                        $"{Describe(expectedColumn)} (per the migration that added it), Tables/*.sql has " +
                        $"{Describe(decomposedColumn)}.");
                }
            }
        }

        return mismatches;
    }

    private static IReadOnlyList<DdlTable> ParseFile(string repoRoot, string relativePath) =>
        DdlTableParser.ParseTables(
            File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))),
            SqlCatalogTableFilter.IsCatalogTable);

    private static Dictionary<string, DdlTable> IndexByTable(IReadOnlyList<DdlTable> tables) =>
        tables.ToDictionary(t => t.TableName, StringComparer.Ordinal);

    private static Dictionary<string, DdlTable> Schema(params DdlTable[] tables) => IndexByTable(tables);

    private static DdlTable Table(string name, params DdlColumn[] columns) => new("dbo", name, columns);

    private static DdlColumn Column(string name, string sqlType, int? maxLength, bool isNullable, string? collation = null) =>
        new(name, sqlType, maxLength, collation, isNullable);

    private static bool ColumnsMatch(DdlColumn expected, DdlColumn actual) =>
        expected.SqlType == actual.SqlType
            && expected.MaxLength == actual.MaxLength
            && expected.IsNullable == actual.IsNullable
            && expected.Collation == actual.Collation;

    private static string Describe(DdlColumn column) =>
        $"{column.SqlType}({column.MaxLength?.ToString() ?? "n/a"}), nullable={column.IsNullable}, " +
        $"collation={column.Collation ?? "n/a"}";
}
