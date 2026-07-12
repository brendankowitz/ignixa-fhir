# Storage Convention Consolidation (Phase 3, Step 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix four confirmed, live correctness bugs caused by storage facts being independently
re-encoded on the write path (`RowGenerators/*.cs`) and read path (`Search/*QueryGenerator.cs`) for
composite search parameters, by extending the existing `TokenCodeStorage` convention-sharing pattern
(and adding its `StringStorage` sibling) so both sides consult one source instead of two drifting copies.

**Architecture:** Two small static helper classes (`TokenCodeStorage`, extended; new `StringStorage`)
hold the shared width/collation constants. Six composite row generators switch from a hardcoded 128-char
token-code split to `TokenCodeStorage`'s existing 256-char logic; one of them additionally switches its
string-component split and normalization to the new `StringStorage`. Five composite read methods in
`CompositeSearchParameterQueryGenerator.cs` switch from ordinal equality to the same
`EF.Functions.Collate(...)`-plus-overflow-concatenation pattern the single-parameter read paths in
`SearchParameterQueryGenerator.cs` already use. Because this makes `EF.Functions.Collate` load-bearing
for composite token/string queries — which the EF Core InMemory test provider cannot translate — the
existing composite unit tests that exercise those queries move to `Ignixa.Api.E2ETests` (real SQL
Server), alongside new characterization tests proving each bug fix.

**Tech Stack:** C# / .NET 10, EF Core, xUnit + Shouldly, existing `Ignixa.DataLayer.SqlEntityFramework`/
`Ignixa.Api.E2ETests` architecture — no new dependencies.

## Global Constraints

- Build: `dotnet build All.sln` must be 0 warnings, 0 errors after every task.
- Test: `dotnet test` on touched projects must be green after every task. Pre-existing unrelated
  failures — 5 documented `Ignixa.DataLayer.LegacySqlEF.Tests` failures from the EF Core InMemory
  provider's `EF.Constant()`/`Collate` translation gap (`ChainedExpressionProcessorTests` ×4,
  `IterateProcessorTests` ×1), and SQL-on-FHIR conformance submodule drift (`Ignixa.SqlOnFhir.Tests`
  ×2 per TFM) — are expected and not a blocker. This plan **adds** to the expected-failure list: once
  Task 6 lands, running the composite unit tests removed by Task 6 (if anyone runs the class directly by
  name rather than via the suite) is not possible — they no longer exist there. Task 10 documents the
  final, complete expected-failure baseline.
- No `#region` blocks. 4-space indentation. File-scoped namespaces.
- Do not use `run_in_background` for `dotnet build`/`dotnet test` commands, and never pass `--no-build`
  — a prior phase on this branch shipped a real regression because a task's own verification used
  `--no-build` and silently tested a stale assembly. Every build/test command in every task must be a
  normal foreground call whose actual output is read before proceeding.
- Full design context: `docs/superpowers/specs/2026-07-12-storage-convention-consolidation-design.md`.
- **Do not touch** `RowGenerators/ResourceWriteClaimRowGenerator.cs`, `ReferenceResourceVersion`
  implementation, or `SearchParamHash` implementation — out of scope per the spec's Non-goals (comment
  retagging in Task 9 is the only touch either TODO gets).
- **Do not build a full per-type declarative descriptor** (table/columns/widths/collation/normalization
  all in one data structure consumed generically by both paths) — that is Step 2, explicitly deferred to
  its own future investigation. This plan's helpers are Step 2's raw material, not Step 2 itself.

---

### Task 1: Extend `TokenCodeStorage` with a shared case-insensitive collation constant

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/TokenCodeStorage.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs:1276,1296,1301,1513,1519`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/TokenCodeStorageTests.cs` (extend)

**Interfaces:**
- Produces: `TokenCodeStorage.CaseInsensitiveCollation` (`public const string`, value
  `"Latin1_General_100_CI_AS"`), consumed by Task 5.

This task is pure extraction — it does not change behavior. It replaces 5 independent occurrences of the
same string literal with one named constant, so Task 5 (which adds a 6th+ set of occurrences in the
composite read methods) has one source to reference instead of copying the literal a 6th time.

- [ ] **Step 1: Write the failing test**

Add to `test/Ignixa.DataLayer.SqlEntityFramework.Tests/TokenCodeStorageTests.cs` (read the file first to
match its existing class/namespace structure exactly):

```csharp
[Fact]
public void GivenCaseInsensitiveCollation_WhenRead_ThenMatchesExpectedSqlServerCollationName()
{
    TokenCodeStorage.CaseInsensitiveCollation.ShouldBe("Latin1_General_100_CI_AS");
}
```

(Note: collation *behavior* can only be verified against a real SQL Server, not EF InMemory — this test
only pins the constant's literal value, which is all a unit test can meaningfully assert here. Real
collation behavior is exercised end-to-end by Task 7's E2E tests.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~GivenCaseInsensitiveCollation_WhenRead_ThenMatchesExpectedSqlServerCollationName"`
Expected: FAIL to compile — `TokenCodeStorage.CaseInsensitiveCollation` doesn't exist yet.

- [ ] **Step 3: Add the constant**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/TokenCodeStorage.cs`, add after
`MaxInlineCodeLength` (currently ends at line 26):

```csharp

    /// <summary>
    /// The collation used to compare token codes case-insensitively at query time, per FHIR R4's
    /// search.html guidance: "When in doubt, servers SHOULD treat tokens in a case-insensitive manner,
    /// on the grounds that including undesired data has less safety implications than excluding
    /// desired behavior." Applied identically to single-parameter and composite token code comparisons.
    /// </summary>
    public const string CaseInsensitiveCollation = "Latin1_General_100_CI_AS";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~GivenCaseInsensitiveCollation_WhenRead_ThenMatchesExpectedSqlServerCollationName"`
Expected: PASS, 1/1.

- [ ] **Step 5: Replace the 5 magic-string occurrences in `SearchParameterQueryGenerator.cs`**

Read the file first to confirm the 5 occurrences are still at (or near) lines 1276, 1296, 1301, 1513,
1519 (line numbers may have shifted slightly since the design spec was written — search for the literal
`"Latin1_General_100_CI_AS"` to find all current occurrences rather than trusting the exact line
numbers). Replace each occurrence of the literal string `"Latin1_General_100_CI_AS"` with
`TokenCodeStorage.CaseInsensitiveCollation`. Example (line ~1276):

```csharp
// Before:
query = query.Where(sp => EF.Functions.Collate(sp.IdentifierTypeCode, "Latin1_General_100_CI_AS") == identifierTypeCode);

// After:
query = query.Where(sp => EF.Functions.Collate(sp.IdentifierTypeCode, TokenCodeStorage.CaseInsensitiveCollation) == identifierTypeCode);
```

Apply the identical mechanical replacement at every other occurrence (the two in
`GenerateTokenQueryAsync`'s code-comparison branches, ~1296 and ~1301/1513/1519 — read the file to find
all 5 and replace each one; do not skip any).

- [ ] **Step 6: Run existing single-token tests to confirm no behavior change**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGenerator"`
Expected: same pass/fail counts as before this task's Step 5 (pure string-literal substitution, no
behavior change) — confirm this by running the same filter before Step 5 too and comparing.

