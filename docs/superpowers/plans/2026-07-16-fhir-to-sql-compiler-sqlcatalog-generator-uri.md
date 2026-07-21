# SqlCatalog Source Generator + UriLoweringRule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `Ignixa.Search.Sql`'s hand-transcribed `SqlCatalog` with a Roslyn source generator that reads the real DDL (`97.sql`) directly, covering all 15 `*SearchParam`/`ResourceType` tables (the 5 already hand-written plus the 10 remaining leaf/composite tables Phase 5's continuation will need) — then add `UriLoweringRule`, the next tier-1 leaf rule, as the first consumer proving the generated catalog data is correct.

**Architecture:** A new `Ignixa.Search.Sql.Generators` project (mirroring `Ignixa.FhirPath.Generators`'s proven shape: `netstandard2.0`, `IsRoslynComponent`, referenced as an `Analyzer`) reads `97.sql` via `AdditionalFiles`, parses every `CREATE TABLE dbo.*SearchParam`/`dbo.ResourceType` block with a plain, Roslyn-independent, directly-unit-testable parser, and emits a generated partial-class implementation of `SqlCatalog.BuildFromDdl()`. `SqlCatalog.cs` itself becomes a thin, hand-written partial class (constructor, `Table()`, `Default`) with zero table data of its own. `UriLoweringRule` then follows the exact shape `StringLoweringRule`/`TokenLoweringRule`/`ReferenceLoweringRule` already established.

**Tech Stack:** Generator project: `netstandard2.0` (required for Roslyn generators), `Microsoft.CodeAnalysis.CSharp`/`Microsoft.CodeAnalysis.Analyzers`. Everything else: `net9.0;net10.0`, matching `Ignixa.Search.Sql`'s existing convention.

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- `Ignixa.Search.Sql.csproj` must keep **no** `Microsoft.EntityFrameworkCore*`/`Microsoft.AspNetCore.*` reference. Adding an `AdditionalFiles` item pointing at a file that physically lives inside `Ignixa.DataLayer.SqlEntityFramework`'s directory does **not** violate this — `AdditionalFiles` is a build-time text input, not an assembly/package reference; it brings in zero EF/AspNetCore code. Verify the grep for EF/AspNetCore references still returns nothing after every task that touches the `.csproj`.
- **No repo precedent exists for a Roslyn generator reading a non-C# `AdditionalFiles` input** (confirmed by research: this repo's only `AdditionalFiles` usage is `Ignixa.Analyzers` consuming its own release-tracking markdown, a different, diagnostic-analyzer pattern). This is genuinely new territory for this codebase. Task 1 proves the wiring works with a trivial hardcoded output *before* task 2/3 add real DDL-parsing logic — do not debug generator wiring and parser correctness at the same time. If the wiring genuinely doesn't work after reasonable troubleshooting, STOP and report BLOCKED rather than spending excessive time on unfamiliar Roslyn generator internals.
- The generator targets exactly the tables whose name ends with `SearchParam`, plus `ResourceType` (the same filter as: `name.EndsWith("SearchParam") || name == "ResourceType"`) — **15 tables total**: `StringSearchParam`, `TokenSearchParam`, `ReferenceSearchParam`, `DateTimeSearchParam`, `NumberSearchParam`, `QuantitySearchParam`, `UriSearchParam`, `TokenTokenCompositeSearchParam`, `TokenQuantityCompositeSearchParam`, `TokenStringCompositeSearchParam`, `TokenDateTimeCompositeSearchParam`, `TokenNumberNumberCompositeSearchParam`, `ReferenceTokenCompositeSearchParam`, `ResourceType`, `SearchParam` (the lookup table `SearchParam` matches the `*SearchParam` filter trivially, since its own name ends with "SearchParam" — no special-casing needed). Generating catalog data for tables no lowering rule consumes yet (the composites, Date/Number/Quantity) is a deliberate, justified exception to this project's usual "only build what's consumed" discipline: the generator parses the whole file regardless, so emitting all 15 tables costs nothing extra over emitting 6, and it's inert, verified-correct *data*, not unused *machinery* (unlike, say, building `ColumnRole` composite-addressing now, which would still be premature).
- The generator's parser (`DdlTableParser`) must be a plain C# class with **no** Roslyn/`Microsoft.CodeAnalysis` dependency — directly unit-testable with xunit against literal DDL string fixtures, independent of the generator-driver machinery. The `IIncrementalGenerator` itself should be a thin wrapper: read the `AdditionalText`, call `DdlTableParser`, emit source. This keeps the hard-to-test part (Roslyn wiring) as small as possible.
- Every existing `SqlCatalog`-consuming test (`SqlCatalogTests.cs`'s 5 current facts, and anything in `StringLoweringRuleTests.cs`/`TokenLoweringRuleTests.cs`/`ReferenceLoweringRuleTests.cs`/`EndToEndCompilationTests.cs` that reads `SqlCatalog.Default`) must still pass **unchanged** once the generator replaces the hand-written data — this is the strongest available regression check that the generator produces byte-identical facts to what was already hand-verified against the real DDL in Phase 3/4/5.
- `UriLoweringRule` handles only the no-modifier (plain equality) case. FHIR's `uri` search type supports `:above`/`:below` modifiers (hierarchical URI matching) — these are explicitly out of scope; the rule throws `NotSupportedException` for any modifier, matching the established "throw rather than silently mishandle" convention from `TokenLoweringRule`/`ReferenceLoweringRule`. `UriSearchValue.Version`/`.Fragment` (canonical-URL extension fields) are **not** in `97.sql`'s base `UriSearchParam` table — confirmed they're populated via a separate post-merge extension-column path outside this schema — so `SqlCatalog`'s generated `UriSearchParam` entry and this rule both cover only the base 4 columns (`ResourceTypeId`/`ResourceSurrogateId`/`SearchParamId`/`Uri`).
- Follow repo convention: file-scoped namespaces (usings above the namespace line), AAA test structure, `GivenContext_WhenAction_ThenResult` naming, no `#region`, one cohesive concept per file.

---

### Task 1: Scaffold `Ignixa.Search.Sql.Generators`, prove the wiring with a trivial output

**Files:**
- Create: `src/Core/Ignixa.Search.Sql.Generators/Ignixa.Search.Sql.Generators.csproj`
- Create: `src/Core/Ignixa.Search.Sql.Generators/TrivialProbeGenerator.cs` (temporary — deleted in task 3 once the real generator replaces it)
- Modify: `src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj` (add the `Analyzer` reference and an `AdditionalFiles` item)
- Modify: `All.sln`

**Interfaces:**
- Consumes: nothing.
- Produces: proof that `IIncrementalGenerator` output actually reaches `Ignixa.Search.Sql`'s compilation — task 3 replaces the trivial probe with the real DDL-driven generator.

- [ ] **Step 1: Read the real reference shape to mirror**

```bash
cat src/Core/Ignixa.FhirPath.Generators/Ignixa.FhirPath.Generators.csproj
grep -n "Ignixa.FhirPath.Generators" -A3 src/Core/Ignixa.FhirPath/Ignixa.FhirPath.csproj
```

Confirm the exact `TargetFramework`/`IsRoslynComponent`/`EnforceExtendedAnalyzerRules`/package-reference shape, and the exact `<ProjectReference>` + `<OutputItemType>Analyzer</OutputItemType>` + `<ReferenceOutputAssembly>false</ReferenceOutputAssembly>` consumer-side shape — copy both verbatim, adjusting only the project name/description.

- [ ] **Step 2: Create the generator project**

```xml
<!-- src/Core/Ignixa.Search.Sql.Generators/Ignixa.Search.Sql.Generators.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <PlatformTarget>AnyCPU</PlatformTarget>
  </PropertyGroup>

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <Description>Source generator for Ignixa.Search.Sql's SqlCatalog -- reads the real search-index DDL (97.sql) and generates the table/column facts SqlCatalog.Default exposes, instead of hand-transcribing them.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write a trivial probe generator to prove the wiring end-to-end first**

```csharp
// src/Core/Ignixa.Search.Sql.Generators/TrivialProbeGenerator.cs
using Microsoft.CodeAnalysis;

namespace Ignixa.Search.Sql.Generators;

[Generator]
public sealed class TrivialProbeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("GeneratorProbe.g.cs",
                "namespace Ignixa.Search.Sql.Generators.Probe; internal static class GeneratorProbe { internal const bool Ran = true; }"));
    }
}
```

- [ ] **Step 4: Wire the Analyzer reference and AdditionalFiles item**

```xml
<!-- src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj -- add inside an ItemGroup -->
<ItemGroup>
  <ProjectReference Include="..\Ignixa.Search.Sql.Generators\Ignixa.Search.Sql.Generators.csproj">
    <OutputItemType>Analyzer</OutputItemType>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
