# Search Indexer IsMin/IsMax Flags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port fhir-server's `ResourceWrapperFactory.ExtractMinAndMaxValues` algorithm into Ignixa's `ElementSearchIndexer.Extract`, so `StringSearchValue`/`DateTimeSearchValue`'s existing (but currently always-`false`) `IsMin`/`IsMax` flags are actually set at indexing time. This is a prerequisite for the fhir-to-sql-compiler's Phase 8 part 2 (sort) design, which targets a `WHERE IsMin = 1`/`IsMax = 1` SQL shape directly rather than a slower query-time aggregation fallback — see `docs/superpowers/specs/2026-07-18-fhir-to-sql-compiler-sort-design.md` §1.4/§3.1/§3.3 for the full context.

**Architecture:** A direct, faithful port of fhir-server's real algorithm (verified against `C:\src\fhir-server\src\Microsoft.Health.Fhir.Core\Features\Persistence\ResourceWrapperFactory.cs`'s `ExtractMinAndMaxValues` method): group a resource's extracted `SearchIndexEntry` values by `SearchParameter.Url`, track the running min/max per group via the already-ported `ISupportSortSearchValue.CompareTo(other, ComparisonRange.Min/Max)`, skip parameters with `SortStatus == SortParameterStatus.Disabled`, then set `IsMin`/`IsMax = true` on the winning instances. Ignixa has no `ResourceWrapperFactory`-equivalent wrapping class (fhir-server's own home for this step) — `searchIndexer.Extract(...)` is called directly from `CreateOrUpdateResourceHandler.cs`, at two call sites, with no single funnel point downstream. The natural home is therefore inside `ElementSearchIndexer.Extract` itself (Core-tier, `Ignixa.Search`), as the final step before returning — this keeps the fix in one place, matches the "derived flags computed by the same code that extracts the raw values" cohesion argument, and needs no new abstraction. **No backfill**: Ignixa has no production data yet (user-confirmed) — this is a pure forward fix.