- [ ] **Step 7: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/TokenCodeStorage.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/TokenCodeStorageTests.cs
git commit -m "refactor(sql): extract shared CaseInsensitiveCollation constant into TokenCodeStorage"
```

---

### Task 2: New `StringStorage` helper

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/StringStorage.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`
  (`GenerateStringQueryAsync`, ~lines 1370-1481)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/StringStorageTests.cs` (new)

**Interfaces:**
- Produces: `StringStorage.InlineWidth` (`public const int`, `256`), `StringStorage.DefaultCollation`
  (`public const string`, `"Latin1_General_100_CI_AI"`), `StringStorage.ExactCollation` (`public const
  string`, `"Latin1_General_100_CS_AS"`), `StringStorage.Split(string value)` (`public static (string
  Inline, string? Overflow) Split(string value)`, splitting at `InlineWidth`, mirroring
  `TokenCodeStorage.SplitCode`'s exact shape). Consumed by Task 4 and Task 6.

This is the string-column sibling of `TokenCodeStorage`, following the identical pattern. Extracting it
now (before Task 4/6 need it) keeps those tasks focused on applying the helper rather than inventing it.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.DataLayer.SqlEntityFramework.Tests/StringStorageTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

public class StringStorageTests
{
    [Fact]
    public void GivenConstants_WhenRead_ThenMatchExpectedValues()
    {
        StringStorage.InlineWidth.ShouldBe(256);
        StringStorage.DefaultCollation.ShouldBe("Latin1_General_100_CI_AI");
        StringStorage.ExactCollation.ShouldBe("Latin1_General_100_CS_AS");
    }

    [Fact]
    public void GivenValueAtOrUnderInlineWidth_WhenSplit_ThenNoOverflow()
    {
        var (inline, overflow) = StringStorage.Split(new string('a', 256));

        inline.Length.ShouldBe(256);
        overflow.ShouldBeNull();
    }

    [Fact]
    public void GivenValueOverInlineWidth_WhenSplit_ThenSplitsAtBoundary()
    {
        var value = new string('a', 300);

        var (inline, overflow) = StringStorage.Split(value);

        inline.Length.ShouldBe(256);
        overflow.ShouldBe(new string('a', 44));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~StringStorageTests"`
Expected: FAIL to compile — `StringStorage` doesn't exist yet.

- [ ] **Step 3: Create `StringStorage`**

Create `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/StringStorage.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Encodes the storage conventions for string search parameter text columns, shared by the write path
/// (RowGenerators) and read path (Search query generators) so the two are never allowed to drift - the
/// string-column sibling of <see cref="TokenCodeStorage"/>.
/// </summary>
public static class StringStorage
{
    /// <summary>
    /// String values at or under this length are stored inline in the Text column; longer values are
    /// truncated to this length with the remainder in TextOverflow. Must match the StringSearchParam.Text
    /// and TokenStringCompositeSearchParam.Text2 columns' NVARCHAR(256) width.
    /// </summary>
    public const int InlineWidth = 256;

    /// <summary>
    /// Collation for FHIR string search's default (no modifier) and :contains/:starts-with matching -
    /// case-insensitive, accent-insensitive.
    /// </summary>
    public const string DefaultCollation = "Latin1_General_100_CI_AI";

    /// <summary>
    /// Collation for FHIR string search's :exact modifier - case-sensitive, accent-sensitive.
    /// </summary>
    public const string ExactCollation = "Latin1_General_100_CS_AS";

    /// <summary>
    /// Splits a string value into its inline and overflow parts per <see cref="InlineWidth"/>.
    /// </summary>
    public static (string Inline, string? Overflow) Split(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Length > InlineWidth
            ? (value[..InlineWidth], value[InlineWidth..])
            : (value, null);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~StringStorageTests"`
Expected: PASS, 3/3.

- [ ] **Step 5: Route `SearchParameterQueryGenerator.cs`'s `GenerateStringQueryAsync` collation literals through `StringStorage`**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`, in
`GenerateStringQueryAsync` (~line 1400), replace:

```csharp
var collation = ignoreCase ? "Latin1_General_100_CI_AI" : "Latin1_General_100_CS_AS";
```

with:

```csharp
var collation = ignoreCase ? StringStorage.DefaultCollation : StringStorage.ExactCollation;
```

Also replace the two occurrences of the literal `256` used as the inline-width threshold in the same
method (`searchText.Length > 256` in the `StartsWith` case, ~line 1411) with `StringStorage.InlineWidth`,
for consistency (read the method in full first to confirm there isn't a third occurrence you'd miss).

- [ ] **Step 6: Route `StringSearchParameterRowGenerator.cs` through `StringStorage`**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs`,
replace the private `StringColumnMaxLength` constant (currently `private const int StringColumnMaxLength
= 256;`, line 35) and its two usages with `StringStorage.InlineWidth`, and replace the inline
split-at-threshold logic (lines 81-93) with a call to `StringStorage.Split`:

```csharp
// Before:
var textValue = stringValue.String;
if (textValue != null && textValue.Length > StringColumnMaxLength)
{
    record.SetString(3, textValue.Substring(0, StringColumnMaxLength));
    record.SetString(4, textValue.Substring(StringColumnMaxLength));
}
else
{
    if (textValue != null)
        record.SetString(3, textValue);
    else
        record.SetDBNull(3);
    record.SetDBNull(4);
}