</ItemGroup>

<ItemGroup>
  <AdditionalFiles Include="..\..\DataLayer\Ignixa.DataLayer.SqlEntityFramework\Resources\97.sql" />
</ItemGroup>
```

Confirm the relative path from `src/Core/Ignixa.Search.Sql/` to `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql` is exactly `..\..\DataLayer\Ignixa.DataLayer.SqlEntityFramework\Resources\97.sql` — verify against the real directory structure (`ls src/Core/Ignixa.Search.Sql`, `ls src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources`) before trusting this path.

- [ ] **Step 5: Register in `All.sln`, build, and prove the probe output actually compiles into `Ignixa.Search.Sql`**

```bash
dotnet sln All.sln add src/Core/Ignixa.Search.Sql.Generators/Ignixa.Search.Sql.Generators.csproj
dotnet build All.sln --nologo
```

**Expected:** 0 warnings, 0 errors. To actually prove the generated source landed in the compilation (not just that the build succeeded, which could happen even if the generator silently did nothing useful), add a temporary throwaway line inside `Ignixa.Search.Sql`'s `SqlCatalog.cs` like `_ = Generators.Probe.GeneratorProbe.Ran;` (fully qualified, no `using` needed), confirm it compiles (proving the type from the generated file is visible), then remove that throwaway line before committing.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): scaffold Ignixa.Search.Sql.Generators, prove AdditionalFiles wiring

Trivial probe generator only -- proves IIncrementalGenerator output
reaches Ignixa.Search.Sql's compilation via the Analyzer reference and
that the AdditionalFiles item resolves to the real 97.sql, before task 2
adds real DDL-parsing logic. No repo precedent existed for a generator
reading a non-C# AdditionalFiles input before this."
```