**Tech Stack:** C# / .NET 9+, xUnit + Shouldly, `Ignixa.Search` (Core-tier).

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework) are out of scope, per every prior increment on this branch.
- The ported algorithm's exact shape (transcribe from the real fhir-server source, do not paraphrase — the file is `C:\src\fhir-server\src\Microsoft.Health.Fhir.Core\Features\Persistence\ResourceWrapperFactory.cs`, method `ExtractMinAndMaxValues`, confirmed real during this plan's own research):
  ```csharp
  private static void MarkMinMaxValues(IReadOnlyCollection<SearchIndexEntry> searchIndices)
  {
      var minValues = new Dictionary<Uri, ISupportSortSearchValue>();
      var maxValues = new Dictionary<Uri, ISupportSortSearchValue>();

      foreach (SearchIndexEntry currentEntry in searchIndices)
      {
          if (currentEntry.Value is not ISupportSortSearchValue currentValue)
          {
              continue;
          }

          if (currentEntry.SearchParameter.SortStatus == SortParameterStatus.Disabled)
          {
              continue;
          }

          if (minValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMinValue))
          {
              if (currentValue.CompareTo(existingMinValue, ComparisonRange.Min) < 0)
              {
                  minValues[currentEntry.SearchParameter.Url] = currentValue;
              }
          }
          else
          {
              minValues.Add(currentEntry.SearchParameter.Url, currentValue);
          }

          if (maxValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMaxValue))
          {
              if (currentValue.CompareTo(existingMaxValue, ComparisonRange.Max) > 0)
              {
                  maxValues[currentEntry.SearchParameter.Url] = currentValue;
              }
          }
          else
          {
              maxValues.Add(currentEntry.SearchParameter.Url, currentValue);
          }
      }

      foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in minValues)
      {
          kvp.Value.IsMin = true;
      }

      foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in maxValues)
      {
          kvp.Value.IsMax = true;
      }
  }
  ```
  Named `MarkMinMaxValues` (not `ExtractMinAndMaxValues` — it mutates in place, it doesn't extract/return anything, so the name should say what it does). `IsMin`/`IsMax` are settable properties (`{ get; set; }`) on `ISupportSortSearchValue` (`src/Core/Ignixa.Search/Indexing/SearchValues/ISupportSortSearchValue.cs`) — mutating a `SearchIndexEntry.Value` instance in place, exactly as fhir-server does, is the correct, already-established mutability contract for this type (not a design decision this plan is introducing).
- Composite search parameters (`CompositeIndexSearchValue`) are never `ISupportSortSearchValue` (confirmed: only `StringSearchValue`/`DateTimeSearchValue` implement it) — the `is not ISupportSortSearchValue` guard already skips them correctly; no separate composite-handling logic is needed.
- A single-value parameter (only one `SearchIndexEntry` for that `SearchParameter.Url`) gets **both** `IsMin = true` and `IsMax = true` set on the same instance — this is fhir-server's own behavior (the same instance is the sole entry in both dictionaries) and is correct: a query filtering `WHERE IsMin = 1` (ascending) or `WHERE IsMax = 1` (descending) must return that resource's only value either way.
- Only add the `MarkMinMaxValues` call inside `ElementSearchIndexer.Extract`, right before `return entries;` — do not touch `ProcessCompositeSearchParameter`/`ProcessNonCompositeSearchParameter`/either call site in `CreateOrUpdateResourceHandler.cs`.

---

### Task 1: Port `MarkMinMaxValues` into `ElementSearchIndexer.Extract`

**Files:**
- Modify: `src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs`
- Test: `test/Ignixa.Application.Tests/Search/Indexing/SearchIndexerMinMaxTests.cs` (new file, matching the existing convention of `Ignixa.Application.Tests/Search/Indexing/*` for `ElementSearchIndexer`-level tests — e.g. `IdentifierOfTypeIndexingTests.cs`, `CompositeSearchIndexingDiagnosticTests.cs` — read one of those first to match its exact fixture/constructor style, both use `SearchIndexerFactory.CreateInstance(schemaProvider, loggerFactory, searchParamManager)` to build a real `ISearchIndexer`)

**Interfaces:**
- Consumes: nothing new — `ISupportSortSearchValue`, `ComparisonRange`, `SortParameterStatus` all already exist.
- Produces: `ElementSearchIndexer.Extract(IElement)`'s existing public contract is unchanged in shape (still returns `IReadOnlyCollection<SearchIndexEntry>`) — only the `IsMin`/`IsMax` values on the returned `StringSearchValue`/`DateTimeSearchValue` instances change, from always-`false` to correctly marked. No consumer of this session's fhir-to-sql-compiler work depends on this task directly (Phase 8 part 2 is a separate plan) — this task's only consumer is that plan's own eventual `Emit` shape, which this fix makes correct to target.

- [ ] **Step 1: Add `using` statements**

In `src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs`, confirm `Ignixa.Search.Indexing.SearchValues` is already imported (it is, for `SearchIndexEntry`/existing search value types) — `ISupportSortSearchValue`, `ComparisonRange`, and `SortParameterStatus` (the last from `Ignixa.Search.Models`, also already imported) all resolve without new `using` directives.

- [ ] **Step 2: Add `MarkMinMaxValues` and call it from `Extract`**

Change the end of `Extract` (currently):

```csharp
        return entries;
    }
```

to:

```csharp
        MarkMinMaxValues(entries);

        return entries;
    }
```

Add the method (place it directly after `Extract`, before `ProcessCompositeSearchParameter`):

```csharp
    /// <summary>
    /// A resource's search parameter can have multiple values (e.g. multiple HumanName entries for
    /// Patient.name). This marks which of those values is the min and which is the max for each
    /// distinct search parameter, so a compiled sort can seek directly against IsMin/IsMax-flagged
    /// rows instead of aggregating at query time. Ported from microsoft/fhir-server's
    /// ResourceWrapperFactory.ExtractMinAndMaxValues -- see
    /// docs/superpowers/plans/2026-07-18-search-indexer-min-max-flags.md's Global Constraints for the
    /// exact source method this mirrors.
    /// </summary>
    private static void MarkMinMaxValues(IReadOnlyCollection<SearchIndexEntry> searchIndices)
    {
        var minValues = new Dictionary<Uri, ISupportSortSearchValue>();
        var maxValues = new Dictionary<Uri, ISupportSortSearchValue>();

        foreach (SearchIndexEntry currentEntry in searchIndices)
        {
            if (currentEntry.Value is not ISupportSortSearchValue currentValue)
            {
                continue;
            }

            if (currentEntry.SearchParameter.SortStatus == SortParameterStatus.Disabled)
            {
                continue;
            }

            if (minValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMinValue))
            {
                if (currentValue.CompareTo(existingMinValue, ComparisonRange.Min) < 0)
                {
                    minValues[currentEntry.SearchParameter.Url] = currentValue;
                }
            }
            else
            {
                minValues.Add(currentEntry.SearchParameter.Url, currentValue);
            }

            if (maxValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMaxValue))
            {
                if (currentValue.CompareTo(existingMaxValue, ComparisonRange.Max) > 0)
                {
                    maxValues[currentEntry.SearchParameter.Url] = currentValue;
                }
            }
            else
            {
                maxValues.Add(currentEntry.SearchParameter.Url, currentValue);
            }
        }

        foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in minValues)
        {
            kvp.Value.IsMin = true;
        }

        foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in maxValues)
        {
            kvp.Value.IsMax = true;
        }
    }
```

- [ ] **Step 3: Write the failing tests**

First, read `test/Ignixa.Application.Tests/Search/Indexing/IdentifierOfTypeIndexingTests.cs` in full to confirm the exact constructor/fixture pattern (schema provider, `SearchParameterDefinitionManager`, `SearchIndexerFactory.CreateInstance`) and match it exactly — do not invent a different fixture shape.

Create `test/Ignixa.Application.Tests/Search/Indexing/SearchIndexerMinMaxTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Shouldly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Definition;
using Ignixa.Specification.Generated;
using Ignixa.Serialization.SourceNodes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.Application.Tests.Search.Indexing;

public class SearchIndexerMinMaxTests
{
    private readonly R4CoreSchemaProvider _schemaProvider;
    private readonly ISearchIndexer _indexer;

    public SearchIndexerMinMaxTests()
    {
        _schemaProvider = new R4CoreSchemaProvider();
        var loggerFactory = NullLoggerFactory.Instance;

        var searchParamManager = new SearchParameterDefinitionManager(
            _schemaProvider,
            new NullLogger<SearchParameterDefinitionManager>());

        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            loggerFactory,
            searchParamManager);
    }

    [Fact]
    public void GivenAPatientWithTwoDistinctNames_WhenIndexed_ThenExactlyOneNameValueIsMarkedMinAndOneIsMarkedMax()
    {
        // Arrange -- two distinct HumanName entries produce multiple "name" search values
        // (String type, multi-valued) -- exactly the shape MarkMinMaxValues exists to flag.
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithFamilyName("Zorro")
            .AddName("Adams", "Anna")
            .Build();

        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert
        var nameValues = indices
            .Where(i => i.SearchParameter.Code == "name")
            .Select(i => i.Value)
            .OfType<StringSearchValue>()
            .ToList();

        nameValues.Count.ShouldBeGreaterThan(1); // multiple values extracted for a multi-name patient

        var minMarked = nameValues.Where(v => v.IsMin).ToList();
        var maxMarked = nameValues.Where(v => v.IsMax).ToList();

        minMarked.Count.ShouldBe(1);
        maxMarked.Count.ShouldBe(1);

        var expectedMin = nameValues.MinBy(v => v.String, StringComparer.Ordinal);
        var expectedMax = nameValues.MaxBy(v => v.String, StringComparer.Ordinal);

        minMarked[0].String.ShouldBe(expectedMin!.String);
        maxMarked[0].String.ShouldBe(expectedMax!.String);
    }

    [Fact]
    public void GivenAPatientWithOneFamilyName_WhenIndexed_ThenTheSoleFamilyNameValueIsMarkedBothMinAndMax()
    {
        // Arrange -- a single value for a search parameter is trivially both its own min and max
        // (fhir-server's own documented behavior for this case).
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithFamilyName("OnlyFamily")
            .Build();

        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert
        var familyValues = indices
            .Where(i => i.SearchParameter.Code == "family")
            .Select(i => i.Value)
            .OfType<StringSearchValue>()
            .ToList();

        familyValues.Count.ShouldBe(1);
        familyValues[0].IsMin.ShouldBeTrue();
        familyValues[0].IsMax.ShouldBeTrue();
    }
}
```

If Step 3's first test's assumption about `name` producing multiple distinct String values from two `AddName`-added `HumanName`s does not hold when actually run (e.g. if the "name" search parameter's FHIRPath expression produces a different value set than expected), do not force the test to pass by weakening its assertions below the "exactly one min, exactly one max, matching the real lexicographic extremes" bar — instead inspect the actual `indices` returned (add a temporary debug assertion or breakpoint to see the real `nameValues` list) and adjust the test's `Arrange`/resource construction so it produces a genuinely multi-valued case, since that is what this test exists to prove.

- [ ] **Step 4: Run the tests to verify they fail without Step 2, then pass with it**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchIndexerMinMaxTests"`
Expected (before Step 2's code change): both tests FAIL (`IsMin`/`IsMax` both `false`).
Expected (after Step 2's code change): both tests PASS.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj`
Expected: PASS, all tests including the 2 new ones, zero regressions (this change only sets two previously-always-`false` boolean flags; no existing test should have asserted `IsMin`/`IsMax` was `false`, but confirm by running the full suite, not just the new tests).

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs test/Ignixa.Application.Tests/Search/Indexing/SearchIndexerMinMaxTests.cs
git commit -m "feat(search): mark IsMin/IsMax on multi-valued search index entries"
```

---

### Task 2: Full regression + report

**Files:** none (verification only).

- [ ] **Step 1: Full solution build and test**

Run: `dotnet build All.sln` → expect 0 warnings, 0 errors.
Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → expect all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 2: Report and ask before merging/pushing**

Summarize the change (one method ported, one call site added, two new tests) and confirm this unblocks the fhir-to-sql-compiler's Phase 8 part 2 (sort) design's `WHERE IsMin = 1`/`IsMax = 1` target shape, matching `docs/superpowers/specs/2026-07-18-fhir-to-sql-compiler-sort-design.md`. Ask before merging into whatever base branch this worktree branched from, and again before pushing — matching this session's established pattern for every prior change.