// After:
var textValue = stringValue.String;
if (textValue != null)
{
    var (inline, overflow) = StringStorage.Split(textValue);
    record.SetString(3, inline);
    if (overflow != null)
        record.SetString(4, overflow);
    else
        record.SetDBNull(4);
}
else
{
    record.SetDBNull(3);
    record.SetDBNull(4);
}
```

Delete the now-unused `private const int StringColumnMaxLength = 256;` field and its doc comment.

- [ ] **Step 7: Run existing single-String tests to confirm no behavior change**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~String"`
Expected: same pass/fail counts as before Steps 5-6 (pure extraction, no behavior change) — run the same
filter before making the changes too and compare.

- [ ] **Step 8: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 9: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/StringStorage.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/StringStorageTests.cs
git commit -m "feat(sql): add StringStorage helper, route single-String path through it"
```

---

### Task 3: Fix composite token-code overflow threshold on the write path (6 generators)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenTokenCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/RefTokenCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenDateTimeCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenQuantityCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenStringCompositeRowGenerator.cs`
  (token component only — the string component is Task 4)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenNumberNumberCompositeRowGenerator.cs`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/CompositeTokenOverflowTests.cs` (new)

**Interfaces:**
- Consumes: `TokenCodeStorage.SplitCode(string)` (existing), `TokenCodeStorage.MaxInlineCodeLength`
  (existing, `256`).
- Produces: all 6 composite row generators now split token codes at 256 chars (matching the actual
  `VARCHAR(256)` column width in `97.sql`) instead of the wrong hardcoded 128, using the identical
  `TokenCodeStorage.SplitCode` logic the single-token generator already uses. No new public API — this
  is a bug fix inside existing methods.

Every one of the 6 files has the identical bug in the identical shape (confirmed by direct read of all
6 during plan-writing): a token component's code is split with `Code.Length > 128 ?
Code.Substring(0,128)/Code.Substring(128) : Code` inline, against a `VARCHAR(256)` TVP/table column.
This task replaces that inline logic with `TokenCodeStorage.SplitCode` in all 6 files. The exact
column indices differ per file (confirmed below) — use the correct index for each file, don't
copy-paste blindly.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/CompositeTokenOverflowTests.cs`.
No prior unit test in this project constructs a `CompositeSearchValue`/`ResourceWrapper` pair directly
(confirmed by repo-wide search during plan-writing) — the constructors below were read directly from
source, not copied from an example: `ResourceWrapper` is a positional record
(`src/Application/Ignixa.Domain/Models/ResourceWrapper.cs:9-16`, `SearchIndices` is a separate
`init`-only `IReadOnlyList<object>?` property, not a constructor parameter);
`SearchIndexEntry(SearchParameterInfo, ISearchValue)`; `CompositeSearchValue(IReadOnlyList<IReadOnlyList<ISearchValue>>
components)`; `TokenSearchValue(string? system, string? code, string? text)` (3 parameters — `text` is
required even when null); `SearchParameterIdLookupHelper.TryGetSearchParamId` (called internally by
every row generator) matches by `searchParameter.Url.ToString()` against the `searchParameterIdMap`
dictionary, so the test `SearchParameterInfo` needs a non-null `Url` matching a dictionary key:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.RowGenerators;

/// <summary>
/// Regression coverage for the confirmed bug: all 6 composite row generators split token codes at a
/// hardcoded 128 characters instead of TokenCodeStorage.MaxInlineCodeLength (256), which is the actual
/// width of the Code1/Code2 TVP and table columns - codes between 129 and 256 characters were being
/// truncated and overflowed unnecessarily, and (before the read-side fix in Task 5) never matched at all.
/// This test file only proves the WRITE side now splits at 256 - read-side matching is proven end-to-end
/// by the E2E tests added in Task 7.
/// </summary>
public class CompositeTokenOverflowTests
{
    private const string LongCode1 = "z0123456789z0123456789z0123456789z0123456789z0123456789" +
        "z0123456789z0123456789z0123456789z0123456789z0123456789" +
        "z0123456789z0123456789z0123456789z0123456789z0123456789" +
        "z0123456789z0123456789z0123456789z0123456789z0123456789z01234"; // 260 chars total

    private static readonly Uri TestParamUrl = new("http://example.org/SearchParameter/test-composite");
    private static readonly SearchParameterInfo TestCompositeParam =
        new("test-composite", "test-composite", SearchParamType.Composite, TestParamUrl);