---

### Task 2: `DdlTableParser` — plain, Roslyn-independent DDL parser

**Files:**
- Create: `src/Core/Ignixa.Search.Sql.Generators/DdlColumn.cs`
- Create: `src/Core/Ignixa.Search.Sql.Generators/DdlTable.cs`
- Create: `src/Core/Ignixa.Search.Sql.Generators/DdlTableParser.cs`
- Create: `test/Ignixa.Search.Sql.Generators.Tests/Ignixa.Search.Sql.Generators.Tests.csproj`
- Test: `test/Ignixa.Search.Sql.Generators.Tests/DdlTableParserTests.cs`

**Interfaces:**
- Consumes: nothing (pure string-in, data-out).
- Produces: `DdlTableParser.ParseTables(string ddlText, Func<string, bool> tableNameFilter): IReadOnlyList<DdlTable>`, consumed by task 3's generator.

- [ ] **Step 1: Write the data types**

```csharp
// src/Core/Ignixa.Search.Sql.Generators/DdlColumn.cs
namespace Ignixa.Search.Sql.Generators;

public sealed class DdlColumn
{
    public DdlColumn(string name, string sqlType, int? maxLength, string? collation, bool isNullable)
    {
        Name = name;
        SqlType = sqlType;
        MaxLength = maxLength;
        Collation = collation;
        IsNullable = isNullable;
    }

    public string Name { get; }
    public string SqlType { get; }
    public int? MaxLength { get; }
    public string? Collation { get; }
    public bool IsNullable { get; }
}
```

```csharp
// src/Core/Ignixa.Search.Sql.Generators/DdlTable.cs
using System.Collections.Generic;

namespace Ignixa.Search.Sql.Generators;

public sealed class DdlTable
{
    public DdlTable(string schemaName, string tableName, IReadOnlyList<DdlColumn> columns)
    {
        SchemaName = schemaName;
        TableName = tableName;
        Columns = columns;
    }

    public string SchemaName { get; }
    public string TableName { get; }
    public IReadOnlyList<DdlColumn> Columns { get; }
}
```

- [ ] **Step 2: Write the failing parser tests against literal DDL fixtures**

```csharp
// test/Ignixa.Search.Sql.Generators.Tests/DdlTableParserTests.cs
using System.Linq;
using Ignixa.Search.Sql.Generators;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Generators.Tests;

public class DdlTableParserTests
{
    [Fact]
    public void GivenASimpleTable_WhenParsed_ThenReturnsItsColumns()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.SimpleSearchParam (
                ResourceTypeId SMALLINT NOT NULL,
                Text NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
                TextOverflow NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam"));

        // Assert
        tables.Count.ShouldBe(1);
        var table = tables[0];
        table.TableName.ShouldBe("SimpleSearchParam");
        table.Columns.Count.ShouldBe(3);
        table.Columns[0].Name.ShouldBe("ResourceTypeId");
        table.Columns[0].SqlType.ShouldBe("smallint");
        table.Columns[0].MaxLength.ShouldBeNull();
        table.Columns[0].IsNullable.ShouldBeFalse();
        table.Columns[1].Name.ShouldBe("Text");
        table.Columns[1].SqlType.ShouldBe("nvarchar");
        table.Columns[1].MaxLength.ShouldBe(256);
        table.Columns[1].Collation.ShouldBe("Latin1_General_100_CI_AI_SC");
        table.Columns[2].Name.ShouldBe("TextOverflow");
        table.Columns[2].MaxLength.ShouldBeNull(); // MAX -- not a numeric width
        table.Columns[2].IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenAColumnWithAConstraintDefault_WhenParsed_ThenTheConstraintIsIgnoredNotTreatedAsAColumn()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.FlagSearchParam (
                IsMin BIT CONSTRAINT flag_IsMin_Constraint DEFAULT 0 NOT NULL,
                IsMax BIT CONSTRAINT flag_IsMax_Constraint DEFAULT 0 NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam"));

        // Assert
        tables[0].Columns.Count.ShouldBe(2);
        tables[0].Columns[0].Name.ShouldBe("IsMin");
        tables[0].Columns[0].SqlType.ShouldBe("bit");
    }

    [Fact]
    public void GivenAMultiArgDecimalType_WhenParsed_ThenTheCommaInsideParensIsNotTreatedAsAColumnSeparator()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.NumberSearchParam (
                SingleValue DECIMAL (36, 18) NULL,
                LowValue DECIMAL (36, 18) NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam"));

        // Assert
        tables[0].Columns.Count.ShouldBe(2);
        tables[0].Columns[0].Name.ShouldBe("SingleValue");
        tables[0].Columns[0].SqlType.ShouldBe("decimal");
        tables[0].Columns[0].MaxLength.ShouldBe(36); // first numeric arg -- precision, not a string-width concept here
        tables[0].Columns[0].IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenATableNameThatDoesNotMatchTheFilter_WhenParsed_ThenItIsExcluded()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.EventLog (
                EventId BIGINT IDENTITY (1, 1) NOT NULL
            );
            CREATE TABLE dbo.StringSearchParam (
                ResourceTypeId SMALLINT NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam"));

        // Assert
        tables.Count.ShouldBe(1);
        tables[0].TableName.ShouldBe("StringSearchParam");
    }

    [Fact]
    public void GivenAnIdentityColumn_WhenParsed_ThenIdentityIsIgnoredLikeAnyOtherModifier()
    {
        // Arrange -- ResourceType/SearchParam lookup tables have IDENTITY primary key columns;
        // ColumnDescriptor has no IDENTITY concept, so the parser must tolerate and ignore it.
        var ddl = """
            CREATE TABLE dbo.ResourceType (
                ResourceTypeId SMALLINT IDENTITY (1, 1) NOT NULL,
                Name NVARCHAR (50) COLLATE Latin1_General_100_CS_AS NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name == "ResourceType");

        // Assert
        tables[0].Columns[0].Name.ShouldBe("ResourceTypeId");
        tables[0].Columns[0].SqlType.ShouldBe("smallint");
    }
}
```

- [ ] **Step 3: Create the test project**

```bash
dotnet new xunit -n Ignixa.Search.Sql.Generators.Tests -o test/Ignixa.Search.Sql.Generators.Tests
```

Add a `ProjectReference` to `Ignixa.Search.Sql.Generators.csproj`, remove the template's default `UnitTest1.cs`, match package versions to `Directory.Packages.props` (no explicit `Version` attributes), matching every other test project's pattern. Register in `All.sln`.

- [ ] **Step 4: Run to confirm failure**

```bash
dotnet build All.sln --nologo
```

Expected: build error, `DdlTableParser` doesn't exist yet.

- [ ] **Step 5: Implement `DdlTableParser`**

```csharp
// src/Core/Ignixa.Search.Sql.Generators/DdlTableParser.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ignixa.Search.Sql.Generators;

/// <summary>
/// Parses CREATE TABLE dbo.X (...) blocks out of a raw T-SQL DDL script -- no Roslyn dependency,
/// directly unit-testable. Depth-aware: DECIMAL(36,18)'s internal comma must not be mistaken for a
/// column separator, and the table body's closing paren must not be mistaken for the type-args'
/// closing paren.
/// </summary>
public static class DdlTableParser
{
    private static readonly Regex TableStart = new(@"CREATE TABLE dbo\.(\w+)\s*\(", RegexOptions.IgnoreCase);

    private static readonly Regex ColumnLine = new(
        @"^(?<name>\w+)\s+(?<type>\w+)\s*(\((?<args>[^)]*)\))?" +
        @"(\s+COLLATE\s+(?<collation>\S+))?" +
        @"(\s+CONSTRAINT\s+\S+\s+DEFAULT\s+\S+)?" +
        @"(\s+IDENTITY\s*\([^)]*\))?" +
        @"\s+(?<nullability>NOT\s+NULL|NULL)\s*$",
        RegexOptions.IgnoreCase);

    public static IReadOnlyList<DdlTable> ParseTables(string ddlText, Func<string, bool> tableNameFilter)
    {
        var tables = new List<DdlTable>();
        var searchStart = 0;

        while (true)
        {
            var startMatch = TableStart.Match(ddlText, searchStart);
            if (!startMatch.Success)
            {
                break;
            }

            var tableName = startMatch.Groups[1].Value;
            var openParenIndex = startMatch.Index + startMatch.Length - 1;
            var closeParenIndex = FindMatchingCloseParen(ddlText, openParenIndex);
            var body = ddlText.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1);

            if (tableNameFilter(tableName))
            {
                tables.Add(new DdlTable("dbo", tableName, ParseColumns(body)));
            }

            searchStart = closeParenIndex + 1;
        }

        return tables;
    }

    private static int FindMatchingCloseParen(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        throw new FormatException("Unbalanced parentheses in DDL table body.");
    }

    private static IReadOnlyList<DdlColumn> ParseColumns(string body)
    {
        var columns = new List<DdlColumn>();
        foreach (var rawLine in SplitTopLevel(body, ','))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Table-level constraints (PRIMARY KEY (...), CONSTRAINT ... CHECK (...)) are not column
            // definitions -- skip lines that open with these keywords rather than a column name.
            if (line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            columns.Add(ParseColumn(line));
        }

        return columns;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
            }
            else if (text[i] == separator && depth == 0)
            {
                yield return text.Substring(start, i - start);
                start = i + 1;
            }
        }

        yield return text.Substring(start);
    }

    private static DdlColumn ParseColumn(string line)
    {
        var match = ColumnLine.Match(line);
        if (!match.Success)
        {
            throw new FormatException($"Could not parse DDL column line: '{line}'");
        }

        var name = match.Groups["name"].Value;
        var sqlType = match.Groups["type"].Value.ToLowerInvariant();

        int? maxLength = null;
        if (match.Groups["args"].Success)
        {
            var firstArg = match.Groups["args"].Value.Split(',')[0].Trim();
            if (int.TryParse(firstArg, out var parsed))
            {
                maxLength = parsed;
            }
            // else: MAX, or a non-numeric first arg -- MaxLength stays null, matching the existing
            // hand-written convention (e.g. TextOverflow's NVARCHAR(MAX) already models as MaxLength: null).
        }

        var collation = match.Groups["collation"].Success ? match.Groups["collation"].Value : null;
        var isNullable = !match.Groups["nullability"].Value.Replace(" ", string.Empty)
            .Equals("NOTNULL", StringComparison.OrdinalIgnoreCase);

        return new DdlColumn(name, sqlType, maxLength, collation, isNullable);
    }
}
```