    private static readonly IReadOnlyDictionary<string, int> EmptySystemMappings =
        new Dictionary<string, int>();
    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 3 };
    private static readonly IReadOnlyDictionary<string, short> SearchParameterIdMap =
        new Dictionary<string, short> { [TestParamUrl.ToString()] = 1 };

    private static ResourceWrapper CreateResourceWithComposite(CompositeSearchValue compositeValue)
    {
        var entry = new SearchIndexEntry(TestCompositeParam, compositeValue);
        return new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "obs-1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Observation", Id = "obs-1" },
            Request: new ResourceRequest("POST", "Observation"),
            IsDeleted: false)
        {
            SearchIndices = [entry],
        };
    }

    [Fact]
    public void GivenTokenTokenCompositeWithLongCode_WhenGenerated_ThenSplitsAt256NotAt128()
    {
        LongCode1.Length.ShouldBe(260);

        var compositeValue = new CompositeSearchValue(
            [
                [new TokenSearchValue(null, LongCode1, null)],
                [new TokenSearchValue(null, "short", null)],
            ]);
        var resource = CreateResourceWithComposite(compositeValue);
        var generator = new TokenTokenCompositeRowGenerator(EmptySystemMappings);

        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParameterIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 100L }).ToList();

        records.ShouldHaveSingleItem();
        // Column 4 = Code1 (inline), Column 5 = CodeOverflow1
        records[0].GetString(4).Length.ShouldBe(256);
        records[0].IsDBNull(5).ShouldBeFalse();
        records[0].GetString(5).ShouldBe(LongCode1[256..]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeTokenOverflowTests"`
Expected: FAIL — `records[0].GetString(4).Length` is 128 (the current wrong threshold), not 256.

- [ ] **Step 3: Fix `TokenTokenCompositeRowGenerator.cs`**

Replace both split blocks (currently lines 102-105 and 131-134 — verify against the file's current state
first) with calls to `TokenCodeStorage.SplitCode`. First occurrence (token component 1, columns 4/5):

```csharp
// Before:
if (tokenComponent1.Code != null && tokenComponent1.Code.Length > 128)
{
    record.SetString(4, tokenComponent1.Code.Substring(0, 128));
    record.SetString(5, tokenComponent1.Code.Substring(128));
}
else
{
    if (tokenComponent1.Code != null)
        record.SetString(4, tokenComponent1.Code);
    else
        record.SetDBNull(4);
    record.SetDBNull(5);
}

// After:
if (tokenComponent1.Code != null)
{
    var (inline1, overflow1) = TokenCodeStorage.SplitCode(tokenComponent1.Code);
    record.SetString(4, inline1);
    if (overflow1 != null)
        record.SetString(5, overflow1);
    else
        record.SetDBNull(5);
}
else
{
    record.SetDBNull(4);
    record.SetDBNull(5);
}
```

Second occurrence (token component 2, columns 7/8) — identical shape, substitute `tokenComponent2`,
columns 7/8, and local variable names `inline2`/`overflow2`.

Also update the `SqlMetaData` declaration for `Code1`/`Code2` from `new SqlMetaData("Code1",
SqlDbType.VarChar, 128)` to `new SqlMetaData("Code1", SqlDbType.VarChar, TokenCodeStorage.MaxInlineCodeLength)`
(and the same for `Code2`) — the metadata width must match the actual split width or `SqlDataRecord`
will throw when setting a 256-char inline value against a 128-char-declared column.

- [ ] **Step 4: Fix `RefTokenCompositeRowGenerator.cs`, `TokenDateTimeCompositeRowGenerator.cs`, `TokenQuantityCompositeRowGenerator.cs`, `TokenNumberNumberCompositeRowGenerator.cs`**

Apply the identical transformation (split-block replacement + `SqlMetaData` width fix) to each file's
single token-code split block. Column indices per file (confirmed by direct read):
- `RefTokenCompositeRowGenerator.cs`: token code at columns 8/9 (`Code2`/`CodeOverflow2`), lines 131-134.
- `TokenDateTimeCompositeRowGenerator.cs`: token code at columns 4/5 (`Code1`/`CodeOverflow1`), lines 102-105.
- `TokenQuantityCompositeRowGenerator.cs`: token code at columns 4/5 (`Code1`/`CodeOverflow1`), lines 109-112.
- `TokenNumberNumberCompositeRowGenerator.cs`: token code at columns 4/5 (`Code1`/`CodeOverflow1`), lines 110-113.

Each file's `SqlMetaData` for its `Code1`/`Code2` column also needs the same width fix (128 → 
`TokenCodeStorage.MaxInlineCodeLength`).

- [ ] **Step 5: Fix `TokenStringCompositeRowGenerator.cs`'s token component only**

This file has both a token-code split (columns 4/5) and a separate string split (columns 6/7, Task 4's
concern). Fix only the token-code split here (lines 102-105), identical transformation, and its
`SqlMetaData` width for `Code1`. Do not touch the string component's split (lines 117-130) or its
`SqlMetaData` for `Text2`/`TextOverflow2` in this task.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeTokenOverflowTests"`
Expected: PASS, 1/1.

- [ ] **Step 7: Full build and existing-test regression check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: same 8/8 pass as before this task — this task only changes write-path code, and these tests
seed data directly into the `Context` (bypassing the row generators entirely), so they're unaffected by
this task. This is expected to change starting in Task 5/6, not here.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenTokenCompositeRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/RefTokenCompositeRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenDateTimeCompositeRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenQuantityCompositeRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenStringCompositeRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenNumberNumberCompositeRowGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/CompositeTokenOverflowTests.cs
git commit -m "fix(sql): correct composite token code overflow threshold from 128 to 256 (write path)"
```

---

### Task 4: Fix `TokenStringCompositeRowGenerator`'s string-component width and normalization (write path)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenStringCompositeRowGenerator.cs`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/CompositeStringOverflowTests.cs` (new)

**Interfaces:**
- Consumes: `StringStorage.Split(string)`, `StringStorage.InlineWidth` (Task 2).
- Produces: `TokenStringCompositeRowGenerator` now splits `Text2` at 256 chars (matching the actual
  `NVARCHAR(256)` column width) instead of the wrong hardcoded 128, and stores original case instead of
  `ToUpperInvariant()`.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/CompositeStringOverflowTests.cs`,
reusing the exact `ResourceWrapper`/`SearchIndexEntry`/`SearchParameterInfo` construction Task 3's
`CompositeTokenOverflowTests.cs` established (that task runs first and its file is already committed by
the time this task starts — copy its `TestParamUrl`/`TestCompositeParam`/`ResourceTypeIdMap`/
`SearchParameterIdMap`/`CreateResourceWithComposite` block verbatim into this new file rather than
re-deriving it, since both files need the identical fixture):

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.RowGenerators;

/// <summary>
/// Regression coverage for two confirmed bugs in TokenStringCompositeRowGenerator's string component:
/// (1) Text2 was split at a hardcoded 128 chars instead of the actual NVARCHAR(256) column width, and
/// (2) the value was stored ToUpperInvariant(), permanently destroying original case and (per the design
/// spec) providing no matching benefit today, since Text2's collation is already case-insensitive.
/// </summary>
public class CompositeStringOverflowTests
{
    private static readonly Uri TestParamUrl = new("http://example.org/SearchParameter/test-composite");
    private static readonly SearchParameterInfo TestCompositeParam =
        new("test-composite", "test-composite", SearchParamType.Composite, TestParamUrl);