- [ ] **Step 6: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~DdlTableParserTests" --nologo
```

Expected: 0 warnings, 0 errors, all five tests pass. If any test's expected value disagrees with what your implementation actually produces, treat it as normal TDD — get the parser's real behavior right per the DDL shape being tested, then make the assertion match reality.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql-generators): add DdlTableParser, a plain Roslyn-independent DDL parser

Depth-aware (DECIMAL(36,18)'s internal comma, the table body's own
closing paren) and directly unit-testable without any generator-driver
machinery. Skips table-level CONSTRAINT/PRIMARY KEY/UNIQUE lines and
tolerates IDENTITY -- neither has a ColumnDescriptor concept to map to."
```

---

### Task 3: `SqlCatalogGenerator` — wire the real DDL into a generated `SqlCatalog` partial

**Files:**
- Create: `src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs`
- Delete: `src/Core/Ignixa.Search.Sql.Generators/TrivialProbeGenerator.cs` (task 1's proof-of-wiring probe, no longer needed)
- Modify: `src/Core/Ignixa.Search.Sql/Catalog/SqlCatalog.cs` (convert to a thin partial class, delete all hand-written table data)

**Interfaces:**
- Consumes: `DdlTableParser` (task 2), `TableDescriptor`/`ColumnDescriptor` (Phase 3, unchanged).
- Produces: a generated `SqlCatalog.g.cs` supplying `SqlCatalog.BuildFromDdl()`'s implementation for all 15 target tables, consumed by every existing `SqlCatalog.Default` call site unchanged.

- [ ] **Step 1: Convert `SqlCatalog.cs` to a partial class with a partial method**

```csharp
// src/Core/Ignixa.Search.Sql/Catalog/SqlCatalog.cs -- replace entirely
namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes the tables and columns this compiler emits SQL against. Table/column facts (SqlCatalog.g.cs)
/// are generated from the real DDL (src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql)
/// by Ignixa.Search.Sql.Generators -- this file owns only lookup behavior, not data. Deliberately does not
/// describe storage convention (e.g. which column an overflowing string lands in) -- that is Lower's job,
/// encoded as a rule, not a catalog fact.
/// </summary>
public sealed partial class SqlCatalog
{
    private readonly IReadOnlyDictionary<string, TableDescriptor> _tables;

    private SqlCatalog(IReadOnlyDictionary<string, TableDescriptor> tables)
    {
        _tables = tables;
    }

    public TableDescriptor Table(string name)
        => _tables.TryGetValue(name, out var table)
           ? table
           : throw new KeyNotFoundException($"SqlCatalog has no table named '{name}'.");

    public static SqlCatalog Default { get; } = new SqlCatalog(BuildFromDdl());

    private static partial IReadOnlyDictionary<string, TableDescriptor> BuildFromDdl();
}
```

A partial method with a non-`void` return type **requires** an implementing declaration to compile — if the generator ever fails to fire (misconfigured `AdditionalFiles`, generator crash, etc.), the build fails loudly with a missing-partial-implementation error rather than silently falling back to something wrong. This is a deliberate safety property, not an accident — do not add a fallback/default body here.

- [ ] **Step 2: Delete the trivial probe generator**

```bash
rm src/Core/Ignixa.Search.Sql.Generators/TrivialProbeGenerator.cs
```

- [ ] **Step 3: Write `SqlCatalogGenerator`**

```csharp
// src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs
using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ignixa.Search.Sql.Generators;

/// <summary>
/// Reads 97.sql (via AdditionalFiles) and generates SqlCatalog.BuildFromDdl()'s implementation --
/// table/column facts sourced directly from the real DDL, not hand-transcribed.
/// </summary>
[Generator]
public sealed class SqlCatalogGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var ddlFile = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith("97.sql", StringComparison.OrdinalIgnoreCase))
            .Collect();

        context.RegisterSourceOutput(ddlFile, static (spc, files) =>
        {
            if (files.Length == 0)
            {
                return;
            }

            var ddlText = files[0].GetText(spc.CancellationToken)?.ToString() ?? string.Empty;
            var tables = DdlTableParser.ParseTables(ddlText,
                name => name.EndsWith("SearchParam", StringComparison.Ordinal) || name == "ResourceType");

            spc.AddSource("SqlCatalog.g.cs", Emit(tables));
        });
    }

    private static string Emit(System.Collections.Generic.IReadOnlyList<DdlTable> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace Ignixa.Search.Sql.Catalog;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class SqlCatalog");
        sb.AppendLine("{");
        sb.AppendLine("    private static partial IReadOnlyDictionary<string, TableDescriptor> BuildFromDdl()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new Dictionary<string, TableDescriptor>");
        sb.AppendLine("        {");

        foreach (var table in tables)
        {
            sb.AppendLine($"            [\"{table.TableName}\"] = new TableDescriptor(\"{table.SchemaName}\", \"{table.TableName}\",");
            sb.AppendLine("            [");
            foreach (var column in table.Columns)
            {
                var maxLength = column.MaxLength.HasValue ? column.MaxLength.Value.ToString() : "null";
                var collation = column.Collation is null ? "null" : $"\"{column.Collation}\"";
                var isNullable = column.IsNullable ? "true" : "false";
                sb.AppendLine($"                new ColumnDescriptor(\"{column.Name}\", \"{column.SqlType}\", {maxLength}, {collation}, {isNullable}),");
            }
            sb.AppendLine("            ]),");
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **Step 4: Build and inspect the actual generated output**

```bash
dotnet build All.sln --nologo
```

Add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and `<CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>` temporarily to `Ignixa.Search.Sql.csproj` to write the real generated `SqlCatalog.g.cs` to disk, inspect it directly (`cat src/Core/Ignixa.Search.Sql/Generated/**/SqlCatalog.g.cs`), and manually diff its `StringSearchParam`/`TokenSearchParam`/`ReferenceSearchParam`/`ResourceType`/`SearchParam` entries against the exact facts Phase 3's plan (`2026-07-15-fhir-to-sql-compiler-phase3-catalog-resolve.md`) hand-verified from the same DDL. They must match exactly — this is the generator's core correctness proof. Remove the two temporary properties once you've confirmed this (don't leave generated files checked into source control).

- [ ] **Step 5: Run the existing (unchanged) `SqlCatalogTests.cs` against the generated data**

```bash
dotnet test All.sln --filter "FullyQualifiedName~SqlCatalogTests" --nologo
```

**Expected: all pre-existing tests pass unchanged.** This is the regression proof that the generator reproduces exactly what was already hand-verified. If any fails, the generator has a real bug — do not adjust the pre-existing tests to match wrong generator output; fix the parser/emitter.

- [ ] **Step 6: Run the full non-E2E suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
grep -i "EntityFrameworkCore\|AspNetCore" src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```

Expected: 0 warnings, 0 errors, all green (aside from the known pre-existing `sql-on-fhir-tests` submodule gap). Grep returns nothing.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): generate SqlCatalog from the real DDL instead of hand-transcribing it

SqlCatalog.cs is now a thin partial class (constructor, Table(), Default)
with zero hand-written table data -- SqlCatalogGenerator reads 97.sql
via AdditionalFiles and emits BuildFromDdl()'s implementation for all 15
*SearchParam/ResourceType tables. The 5 tables Phase 3 hand-transcribed
are reproduced byte-identical (verified against the generated output
directly); the 10 remaining leaf/composite tables Phase 5's continuation
needs are now available for free, as data -- no lowering rules exist to
consume most of them yet, which is fine, since generating them costs
nothing beyond generating the 5 that were already needed.

BuildFromDdl() is a partial method with a non-void return type, so a
missing generator implementation is a compile error, not a silent
runtime gap."
```

---

### Task 4: `SqlCatalogTests.cs` coverage for the 10 new tables

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogTests.cs`

**Interfaces:**
- Consumes: `SqlCatalog.Default` (now generator-backed, task 3).
- Produces: nothing new — proves the generator parsed all 15 target tables correctly, not just the 5 already covered.

- [ ] **Step 1: Read the real DDL for the 10 new tables directly (do not trust a prior summary)**

```bash
grep -n "CREATE TABLE dbo.DateTimeSearchParam\|CREATE TABLE dbo.NumberSearchParam\|CREATE TABLE dbo.QuantitySearchParam\|CREATE TABLE dbo.UriSearchParam\|CREATE TABLE dbo.TokenTokenCompositeSearchParam\|CREATE TABLE dbo.TokenQuantityCompositeSearchParam\|CREATE TABLE dbo.TokenStringCompositeSearchParam\|CREATE TABLE dbo.TokenDateTimeCompositeSearchParam\|CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam\|CREATE TABLE dbo.ReferenceTokenCompositeSearchParam" -A 15 src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql
```

- [ ] **Step 2: Add one fact-check test per new table, matching the established pattern**