    private static readonly IReadOnlyDictionary<string, int> EmptySystemMappings =
        new Dictionary<string, int>();
    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 3 };
    private static readonly IReadOnlyDictionary<string, short> SearchParameterIdMap =
        new Dictionary<string, short> { [TestParamUrl.ToString()] = 1 };

    private static ResourceWrapper CreateResourceWithComposite(CompositeSearchValue compositeValue)
    {
        var entry = new SearchIndexEntry(TestCompositeParam, compositeValue);
        return new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "obs-1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Observation", Id = "obs-1" },
            Request: new ResourceRequest("POST", "Observation"),
            IsDeleted: false)
        {
            SearchIndices = [entry],
        };
    }

    [Fact]
    public void GivenMixedCaseStringComponent_WhenGenerated_ThenStoresOriginalCaseNotUppercased()
    {
        var compositeValue = new CompositeSearchValue(
            [
                [new TokenSearchValue(null, "code1", null)],
                [new StringSearchValue("Smith")],
            ]);
        var resource = CreateResourceWithComposite(compositeValue);
        var generator = new TokenStringCompositeRowGenerator(EmptySystemMappings);

        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParameterIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 100L }).ToList();

        records.ShouldHaveSingleItem();
        records[0].GetString(6).ShouldBe("Smith"); // Column 6 = Text2 - original case, not "SMITH"
    }

    [Fact]
    public void GivenStringComponentOver256Chars_WhenGenerated_ThenSplitsAt256NotAt128()
    {
        var longText = new string('a', 260);
        var compositeValue = new CompositeSearchValue(
            [
                [new TokenSearchValue(null, "code1", null)],
                [new StringSearchValue(longText)],
            ]);
        var resource = CreateResourceWithComposite(compositeValue);
        var generator = new TokenStringCompositeRowGenerator(EmptySystemMappings);

        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParameterIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 100L }).ToList();

        records.ShouldHaveSingleItem();
        records[0].GetString(6).Length.ShouldBe(256); // Text2 inline
        records[0].IsDBNull(7).ShouldBeFalse(); // TextOverflow2
        records[0].GetString(7).ShouldBe(longText[256..]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeStringOverflowTests"`
Expected: FAIL — first test fails because the stored value is `"SMITH"` not `"Smith"`; second fails
because inline length is 128 not 256.

- [ ] **Step 3: Fix `TokenStringCompositeRowGenerator.cs`'s string component**

Replace the string-handling block (currently lines 117-130):

```csharp
// Before:
// String component
var textValue = stringComponent.String?.ToUpperInvariant();
if (textValue != null && textValue.Length > StringColumnMaxLength)
{
    record.SetString(6, textValue.Substring(0, StringColumnMaxLength));
    record.SetString(7, textValue.Substring(StringColumnMaxLength));
}
else
{
    if (textValue != null)
        record.SetString(6, textValue);
    else
        record.SetDBNull(6);
    record.SetDBNull(7);
}

// After:
// String component - stores original case; matching normalization (default CI_AI vs :exact CS_AS)
// happens at query time via collation, mirroring StringSearchParameterRowGenerator's single-String
// convention (see StringStorage).
var textValue = stringComponent.String;
if (textValue != null)
{
    var (inline, overflow) = StringStorage.Split(textValue);
    record.SetString(6, inline);
    if (overflow != null)
        record.SetString(7, overflow);
    else
        record.SetDBNull(7);
}
else
{
    record.SetDBNull(6);
    record.SetDBNull(7);
}
```

Delete the now-unused `private const int StringColumnMaxLength = 128;` field (line 20) and update the
`SqlMetaData` for `Text2` from `new SqlMetaData("Text2", SqlDbType.NVarChar, 128)` to `new
SqlMetaData("Text2", SqlDbType.NVarChar, StringStorage.InlineWidth)`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeStringOverflowTests"`
Expected: PASS, 2/2.

- [ ] **Step 5: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenStringCompositeRowGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/CompositeStringOverflowTests.cs
git commit -m "fix(sql): store composite string component in original case at correct 256-char width"
```

---

### Task 5: Fix composite token-code comparison on the read path (case-insensitive, overflow-aware)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`
  (`GenerateTokenTokenQueryAsync`, `GenerateTokenQuantityQueryAsync`, `GenerateTokenStringQueryAsync`
  token half, `GenerateReferenceTokenQueryAsync`, `GenerateTokenDateTimeQueryAsync`)

**Interfaces:**
- Consumes: `TokenCodeStorage.CaseInsensitiveCollation`, `TokenCodeStorage.MaxInlineCodeLength` (Task 1).
- Produces: all 5 composite read methods that compare a token component now use the identical
  `EF.Functions.Collate(...)` + overflow-concatenation pattern
  `SearchParameterQueryGenerator.GenerateTokenQueryAsync` (~lines 1509-1521) already uses for
  single-token reads, instead of ordinal `t.CodeN == token.Code`. No signature changes.

**This task has no new unit tests of its own** — EF Core InMemory cannot translate
`EF.Functions.Collate`, so this method's own correctness is proven by Task 7's E2E tests, not a unit
test here. This task's own verification is: (a) the existing `dotnet build All.sln` stays clean, and (b)
Task 7's re-homed tests (not yet moved when this task runs) will be the first tests to actually exercise
this code — do not attempt to add InMemory unit tests for this task, they cannot work (see Global
Constraints and the design spec's Testing strategy section for why).

- [ ] **Step 1: Fix `GenerateTokenTokenQueryAsync`'s two token-code comparisons**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`,
replace (currently lines 162-166):

```csharp
// Apply first component filter
if (!string.IsNullOrEmpty(token1.Code))
{
    query = query.Where(t => t.Code1 == token1.Code);
}
```

with:

```csharp
// Apply first component filter
if (!string.IsNullOrEmpty(token1.Code))
{
    query = token1.Code.Length > TokenCodeStorage.MaxInlineCodeLength
        ? query.Where(t => t.CodeOverflow1 != null &&
            EF.Functions.Collate(t.Code1 + t.CodeOverflow1, TokenCodeStorage.CaseInsensitiveCollation) == token1.Code)
        : query.Where(t => EF.Functions.Collate(t.Code1, TokenCodeStorage.CaseInsensitiveCollation) == token1.Code);
}
```

Apply the identical transformation to the second component filter (currently lines 178-182, `token2`/
`Code2`/`CodeOverflow2`).

- [ ] **Step 2: Fix `GenerateTokenQuantityQueryAsync`'s token-code comparison**

Replace (currently lines 227-231):

```csharp
// Apply first component (token) filter
if (!string.IsNullOrEmpty(token.Code))
{
    query = query.Where(t => t.Code1 == token.Code);
}
```

with the same pattern (`token`/`Code1`/`CodeOverflow1`).

- [ ] **Step 3: Fix `GenerateTokenStringQueryAsync`'s token-code comparison (not the string comparison — that's Task 6)**

Replace (currently lines 278-282):

```csharp
// Apply first component (token) filter
if (!string.IsNullOrEmpty(token.Code))
{
    query = query.Where(t => t.Code1 == token.Code);
}
```

with the same pattern (`token`/`Code1`/`CodeOverflow1`). Do not touch the string-component filter
(lines 293-299) in this task.

- [ ] **Step 4: Fix `GenerateReferenceTokenQueryAsync`'s token-code comparison**

Replace (currently lines 355-358):

```csharp
// Apply second component (token) filter
if (!string.IsNullOrEmpty(token.Code))
{
    query = query.Where(r => r.Code2 == token.Code);
}
```

with the same pattern (`token`/`Code2`/`CodeOverflow2`, and the query variable is `r` not `t` in this
method — match the existing lambda parameter name).

- [ ] **Step 5: Fix `GenerateTokenDateTimeQueryAsync`'s token-code comparison**

Replace (currently lines 402-406):

```csharp
// Apply first component (token) filter
if (!string.IsNullOrEmpty(token.Code))
{
    query = query.Where(t => t.Code1 == token.Code);
}
```

with the same pattern (`token`/`Code1`/`CodeOverflow1`).

- [ ] **Step 6: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Confirm existing composite unit tests now fail as expected (not a regression — expected, tracked, resolved in Task 7)**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: 7 of the 8 tests now FAIL with `InvalidOperationException` (EF InMemory cannot translate
`EF.Functions.Collate`) — specifically every test except
`GivenReferenceTokenComposite_WhenComponentsPassedInWrongOrder_ThenReturnsEmptyWithoutApplyingSpuriousFilters`
(which returns via the early-exit guard clause before ever reaching a `Collate`-using comparison, so it
is unaffected). This is the exact, planned consequence described in the design spec's Testing strategy —
do not attempt to fix it here. Confirm the failure reason is `InvalidOperationException` (translation
failure), not a wrong-result assertion failure — if any test fails with a different exception or an
assertion mismatch instead, stop and report BLOCKED, since that would indicate this task's transformation
has a bug beyond the expected InMemory limitation.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs
git commit -m "fix(sql): converge composite token comparison to case-insensitive, overflow-aware (read path)"
```

---

### Task 6: Fix composite string comparison on the read path (original case, collation-based, overflow-aware)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`
  (`GenerateTokenStringQueryAsync`'s string half)

**Interfaces:**
- Consumes: `StringStorage.DefaultCollation`, `StringStorage.InlineWidth` (Task 2).
- Produces: `GenerateTokenStringQueryAsync`'s string-component comparison now uses collation-based,
  overflow-aware matching mirroring `SearchParameterQueryGenerator.GenerateStringQueryAsync`'s
  `StartsWith` case (~lines 1406-1430), instead of `ToUpperInvariant()` + ordinal `StartsWith`.

**No new unit test for the same reason as Task 5** — this makes `EF.Functions.Collate` load-bearing for
this method, which EF InMemory can't translate. Proven by Task 7's E2E tests.

- [ ] **Step 1: Fix the string-component filter**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`'s
`GenerateTokenStringQueryAsync`, replace (currently lines 293-299):

```csharp
// Apply second component (string) filter
var stringValue = ExtractStringValue(component1);
if (!string.IsNullOrEmpty(stringValue))
{
    var normalizedValue = stringValue.ToUpperInvariant();
    query = query.Where(t => t.Text2.StartsWith(normalizedValue));
}
```

with:

```csharp
// Apply second component (string) filter - collation-based, overflow-aware, mirroring
// SearchParameterQueryGenerator.GenerateStringQueryAsync's single-String StartsWith case.
var stringValue = ExtractStringValue(component1);
if (!string.IsNullOrEmpty(stringValue))
{
    var pattern = $"{stringValue}%";
    query = stringValue.Length > StringStorage.InlineWidth
        ? query.Where(t => t.TextOverflow2 != null &&
            EF.Functions.Like(EF.Functions.Collate(t.Text2 + t.TextOverflow2, StringStorage.DefaultCollation), pattern))
        : query.Where(t => EF.Functions.Like(EF.Functions.Collate(t.Text2, StringStorage.DefaultCollation), pattern));
}
```

(This composite method only ever did a starts-with match, unlike the single-String path's full
StartsWith/Contains/EndsWith/Equals/`:exact` operator switch — this task preserves that existing
narrower scope, it does not add operator/modifier support the composite path didn't have before. Adding
`:exact` support to composite string search is a reasonable future follow-up, not required by this
phase's stated bugs.)

- [ ] **Step 2: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 3: Confirm the remaining composite `Token|String` unit test now also fails as expected**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~GivenTokenStringComposite_WhenStringPrefixMatches_ThenReturnsResource"`
Expected: FAIL with `InvalidOperationException` (already failing since Task 5's token-code fix touched
this same method's token half — confirm the failure is still the same translation-failure class, not a
new/different failure introduced by this task's string-half change).

- [ ] **Step 4: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs
git commit -m "fix(sql): converge composite string comparison to original-case, collation-based matching (read path)"
```

---

### Task 7: Re-home affected composite unit tests to E2E, add new characterization tests

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs`
  (delete 7 of 8 test methods — keep only the wrong-order guard test)
- Create: `test/Ignixa.Api.E2ETests/Search/DataTypes/CompositeSearchTests.cs` (or extend it if it already
  exists — check first)

**Interfaces:**
- Consumes: `Ignixa.Api.E2ETests._Infrastructure.IgnixaApiFixture`, `CapabilityDrivenTestBase`,
  `Harness.SearchAsync(resourceType, query)` (existing E2E test infrastructure, same pattern used by
  `test/Ignixa.Api.E2ETests/Search/DataTypes/TokenSearchTests.cs`).
- Produces: E2E characterization tests proving all 4 confirmed bugs are fixed, running against a real
  SQL Server where `EF.Functions.Collate` actually executes.

**This is the task that closes the loop on Tasks 5-6's expected unit-test breakage.** The following 7 of
8 existing tests in `CompositeSearchParameterQueryGeneratorTests.cs` now throw
`InvalidOperationException` under EF InMemory (confirmed during Task 5/6's own verification) and must be
deleted from that file (their coverage is superseded by this task's new E2E tests, not lost):
`GivenTokenTokenComposite_WhenBothComponentsMatch_ThenReturnsResource`,
`GivenTokenQuantityComposite_WhenValueInRange_ThenReturnsResource`,
`GivenOverlappingStoredCompositeRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults`,
`GivenTokenDateTimeComposite_WhenDateMatches_ThenReturnsResource`,
`GivenOverlappingStoredCompositeDateRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults`,
`GivenTokenStringComposite_WhenStringPrefixMatches_ThenReturnsResource`,
`GivenReferenceTokenComposite_WhenComponentsInExpectedOrder_ThenReturnsResource`. Keep exactly one:
`GivenReferenceTokenComposite_WhenComponentsPassedInWrongOrder_ThenReturnsEmptyWithoutApplyingSpuriousFilters`
(it never reaches a `Collate`-using comparison, confirmed by tracing its code path — the guard clause
returns before any token/reference comparison runs).

- [ ] **Step 1: Delete the 7 affected tests from `CompositeSearchParameterQueryGeneratorTests.cs`**

Read the file in full first (it's short, one class). Delete the 7 test methods named above in their
entirety (each is a self-contained `[Fact]` method — remove each one completely, including its
attribute, from `[Fact]` through the closing `}`). Keep the class declaration, constructor, and the one
surviving test (`GivenReferenceTokenComposite_WhenComponentsPassedInWrongOrder_...`) exactly as they are.
Update the class's doc comment (currently describes "five supported composite shapes... locked down
around Task 4") to reflect its narrowed scope, e.g.: "Regression coverage for
`GenerateReferenceTokenQueryAsync`'s misordered-component guard clause. Collation-dependent composite
read-path coverage lives in `test/Ignixa.Api.E2ETests/Search/DataTypes/CompositeSearchTests.cs` — EF
Core's InMemory test provider cannot translate `EF.Functions.Collate`, which this class's read paths
now use (see `docs/superpowers/specs/2026-07-12-storage-convention-consolidation-design.md`)."

- [ ] **Step 2: Run the narrowed test class to confirm the surviving test still passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: PASS, 1/1 (only the surviving test remains).

- [ ] **Step 3: Check whether a composite-search E2E test file already exists**

Run: `find test/Ignixa.Api.E2ETests/Search -iname "*composite*"` (or equivalent directory listing) to
check for an existing `CompositeSearchTests.cs` or similar. If one exists, read it in full and extend it
following its existing patterns instead of creating a new file with Step 4's scaffold. If none exists,
proceed to Step 4 to create one from scratch, modeling its structure directly on
`test/Ignixa.Api.E2ETests/Search/DataTypes/TokenSearchTests.cs` (read that file in full first — it uses
`CapabilityDrivenTestBase`, `IClassFixture<TSomeFixture>`, `RequireSearchParameter(...)` capability
checks, and `Harness.SearchAsync(resourceType, query)`).

- [ ] **Step 4: Create/extend the E2E test file with characterization tests for all 4 bugs**

Each test needs a composite search parameter that actually exists in this codebase's FHIR search
parameter definitions with a Token-paired component — `Observation`'s `code-value-quantity` (Token|
Quantity) and `combo-code-value-concept` (Token|Token) are confirmed present via
`CompositeSearchParameterQueryGenerator.DetermineCompositeType`'s routing logic (read that method to
confirm current parameter-to-shape mappings before finalizing test data, in case they've changed).
Follow `TokenSearchTests.cs`'s exact fixture/capability-check/`Harness.SearchAsync` pattern for each:

1. **Composite token overflow test** (`code-value-quantity` or `combo-code-value-concept`, a token
   component value over 256 characters): create an `Observation` with a composite token code longer than
   256 characters (matching Task 3's write-side fix scope), search for it, confirm the resource is
   returned. Before this phase, this returned empty (write-side truncated at the wrong 128 threshold and
   read-side never checked overflow); confirm it now matches.
2. **Composite token case-insensitivity test**: create an `Observation` with a composite token code in
   one case (e.g. `"Final"`), search using a different case (e.g. `"FINAL"` or `"final"`), confirm the
   resource is returned (previously: composite token comparison was ordinal/case-sensitive, so this
   would not have matched).
3. **Composite string original-case preservation test** (`code-value-string`, Token|String — confirm
   this composite type exists via `DetermineCompositeType` before finalizing): create an `Observation`
   with a mixed-case string composite component (e.g. `"Smith"`), search with a matching-case prefix,
   confirm it's returned; if practical within this test infrastructure, also confirm the stored value's
   case is preserved (previously: stored as `"SMITH"`, destroying original case).
4. **Composite string overflow test**: create an `Observation` with a string composite component over
   256 characters, search for a prefix within the overflow range, confirm it now matches (previously:
   write-side split at the wrong 128 threshold, read-side never checked `TextOverflow2`).

Write complete, runnable test code for each — do not leave placeholder assertions. If the exact
composite search parameter codes/component ordering differ from what's assumed above once you check
`DetermineCompositeType`, adapt the test data accordingly (this is exactly the kind of detail that must
be verified against current source, not assumed from this plan).

- [ ] **Step 5: Run the new E2E tests**

Run: `dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~CompositeSearchTests"`
Expected: PASS, all new tests. (This requires a real SQL Server — confirm the E2E test infrastructure's
existing setup/connection mechanism via `IgnixaApiFixture` handles this the same way every other E2E
test in this project already does; do not invent new infrastructure.)

- [ ] **Step 6: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs test/Ignixa.Api.E2ETests/Search/DataTypes/CompositeSearchTests.cs
git commit -m "test(sql): re-home collation-dependent composite tests to E2E, add characterization coverage for all 4 fixed bugs"
```

---

### Task 8: Delete dead `QuantityCodeRowGenerator`

**Files:**
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/QuantityCodeRowGenerator.cs`

**Interfaces:** None — this file has zero references anywhere in the repo outside its own declaration
(confirmed via repo-wide grep during design review), and no corresponding test file exists.

- [ ] **Step 1: Confirm no references exist**

Run: `grep -rn "QuantityCodeRowGenerator" --include="*.cs" --include="*.csproj" .`
Expected: only the file's own declaration (`class QuantityCodeRowGenerator`) — no instantiation, no
registration in `SqlMergeRepository.cs`, no test file. If anything else appears, STOP and report
BLOCKED rather than deleting — this plan's citation was verified once during design review but must be
re-confirmed at implementation time in case something changed.

- [ ] **Step 2: Delete the file**

```bash
git rm src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/QuantityCodeRowGenerator.cs
```

- [ ] **Step 3: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s) — a compile error here would mean Step 1's grep missed a reference;
if that happens, restore the file (`git checkout HEAD -- <path>`) and report BLOCKED instead of forcing
the deletion.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(sql): delete dead QuantityCodeRowGenerator (unreferenced, placeholder hash-based IDs)"
```

---

### Task 9: Retag two TODO comments

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/ReferenceSearchParameterRowGenerator.cs:90`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/ResourceRowGenerator.cs:114`

**Interfaces:** None — comment-only change, no behavior change, no new tests needed.

- [ ] **Step 1: Retag `ReferenceSearchParameterRowGenerator.cs`'s TODO**

Read the file to confirm the comment is still at (or near) line 90 and reads `// TODO Phase 3: Extract
version if available`. Replace it with:

```csharp
// TODO(versioned-references): Extract version if available
```

- [ ] **Step 2: Retag `ResourceRowGenerator.cs`'s TODO**

Read the file to confirm the comment is still at (or near) line 114 and reads `// SearchParamHash: TODO
Phase 2`. Replace it with:

```csharp
// SearchParamHash: TODO(reindex)
```

- [ ] **Step 3: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s) (comment-only change, should never fail, but confirm anyway).

- [ ] **Step 4: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/ReferenceSearchParameterRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/ResourceRowGenerator.cs
git commit -m "docs(sql): retag stale phase-numbered TODOs to avoid colliding with this cleanup's own Phase 3"
```

---

### Task 10: End-to-end regression pass and final verification

**Files:** none (verification only)

**Interfaces:** none.

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Full solution test run with named failures captured**

Run: `dotnet test All.sln`
Expected: capture the full list of failing test names (not just a count). The `Ignixa.DataLayer.LegacySqlEF.Tests`
project is not part of `All.sln` (confirmed during Phase 2 — verify this is still true) and must be run
separately in Step 3. For the projects that are in `All.sln`, confirm failures match exactly the
pre-existing baseline (SqlOnFhir conformance drift ×2 per TFM) plus any composite-related test names
from `Ignixa.Application.Tests` documented as pre-existing in prior phases' plans (`CompositeSearchIndexingDiagnosticTests`
should NOT be among them — that was fixed in Phase 2's final review-fix pass; if it reappears as
failing, STOP and report BLOCKED, since that would indicate an unexpected regression). Document the
exact list.

- [ ] **Step 3: Run the DataLayer test project separately**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: the 5 pre-existing failures (`ChainedExpressionProcessorTests` ×4, `IterateProcessorTests` ×1)
and nothing else — specifically, no composite-related failures (they were re-homed to E2E in Task 7, not
left broken).

- [ ] **Step 4: Run the E2E test project**

Run: `dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~CompositeSearchTests|FullyQualifiedName~TokenSearchTests"`
Expected: all pass, including Task 7's new composite characterization tests and the pre-existing
`TokenSearchTests` (confirming this phase's single-token changes — Task 1's constant extraction — didn't
regress anything there).

- [ ] **Step 5: Verify the four bug fixes are each covered by a passing test**

Cross-check against the design spec's Testing strategy: composite token overflow (Task 7 Step 4 test 1),
composite token case-insensitivity (test 2), composite string original-case preservation (test 3),
composite string overflow (test 4). Confirm each has a named, currently-passing test — if any is
missing or was silently dropped during the plan's execution, that's a gap to close before finishing,
not something to note and move past.

- [ ] **Step 6: Update the design spec if any deviation occurred**

If any task's implementation deviated from what `docs/superpowers/specs/2026-07-12-storage-convention-consolidation-design.md`
describes (e.g., a composite search parameter code used in Task 7's E2E tests turned out different from
what `DetermineCompositeType` was assumed to route, or a column index differed from what Task 3/5 cited),
update the spec to match reality.

- [ ] **Step 7: Record this phase's completion in the cross-plan tracking location**

Follow the established pattern from Phase 0/1's plan doc
(`docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md`'s "Post-Plan" section, which
already has dated entries for the Phase 2 prerequisite investigation and Phase 2's own implementation
findings) — add a new dated entry recording: this phase's scope (Step 1 only), the four bugs fixed, and
explicitly note that Step 2 (the full declarative storage descriptor — the complete answer to audit
finding 4) remains a separate, not-yet-scoped future investigation, now that Step 1's real
implementation cost is known (record roughly how many files/tasks it took, as data for that future
scoping decision, matching how Phase 1's real cost informed Phase 2's own scoping).

- [ ] **Step 8: Final commit if Steps 6-7 produced changes**

```bash
git add -A
git commit -m "docs(sql): reconcile Phase 3 Step 1 spec/plan with final implementation, record completion"
```

If Steps 6-7 produced no file changes, skip this commit — there's nothing to commit.

---

## Post-Plan

(To be filled in during execution: Fable whole-branch review findings and their resolution, final HEAD
commit SHA, confirmation the four bug fixes are each covered by a passing test, and the Phase 0/1 plan
doc's Post-Plan section updated per Task 10 Step 7.)