```csharp
// test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogTests.cs -- add these [Fact]s to the existing class
[Fact]
public void GivenDateTimeSearchParam_WhenLookedUp_ThenStartDateTimeColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("DateTimeSearchParam");
    var column = table.Column("StartDateTime");

    column.SqlType.ShouldBe("datetime2");
    column.IsNullable.ShouldBeFalse();
}

[Fact]
public void GivenNumberSearchParam_WhenLookedUp_ThenLowValueColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("NumberSearchParam");
    var column = table.Column("LowValue");

    column.SqlType.ShouldBe("decimal");
    column.MaxLength.ShouldBe(36);
    column.IsNullable.ShouldBeFalse();
}

[Fact]
public void GivenQuantitySearchParam_WhenLookedUp_ThenSystemIdColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("QuantitySearchParam");
    var column = table.Column("SystemId");

    column.SqlType.ShouldBe("int");
    column.IsNullable.ShouldBeTrue();
}

[Fact]
public void GivenUriSearchParam_WhenLookedUp_ThenUriColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("UriSearchParam");
    var column = table.Column("Uri");

    column.SqlType.ShouldBe("varchar");
    column.MaxLength.ShouldBe(256);
    column.Collation.ShouldBe("Latin1_General_100_CS_AS");
    column.IsNullable.ShouldBeFalse();
}

[Fact]
public void GivenTokenTokenCompositeSearchParam_WhenLookedUp_ThenCode1ColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("TokenTokenCompositeSearchParam");
    var column = table.Column("Code1");

    column.SqlType.ShouldBe("varchar");
    column.MaxLength.ShouldBe(256);
    column.Collation.ShouldBe("Latin1_General_100_CS_AS");
}

[Fact]
public void GivenTokenQuantityCompositeSearchParam_WhenLookedUp_ThenLowValue2ColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("TokenQuantityCompositeSearchParam");
    var column = table.Column("LowValue2");

    column.SqlType.ShouldBe("decimal");
    column.IsNullable.ShouldBeTrue(); // NULL, unlike the base NumberSearchParam/QuantitySearchParam LowValue -- confirmed real divergence, not a transcription error
}

[Fact]
public void GivenTokenStringCompositeSearchParam_WhenLookedUp_ThenText2ColumnCollationMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("TokenStringCompositeSearchParam");
    var column = table.Column("Text2");

    // Latin1_General_CI_AI, NOT Latin1_General_100_CI_AI_SC -- a different literal collation string
    // than the base StringSearchParam.Text column. Confirmed against the real DDL, not a typo to "fix."
    column.Collation.ShouldBe("Latin1_General_CI_AI");
}

[Fact]
public void GivenTokenDateTimeCompositeSearchParam_WhenLookedUp_ThenStartDateTime2ColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("TokenDateTimeCompositeSearchParam");
    var column = table.Column("StartDateTime2");

    column.SqlType.ShouldBe("datetime2");
    column.IsNullable.ShouldBeFalse();
}

[Fact]
public void GivenTokenNumberNumberCompositeSearchParam_WhenLookedUp_ThenHasRangeColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("TokenNumberNumberCompositeSearchParam");
    var column = table.Column("HasRange");

    column.SqlType.ShouldBe("bit");
    column.IsNullable.ShouldBeFalse();
}

[Fact]
public void GivenReferenceTokenCompositeSearchParam_WhenLookedUp_ThenReferenceResourceId1ColumnMatchesRealDdl()
{
    var table = SqlCatalog.Default.Table("ReferenceTokenCompositeSearchParam");
    var column = table.Column("ReferenceResourceId1");

    column.SqlType.ShouldBe("varchar");
    column.MaxLength.ShouldBe(64);
    column.IsNullable.ShouldBeFalse();
}
```

Correct any assertion above against Step 1's real DDL read if it disagrees — these are transcribed from research, not independently re-verified line-by-line in this plan document itself.

- [ ] **Step 3: Run to confirm all pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~SqlCatalogTests" --nologo
dotnet build All.sln --nologo
```

Expected: 0 warnings, 0 errors, all 15 tables' facts (5 pre-existing + 10 new) pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(search-sql): add SqlCatalog coverage for the 10 remaining leaf/composite tables

One fact-check per table, matching the established pattern -- proves
SqlCatalogGenerator correctly parsed all 15 target tables' real DDL, not
just the 5 already covered. Two confirmed real divergences worth keeping
as regression tests: TokenQuantityComposite's LowValue2/HighValue2 are
nullable (unlike the base Quantity/NumberSearchParam columns), and
TokenStringComposite's Text2 collation literal differs from the base
StringSearchParam.Text column."
```

---

### Task 5: `UriLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/UriLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/UriLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext` (Phase 4-5), `UriSearchValue` (`Ignixa.Search.Indexing.SearchValues`), `SqlCatalog.Default.Table("UriSearchParam")` (task 3/4).
- Produces: `UriLoweringRule.Lower(SearchParameterPredicateExpression, UriSearchValue, LeafContext): CteDefinition.ParamSource`, to be wired into `LeafLoweringDispatcher` in task 6.

- [ ] **Step 1: Verify `UriSearchValue`'s real shape and `SearchModifierCode`'s `Above`/`Below` members before writing code**

```bash
grep -n "class UriSearchValue" -A 15 src/Core/Ignixa.Search/Indexing/SearchValues/UriSearchValue.cs
grep -n "Above\|Below" src/Core/Ignixa.Specification/ValueSets/Normative/SearchModifierCode.cs
```

Confirm `UriSearchValue.Uri` is the field to compare, and that `SearchModifierCode.Above`/`.Below` are the real enum member names — correct the code below if they differ.

- [ ] **Step 2: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/UriLoweringRuleTests.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class UriLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    [Fact]
    public void GivenAPlainUriValue_WhenLowered_ThenComparesTheUriColumn()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new UriSearchValue("http://example.org/fhir/ValueSet/1", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88));

        // Assert
        cte.SearchParamId.ShouldBe((short)88);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Uri");
        equal.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/1");
    }

    [Fact]
    public void GivenAnAboveModifier_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringHierarchy()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Above), new UriSearchValue("http://example.org/fhir", separateCanonicalComponents: false));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88)));
    }

    [Fact]
    public void GivenABelowModifier_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringHierarchy()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Below), new UriSearchValue("http://example.org/fhir/ValueSet", separateCanonicalComponents: false));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88)));
    }
}
```

- [ ] **Step 3: Implement `UriLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/UriLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Uri search value to a ParamSource over UriSearchParam. Plain (no-modifier) equality only --
/// :above/:below (hierarchical URI matching) are not implemented and throw rather than silently
/// matching without the hierarchy constraint. Version/Fragment (canonical-URL extension fields) are
/// not in 97.sql's base UriSearchParam table -- they're populated via a separate post-merge extension
/// path -- so this rule, like SqlCatalog's UriSearchParam entry, covers only the base Uri column.
/// </summary>
public static class UriLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, UriSearchValue value, LeafContext context)
    {
        if (predicate.Modifier?.SearchModifierCode is SearchModifierCode.Above or SearchModifierCode.Below)
        {
            throw new NotSupportedException(
                $"Uri search with modifier '{predicate.Modifier.SearchModifierCode}' (hierarchical matching) is not " +
                "supported yet -- this rule only implements plain equality.");
        }

        var table = SqlCatalog.Default.Table("UriSearchParam");
        var column = new SqlColumnRef(table.TableName, "Uri");
        var predicateExpr = new Predicate.Equal(column, context.Parameter(value.Uri));

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
```

- [ ] **Step 4: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~UriLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all three tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add UriLoweringRule (plain equality only)

:above/:below (hierarchical URI matching) throw NotSupportedException
rather than silently matching without the hierarchy constraint --
matches the established pattern from TokenLoweringRule/ReferenceLoweringRule.
Version/Fragment extension fields are out of scope -- not in the base
UriSearchParam table this rule/catalog entry cover."
```

---

### Task 6: Wire into `LeafLoweringDispatcher`, end-to-end proof

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs`
- Test: extend `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `UriLoweringRule.Lower` (task 5).
- Produces: `UriSearchValue` now dispatches correctly through `Lower.Run`, proven end-to-end via `Resolve → Lower → Emit`.

- [ ] **Step 1: Wire the dispatcher**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs -- add one switch arm
public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Value switch
{
    StringSearchValue s => StringLoweringRule.Lower(predicate, s, context),
    TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context),
    ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context),
    UriSearchValue u => UriLoweringRule.Lower(predicate, u, context),
    _ => throw new NotSupportedException(
        $"No lowering rule for {predicate.Value.GetType().Name} -- Date/Number/Quantity and composites are out of scope for this plan."),
};
```

Update the `NotSupportedException` message's excluded-type list (currently says "Date/Number/Quantity/Uri and composites") to drop `Uri` now that it's handled.

- [ ] **Step 2: Add an end-to-end test**

```csharp
// test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs -- add this [Fact] to the existing class
[Fact]
public async Task GivenAValueSetUrlQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
{
    // Arrange -- ValueSet?url=http://example.org/fhir/ValueSet/1
    var urlParam = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
    var predicate = new SearchParameterPredicateExpression(
        urlParam, SearchComparator.Eq, modifier: null, new UriSearchValue("http://example.org/fhir/ValueSet/1", separateCanonicalComponents: false));
    var resolver = new FakeSymbolResolver();
    resolver.SearchParamIds[urlParam.Url!.ToString()] = 88;

    // Act
    var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);
    var plan = Lower.Run(predicate, symbolTable);
    var emitted = Emit.Run(plan);

    // Assert
    plan.Explain().ShouldBe("root = UriSearchParam[88]  Uri = @p0");
    emitted.Sql.ShouldNotContain("example.org");
    emitted.Parameters.ShouldContain(p => p.Value.Equals("http://example.org/fhir/ValueSet/1"));
}
```

Reuse the existing `FakeSymbolResolver` already defined in this test class (from task 10 of the phase 4-5 plan) — do not redefine it.

- [ ] **Step 3: Run to confirm it passes, build the full solution**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors, all green (aside from the known pre-existing `sql-on-fhir-tests` submodule gap).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(search-sql): wire UriLoweringRule into the dispatcher, prove it end to end

ValueSet?url=... compiles through Resolve -> Lower -> Emit correctly.
Closes this increment: SqlCatalog is now DDL-generated for all 15
*SearchParam/ResourceType tables, and Uri joins String/Token/Reference
as a working tier-1 leaf rule."
```

## Self-Review

- **Spec coverage:** Tasks 1-3 build and prove the generator (wiring, parser, real-DDL emission with byte-identical regression proof against Phase 3's hand-verified facts). Task 4 extends test coverage to all 10 newly-available tables. Tasks 5-6 add and wire `UriLoweringRule`, the next tier-1 leaf rule, matching every scope decision from the Global Constraints (no `:above`/`:below`, no `Version`/`Fragment` extension columns).
- **Placeholder scan:** Task 1's relative-path claim, task 5's `SearchModifierCode`/`UriSearchValue` shape, and task 4's transcribed DDL facts are all marked "verify against real source before finalizing" — matching the established honest-deferral pattern from every prior phase's plan in this repo.
- **Type consistency:** `DdlColumn`/`DdlTable`/`DdlTableParser.ParseTables`, `SqlCatalog.BuildFromDdl()`, `UriLoweringRule.Lower(SearchParameterPredicateExpression, UriSearchValue, LeafContext): CteDefinition.ParamSource` are used identically everywhere they appear across tasks 2-6 — checked for drift, none found.
- **Scope discipline:** Generating catalog *data* for all 15 tables (rather than just the 6 currently consumed) is the one deliberate exception to "only build what's consumed" in this plan, justified explicitly in the Global Constraints (zero marginal cost, inert data not machinery). No other scope creep — `Date`/`Number`/`Quantity`/composites/`Not` remain untouched, exactly as the user's phase-pacing decision required.
