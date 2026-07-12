# SQL Data Layer Cleanup — Phase 0 & Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute Phase 0 (mechanical dedup) and Phase 1 (visitor-contract adoption) of `docs/features/sql-datalayer-architecture/investigations/staged-query-compiler.md` against `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework`, closing the test-coverage gap first.

**Architecture:** No schema change, no behavior change to production search semantics. Tasks 0a/0b restore a compiling test baseline. Tasks 1-2 add characterization tests locking down current behavior of the two largest, least-tested files (`SearchParameterQueryGenerator.cs`, `CompositeSearchParameterQueryGenerator.cs`) before they're touched. Tasks 3-4 extract duplicated logic into shared helpers with zero behavior change. Task 5 replaces `SearchExpressionQueryBuilder`'s ad-hoc `expression switch` dispatch with Core's existing `IExpressionVisitor<TContext, TOutput>` contract, again with zero behavior change — same generated queries, different dispatch mechanism.

**Tech Stack:** C# 13, .NET, xUnit, Shouldly, NSubstitute, EF Core (InMemory provider for tests), EF Core LINQ-to-SQL translation for production queries.

## Global Constraints

- Nullable reference types enabled; no `#nullable disable`.
- `CancellationToken` parameters are always named `cancellationToken`, last parameter, propagated end-to-end.
- No behavior change to generated SQL/query results in Tasks 3, 4, 5 — these are pure refactors. Any test whose expected result would need to change is a signal the refactor broke something, not that the test was wrong.
- Test naming: `GivenContext_WhenAction_ThenResult` (xUnit `[Fact]`/`[Theory]`, Shouldly assertions — `ShouldBe`, `ShouldHaveSingleItem`, `ShouldBeEmpty`, per existing tests in `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/`).
- Tests inherit `TestBase` (`test/Ignixa.DataLayer.SqlEntityFramework.Tests/TestBase.cs`) for the EF Core InMemory `FhirDbContext`, `SearchIndexReferenceDataCache`, and seeded reference data (`ResourceTypeId` 1=Patient, 2=Organization, 3=Observation, 4=Practitioner, 5=Encounter; `SearchParamId` 1-6 pre-seeded — see `TestBase.SeedReferenceData()`). Add new `SearchParamEntity`/`ResourceTypeEntity` rows in test constructors when a task needs IDs beyond the seeded set — do not modify `TestBase` itself without checking every existing test that depends on its current seed data.
- One type per file (CLAUDE.md); no `#region` blocks.
- Treat warnings as errors — a task is not done if `dotnet build` reports new warnings.
- No commits without this plan's task steps explicitly calling for one (each task's last step is `git add` + `git commit`; do not commit outside that step).

---

## Task 0a: Fix compile errors in the chain/include test files

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/IncludeProcessorTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/IterateProcessorTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/RevIncludeProcessorTests.cs`

**Context:** These three files currently fail to compile — 64 of the errors below are theirs. This is pre-existing breakage on `main` (traced to commit `7bf8213f`, "Removes FluentAssertions. Adds Shouldly"), not something introduced by this plan. It blocks `dotnet test` for the *entire* `Ignixa.DataLayer.LegacySqlEF.Tests` project, including any new tests Tasks 1-2 add, so it must be fixed first.

**Investigation required — these are the confirmed root causes, apply them uniformly across all three files:**

1. **Missing `using NSubstitute;`** — causes `error CS0103: The name 'Arg' does not exist` and `error CS0103: The name 'Substitute' does not exist`. Add the using directive to each file's using block (see `ChainedExpressionProcessorTests.cs` for the correct pattern — it does not hit this error because it doesn't call `Substitute.For<...>` directly with unresolved usings).
2. **`ISourceNode` no longer exists in this codebase** (confirmed — zero matches anywhere under `src/` as of this plan). It was removed by the "Hide MutableNode from SDK surface" refactor. Every place these test files construct a fake resource body via `ISourceNode` must instead use `ResourceJsonNode` (`Ignixa.Serialization.SourceNodes.ResourceJsonNode` — add `using Ignixa.Serialization.SourceNodes;`). Read `src/Application/Ignixa.Domain/Models/ResourceWrapper.cs` — its `Resource` property is typed `ResourceJsonNode`, confirming this is the current expected type. Check how `TestBase.CreateResource` in the same test project builds a minimal resource body (it compresses a raw JSON string via `GzipResourceCompressor`, not via a source-node type) for a working reference pattern, but note `TestBase.CreateResource` builds a `ResourceEntity` (EF entity) not a `ResourceWrapper` (domain model needed here) — read `ResourceJsonNode.cs` (`src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs`) to find its actual construction API (e.g. a static `Parse`/`Create` factory) before writing replacement code.
3. **`error CS0246: 'ResourceKey' could not be found`** — `ResourceKey` exists at `src/Core/Ignixa.Abstractions/ResourceKey.cs`. Add `using Ignixa.Abstractions;`. Note there is a second, unrelated `ResourceKey` type at `src/Application/Ignixa.Application/Features/Experimental/GraphQl/Models/ResourceKey.cs` — do not use that one; if both usings would be ambiguous in a given file, use the fully-qualified `Ignixa.Abstractions.ResourceKey` at the call site instead of a bare using.
4. **`error CS7036: ... required parameter 'Method' of 'ResourceRequest.ResourceRequest(...)'`** — `ResourceRequest` (`src/Application/Ignixa.Domain/Models/ResourceRequest.cs`) is a positional record: `ResourceRequest(string Method, string Url, string? IfMatch = null, string? IfNoneExist = null, string? IfModifiedSince = null)`. Every construction site missing `Method` needs it added — use `"GET"` for read/search-context fake requests (check surrounding test intent; use `"PUT"` only if the test is specifically about a write scenario).
5. **`error CS0246: 'SearchParameterInfo' could not be found` / `error CS0103: 'SearchParamType' does not exist`** — add `using Ignixa.Search.Models;` (for `SearchParameterInfo`) and `using Ignixa.Specification.ValueSets.Normative;` (for `SearchParamType`) — this exact pair of usings is already present and working in `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`, use it as the reference.
6. **`error CS1503: cannot convert from 'List<ResourceWrapper>' to 'IReadOnlyList<(string ResourceType, string ResourceId)>'`** (and the similar `IReadOnlyList<SearchEntryResult>` variant in `IterateProcessorTests.cs`) — a call site is passing a list of the wrong shape to a processor method. Read the actual method signature being called at each reported line (`IncludeProcessor.ProcessIncludesAsync` returns/expects specific shapes — read `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/IncludeProcessor.cs`, `IterateProcessor.cs`, and `RevIncludeProcessor.cs` directly for their current parameter and return types) and fix the test's constructed value to match, preserving the test's original intent (what scenario it's arranging), not just satisfying the compiler.

**Do not** weaken any assertion or delete a test to make the build pass — if a test's *intent* no longer maps to any current production API at all (distinct from a mechanical signature drift), stop and report `NEEDS_CONTEXT` rather than guessing.

**Report file:** `docs/superpowers/plans/task-0a-report.md`

- [ ] **Step 1: Read the three target files in full, and the four production files they construct/call** (`ResourceRequest.cs`, `ResourceWrapper.cs`, `ResourceJsonNode.cs`, `ResourceKey.cs`, `IncludeProcessor.cs`, `IterateProcessor.cs`, `RevIncludeProcessor.cs`) to confirm current signatures before editing.

- [ ] **Step 2: Fix `IncludeProcessorTests.cs`** applying the six root causes above everywhere they occur in this file.

- [ ] **Step 3: Build and confirm this file's errors are gone**

Run: `dotnet build "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: no `error` lines referencing `IncludeProcessorTests.cs` (other files may still show errors until Steps 4-5 are done).

- [ ] **Step 4: Fix `IterateProcessorTests.cs`** the same way.

- [ ] **Step 5: Fix `RevIncludeProcessorTests.cs`** the same way.

- [ ] **Step 6: Full build and full test run**

Run: `dotnet build "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: 0 errors from these three files (Task 0b's two files may still error — that's fine, not this task's scope).

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --filter "FullyQualifiedName~IncludeProcessorTests|FullyQualifiedName~IterateProcessorTests|FullyQualifiedName~RevIncludeProcessorTests" --nologo -v quiet` (only runs if Task 0b's files also compile by this point — if not, this step will still fail to build the whole project; in that case just confirm via `dotnet build` above and note in the report that full `dotnet test` awaits Task 0b)

- [ ] **Step 7: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/IncludeProcessorTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/IterateProcessorTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/RevIncludeProcessorTests.cs
git commit -m "fix(tests): restore compile-clean IncludeProcessor/IterateProcessor/RevIncludeProcessor test files"
```

---

## Task 0b: Fix compile errors in the remaining seven broken test/infrastructure files

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/TestBase.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/SqlMergeRepositoryTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/SearchIndexReferenceDataCacheTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/HybridTerminologyServiceTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ChainedExpressionProcessorTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ReferenceSearchParameterTests.cs`
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/NotReferencedSearchParameterTests.cs`

**Context:** This task's scope grew from its original 2 files after Task 0a's implementer discovered its fix pattern (missing `NullLogger<T>.Instance` usage, `IFhirRepository.GetAsync` return-type drift, `SearchParameterInfo` constructor drift) recurs elsewhere, and the controller independently confirmed via a fresh full-project build that these 7 files — including `TestBase.cs`, the shared base class every test in this project inherits from — are broken, not just the originally-scoped 2. This is still pre-existing breakage unrelated to the SQL cleanup plan itself; fixing it is a hard prerequisite because `TestBase.cs` being broken means **no test in the entire project can currently run**.

**Fix `TestBase.cs` first** — every other file in this task depends on it compiling.

**Confirmed root causes:**

1. **`TestBase.cs:75` `error CS7036: ... required parameter 'memoryStreamManager' of 'GzipResourceCompressor.GzipResourceCompressor(RecyclableMemoryStreamManager)'`, and `TestBase.cs:78` `error CS1061: 'GzipResourceCompressor' does not contain a definition for 'CompressBytes'`.** Confirmed by reading `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Compression/GzipResourceCompressor.cs` directly: its constructor requires a `RecyclableMemoryStreamManager` (construct with `new RecyclableMemoryStreamManager()` — this exact parameterless-construction pattern is already used elsewhere in this codebase, e.g. `src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs:53` and `src/DataLayer/Ignixa.DataLayer.BlobStorage/BlobStorageExportStreamWriter.cs:160`), and its API is now `SerializeAndCompress(ResourceJsonNode node) : byte[]` — not `CompressBytes(byte[])`. `ResourceJsonNode` has a static factory `ResourceJsonNode.Parse(string json)` (`src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs:183`). Rewrite `TestBase.CreateResource`'s body to: construct the compressor once (as a field, or inline — match the constructor-injection style already used for `Cache`/`LoggerFactory` in this class), replace the raw `Encoding.UTF8.GetBytes(minimalJson)` + `compressor.CompressBytes(jsonBytes)` pair with `compressor.SerializeAndCompress(ResourceJsonNode.Parse(minimalJson))`, and assign the result to `RawResource` as before. Add `using Ignixa.Serialization.SourceNodes;` and `using Microsoft.IO;` as needed.
2. **`NullLoggerFactory.CreateLogger<T>()` used with a type argument (`error CS0308`)** — appears in `NotReferencedSearchParameterTests.cs:28,32` and `ReferenceSearchParameterTests.cs:29`. `Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.CreateLogger` is non-generic (`CreateLogger(string categoryName)`); the generic logger these tests actually want is `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance`. Replace every `NullLoggerFactory.Instance.CreateLogger<T>()` (or similar) call with `NullLogger<T>.Instance`. Confirm via `TestBase.cs`'s own `LoggerFactory` property whether these test files should instead be using the inherited `LoggerFactory.CreateLogger<T>()` pattern already established in `ChainedExpressionProcessorTests.cs` (`LoggerFactory.CreateLogger<ChainedExpressionProcessor>()`) — prefer matching that existing working pattern over introducing a second logging convention, unless the specific test constructs a component outside `TestBase`'s scope.
3. **`ChainedExpressionProcessorTests.cs`: `SearchParamType` not found (`error CS0103`, lines 130, 133, 167), `SearchParameterInfo.TargetResourceTypes` read-only (`error CS0200`, line 135), `ProcessChainAsync` has no parameter named `resourceTypeId` (`error CS1739`, lines 106, 146), `StringExpression` missing required `value` parameter (`error CS7036`, lines 131, 168).** Four distinct fixes, same pattern as Task 0a and this task's root causes 1-2 — apply all of them:
   - Add `using Ignixa.Specification.ValueSets.Normative;` for `SearchParamType`.
   - `SearchParameterInfo` (`src/Core/Ignixa.Search/Models/SearchParameterInfo.cs:21-43`) has one full constructor: `SearchParameterInfo(string name, string code, SearchParamType searchParamType, Uri url = null, IReadOnlyList<SearchParameterComponentInfo> components = null, string expression = null, IReadOnlyList<string> targetResourceTypes = null, IReadOnlyList<string> baseResourceTypes = null, string description = null)`. Replace every `new SearchParameterInfo(code, type) { TargetResourceTypes = [...] }` object-initializer pattern with `new SearchParameterInfo(name, code, type, targetResourceTypes: [...])` using the named `targetResourceTypes:` parameter — `TargetResourceTypes` can no longer be set via object initializer.
   - `ChainedExpressionProcessor.ProcessChainAsync` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ChainedExpressionProcessor.cs:50-53`) has signature `(short? sourceResourceTypeId, ChainedExpression chainedExpression, CancellationToken ct)` — rename every `resourceTypeId:` named-argument call site to `sourceResourceTypeId:`.
   - `StringExpression` (`src/Core/Ignixa.Search/Expressions/StringExpression.cs:23`) has exactly one constructor: `(StringOperator stringOperator, FieldName fieldName, int? componentIndex, string value, bool ignoreCase)` — 5 args. Replace every 3-arg call like `new StringExpression(StringOperator.Equals, "Acme", false)` with the full 5-arg form, e.g. `new StringExpression(StringOperator.Equals, FieldName.String, null, "Acme", false)` — pick the `FieldName` value that matches what the test is actually asserting about (string-type search parameters use `FieldName.String`; check the surrounding test's search parameter type before assuming).
4. **`ReferenceSearchParameterTests.cs`: same `StringExpression`/`NullLoggerFactory` issues as above, plus `SearchParameterQueryGenerator` constructor missing required `logger` argument (`error CS7036`, line 27), plus `SearchParameterQueryGenerator.ProcessExpressionAsync` does not exist / is not accessible (`error CS1061`, lines 71, 149, 198, 245).** `SearchParameterQueryGenerator`'s real constructor is `(FhirDbContext context, SearchIndexReferenceDataCache cache, ILogger<SearchParameterQueryGenerator> logger, CompositeSearchParameterQueryGenerator compositeQueryGenerator)` (confirmed in Task 1's brief) — fix the construction call to supply all four arguments, including a `CompositeSearchParameterQueryGenerator` instance (see Task 1's brief Step 1 for the exact pattern already used successfully). `ProcessExpressionAsync` is `private` on `SearchParameterQueryGenerator` (confirmed by reading the full 2113-line file) — it was never meant to be called directly from a test. Rewrite each of the 4 call sites to go through the public `GenerateQueryAsync(short? resourceTypeId, SearchParameterExpression expression, CancellationToken cancellationToken)` entry point instead, wrapping the expression under test in a `SearchParameterExpression` the way `SearchParameterQueryGeneratorResourceLevelTests.cs` (Task 1, once it exists — read that file if Task 1 has already landed by the time you do this task) or `ChainedExpressionProcessorTests.cs` does, preserving each test's original intent (what reference-search scenario it exercises) rather than just making it compile.
5. **`SearchIndexReferenceDataCacheTests.cs`: six `error CS1929` occurrences (lines 62, 63, 77, 78, 91, 104, 176) where Shouldly's `ShouldBe`/`ShouldBeGreaterThan` don't resolve against a `short` receiver, plus one `error CS0119` (line 196) where `.Count` is used as a property but only the `Enumerable.Count()` method applies.** For each `ShouldBe`/`ShouldBeGreaterThan` failure, read the actual field type being asserted (likely a `short` where the assertion was originally written against an `int` or enumerable) and either cast the actual/expected value to a type Shouldly's installed overload set covers (e.g. `((int)actual).ShouldBe(expected)`) or use a `short`-typed expected literal so C#'s overload resolution matches — pick whichever preserves the assertion's original numeric comparison intent, don't just cast to silence the compiler if it changes what's being compared. For the `.Count` vs `.Count()` case at line 196, determine from context whether the receiver is an array/`ICollection` (has a `.Count` property) or a plain `IEnumerable<T>` (needs `.Count()` as a method call) and use the correct syntax.
6. **`HybridTerminologyServiceTests.cs`: `ITerminologyService.GetImportStatusAsync` does not exist (`error CS1061`, lines 26, 68), `IssueSeverity` not found (`error CS0103`, lines 37, 79), and a direct cast/conversion from `ITerminologyService` to the concrete `SqlTerminologyService` fails (`error CS1503`, lines 39, 81).** This file tests a subsystem (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/`) that has changed more substantially than a simple rename — read `ITerminologyService`'s current interface (`Ignixa.Validation.Abstractions.ITerminologyService`) and `SqlTerminologyService`'s current public surface in full before deciding whether `GetImportStatusAsync` moved elsewhere (check `SqlCodeSystemImporter.cs` and `HybridTerminologyService.cs` in the same folder) or was genuinely removed. `IssueSeverity` is likely `Hl7.Fhir`/`Ignixa`-namespaced validation-issue severity that moved namespace — grep the codebase for its current namespace. If, after this investigation, a specific test's scenario no longer maps to any current capability, delete that test method (not the whole file) and note it in your report rather than fabricating a replacement assertion. This file may need the most judgment of the seven — take the time to actually understand the current terminology service shape rather than pattern-matching a quick fix.
7. **`CA1307` (2 occurrences in `SqlMergeRepositoryTests.cs`, lines ~170, ~212, `string.Contains(string)` without `StringComparison`)** — treated as a build error under this repo's warnings-as-errors policy. Fix both call sites by adding the comparison type, e.g. `.Contains(value, StringComparison.Ordinal)` — pick `Ordinal` unless the surrounding assertion is explicitly about culture/case-insensitive text matching, in which case use `StringComparison.OrdinalIgnoreCase`.
8. **`SqlMergeRepositoryTests.cs`: four missing methods on `SqlMergeRepository` — `GetResourceTypeIdMapAsync` (line 70), `GetSearchParameterIdMapAsync` (line 86), `GetSystemIdMapAsync` (line 102), `GetQuantityCodeIdMapAsync` (line 116) — plus `ResourceJsonNode` not found (`error CS0246`, lines 135, 136, 185), `ResourceWrapper`'s `resourceType:` named argument casing (`error CS1739`, lines 139, 149, 188 — same PascalCase fix as `TargetResourceTypes` above, `ResourceType:` not `resourceType:`), and an argument-order mismatch passing a `CancellationToken` where `IReadOnlyList<int>` is expected (`error CS1503`, line 168).** Confirmed by reading `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlMergeRepository.cs` directly: its only public methods today are `BeginTransactionAsync`, `MergeResourcesAsync`, `CommitTransactionAsync`, `PutTransactionHeartbeatAsync` — none of the four `GetXIdMapAsync` methods exist on this type. This strongly suggests ID-lookup responsibility moved to `SearchIndexReferenceDataCache`/`MultiTenantSearchIndexCache` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/`) — read both files in full to find the current equivalent of each missing method (they may not have 1:1 replacements; some may now happen implicitly inside `MergeResourcesAsync` itself). For each of the four, either rewrite the test against the real current API or, if no equivalent capability exists as a directly-testable method anymore, delete that specific test method (not the file) and note the deletion and why in your report. For `ResourceJsonNode`, add `using Ignixa.Serialization.SourceNodes;`. For the line-168 argument mismatch, read `MergeResourcesAsync`'s actual current parameter list and fix the call site's argument order/count to match.

**Report file:** `docs/superpowers/plans/task-0b-report.md`

- [ ] **Step 1: Read all seven target files in full**, plus every production file named above (`GzipResourceCompressor.cs`, `ResourceJsonNode.cs`, `SearchParameterInfo.cs`, `ChainedExpressionProcessor.cs`, `StringExpression.cs`, `SearchParameterQueryGenerator.cs`, `SqlMergeRepository.cs`, `SearchIndexReferenceDataCache.cs`, `MultiTenantSearchIndexCache.cs`, `ITerminologyService`'s current definition, `SqlTerminologyService.cs`, `HybridTerminologyService.cs`), to ground every fix in current production code before editing anything.

- [ ] **Step 2: Fix `TestBase.cs`** applying root cause 1. Build immediately after (`dotnet build "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`) and confirm `TestBase.cs` no longer appears in the error output — every subsequent step depends on this file compiling correctly.

- [ ] **Step 3: Fix `NotReferencedSearchParameterTests.cs`** applying root cause 2.

- [ ] **Step 4: Fix `ChainedExpressionProcessorTests.cs`** applying root causes 2, 3.

- [ ] **Step 5: Fix `ReferenceSearchParameterTests.cs`** applying root causes 2, 4.

- [ ] **Step 6: Fix `SearchIndexReferenceDataCacheTests.cs`** applying root cause 5.

- [ ] **Step 7: Fix `HybridTerminologyServiceTests.cs`** applying root cause 6 — this is the file most likely to need a genuine judgment call (rewrite vs. delete a test method); take your time here.

- [ ] **Step 8: Fix `SqlMergeRepositoryTests.cs`** applying root causes 7, 8.

- [ ] **Step 9: Full build and full test run**

Run: `dotnet build "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: **0 errors, 0 warnings** — this is the first point where the entire project should build clean (Task 0a's files are already fixed by this point).

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: all tests pass (report the exact pass/fail count — if any test that isn't one you touched now fails at runtime rather than compile time, report that explicitly as a `DONE_WITH_CONCERNS` finding, don't silently fix unrelated runtime failures without flagging them first).

- [ ] **Step 10: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.Tests/TestBase.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/SqlMergeRepositoryTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/SearchIndexReferenceDataCacheTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/HybridTerminologyServiceTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ChainedExpressionProcessorTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ReferenceSearchParameterTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/NotReferencedSearchParameterTests.cs
git commit -m "fix(tests): restore compile-clean test project (TestBase + 6 dependent files)"
```

---

## Task 1: Characterization tests for resource-level search parameters

**Files:**
- Create: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorResourceLevelTests.cs`

**Context:** `SearchParameterQueryGenerator.cs` (2113 lines) has no dedicated test file today. Before Task 3 touches its duplicated `BinaryOperator` switches, lock down current behavior for the four resource-level parameters it special-cases by string comparison (`_id`, `_lastUpdated`, `_ttl`, `_type` — see `SearchParameterQueryGenerator.cs:77-97`).

**Interfaces:**
- Consumes: `SearchParameterQueryGenerator` public constructor `(FhirDbContext context, SearchIndexReferenceDataCache cache, ILogger<SearchParameterQueryGenerator> logger, CompositeSearchParameterQueryGenerator compositeQueryGenerator)` and its public method `Task<IQueryable<long>> GenerateQueryAsync(short? resourceTypeId, SearchParameterExpression expression, CancellationToken cancellationToken)`.
- `CompositeSearchParameterQueryGenerator` public constructor: `(FhirDbContext context, SearchIndexReferenceDataCache cache, ILogger<CompositeSearchParameterQueryGenerator> logger)`.
- `TestBase` provides `Context`, `Cache`, `LoggerFactory`, `CreateResource(short resourceTypeId, string resourceId, int version = 1, bool isHistory = false, bool isDeleted = false)`.

- [ ] **Step 1: Write the test class skeleton and constructor**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Characterization tests pinning down current behavior of SearchParameterQueryGenerator's
/// resource-level parameter handling (_id, _lastUpdated, _ttl, _type) before Task 3 of the
/// SQL data layer cleanup plan extracts its duplicated BinaryOperator switches.
/// </summary>
public class SearchParameterQueryGeneratorResourceLevelTests : TestBase
{
    private readonly SearchParameterQueryGenerator _generator;

    public SearchParameterQueryGeneratorResourceLevelTests()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());

        _generator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(),
            compositeGenerator);
    }
}
```

- [ ] **Step 2: Write `_id` equality test**

```csharp
    [Fact]
    public async Task GivenIdEquality_WhenGeneratingQuery_ThenReturnsMatchingResourceOnly()
    {
        // Arrange
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateResource(resourceTypeId: 1, resourceId: "patient-2");

        var idParameter = new SearchParameterInfo("_id", "_id", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            idParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "patient-1", false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(patient.ResourceSurrogateId);
    }
```

- [ ] **Step 3: Write `_id` multi-value (OR) test**

```csharp
    [Fact]
    public async Task GivenMultipleIdValues_WhenGeneratingQuery_ThenReturnsAllMatchingResources()
    {
        // Arrange
        var patient1 = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var patient2 = CreateResource(resourceTypeId: 1, resourceId: "patient-2");
        CreateResource(resourceTypeId: 1, resourceId: "patient-3");

        var idParameter = new SearchParameterInfo("_id", "_id", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            idParameter,
            new MultiaryExpression(
                MultiaryOperator.Or,
                new Expression[]
                {
                    new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "patient-1", false),
                    new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "patient-2", false),
                }));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldContain(patient1.ResourceSurrogateId);
        results.ShouldContain(patient2.ResourceSurrogateId);
    }
```

- [ ] **Step 4: Write `_lastUpdated` comparator tests (one per `BinaryOperator` the current switch handles)**

```csharp
    [Theory]
    [InlineData(BinaryOperator.Equal, 0, false)]
    [InlineData(BinaryOperator.GreaterThan, 1, true)]
    [InlineData(BinaryOperator.GreaterThanOrEqual, 0, true)]
    [InlineData(BinaryOperator.LessThan, -1, true)]
    [InlineData(BinaryOperator.LessThanOrEqual, 0, true)]
    [InlineData(BinaryOperator.NotEqual, 1, true)]
    public async Task GivenLastUpdatedComparator_WhenGeneratingQuery_ThenAppliesCorrectComparison(
        BinaryOperator op, int surrogateIdOffsetDays, bool expectMatch)
    {
        // Arrange: resource's ResourceSurrogateId encodes its creation time via IdHelper.ToId(),
        // so a resource created "now" and a target date offset by `surrogateIdOffsetDays` days
        // exercise each comparator direction using the same _lastUpdated encoding production code uses.
        var referenceDate = DateTimeOffset.UtcNow.Date;
        var resource = CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        var lastUpdatedParameter = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date);
        var targetDate = referenceDate.AddDays(surrogateIdOffsetDays);
        var expression = new SearchParameterExpression(
            lastUpdatedParameter,
            new BinaryExpression(op, FieldName.DateTimeStart, null, targetDate));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert: this test's purpose is to CAPTURE current behavior, not assert a spec-correct
        // expectation. Run it against the pre-refactor code, record the actual pass/fail per
        // InlineData row in the task report, and use that recorded behavior (not `expectMatch`
        // as written) as the ground truth if it disagrees — adjust `expectMatch` values to match
        // observed pre-refactor output before committing, then this test becomes the regression
        // guard for Task 3/5.
        results.Contains(resource.ResourceSurrogateId).ShouldBe(expectMatch);
    }
```

- [ ] **Step 5: Write `_ttl` comparator test and `_ttl:missing` test**

```csharp
    [Fact]
    public async Task GivenTtlGreaterThan_WhenGeneratingQuery_ThenReturnsResourcesWithLaterExpiry()
    {
        // Arrange
        var expiringLate = CreateResource(resourceTypeId: 1, resourceId: "patient-late");
        var expiringEarly = CreateResource(resourceTypeId: 1, resourceId: "patient-early");
        var cutoff = DateTimeOffset.UtcNow;

        Context.ResourceTtls.Add(new Ignixa.DataLayer.SqlEntityFramework.Entities.ResourceTtlEntity
        {
            ResourceTypeId = 1,
            ResourceId = expiringLate.ResourceId,
            ExpiresAt = cutoff.AddDays(1),
        });
        Context.ResourceTtls.Add(new Ignixa.DataLayer.SqlEntityFramework.Entities.ResourceTtlEntity
        {
            ResourceTypeId = 1,
            ResourceId = expiringEarly.ResourceId,
            ExpiresAt = cutoff.AddDays(-1),
        });
        await Context.SaveChangesAsync();

        var ttlParameter = new SearchParameterInfo("_ttl", "_ttl", SearchParamType.Date);
        var expression = new SearchParameterExpression(
            ttlParameter,
            new BinaryExpression(BinaryOperator.GreaterThan, FieldName.DateTimeStart, null, cutoff));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(expiringLate.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTtlMissing_WhenGeneratingQuery_ThenReturnsOnlyResourcesWithoutTtl()
    {
        // Arrange
        var withTtl = CreateResource(resourceTypeId: 1, resourceId: "patient-with-ttl");
        var withoutTtl = CreateResource(resourceTypeId: 1, resourceId: "patient-without-ttl");

        Context.ResourceTtls.Add(new Ignixa.DataLayer.SqlEntityFramework.Entities.ResourceTtlEntity
        {
            ResourceTypeId = 1,
            ResourceId = withTtl.ResourceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        await Context.SaveChangesAsync();

        var ttlParameter = new SearchParameterInfo("_ttl", "_ttl", SearchParamType.Date);
        var expression = new SearchParameterExpression(
            ttlParameter,
            new MissingSearchParameterExpression(ttlParameter, isMissing: true));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(withoutTtl.ResourceSurrogateId);
    }
```

- [ ] **Step 6: Write `_type` single-value and multi-value tests**

```csharp
    [Fact]
    public async Task GivenTypeEquality_WhenGeneratingQuery_ThenReturnsOnlyMatchingType()
    {
        // Arrange
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateResource(resourceTypeId: 2, resourceId: "org-1");

        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            typeParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Patient", false));

        // Act: system-wide search (resourceTypeId: null) so _type is the only type filter
        var query = await _generator.GenerateQueryAsync(resourceTypeId: null, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTypeConstrainedSearchWithMismatchedTypeFilter_WhenGeneratingQuery_ThenReturnsEmpty()
    {
        // Arrange: resourceTypeId constrains the search to Patient (1), but _type asks for Observation.
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            typeParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Observation", false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldBeEmpty();
    }
```

- [ ] **Step 7: Run the new tests and record actual results**

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --filter "FullyQualifiedName~SearchParameterQueryGeneratorResourceLevelTests" --nologo -v quiet`
Expected: all tests pass. For the `[Theory]` in Step 4, if any `InlineData` row fails against pre-refactor code, adjust that row's `expectMatch` to match observed behavior (per the comment in Step 4) and re-run until green — the point is capturing truth, not asserting a hoped-for spec.

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorResourceLevelTests.cs
git commit -m "test(sql): characterize SearchParameterQueryGenerator resource-level parameter behavior"
```

---

## Task 2: Characterization tests for composite search parameters

**Files:**
- Create: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs`

**Context:** `CompositeSearchParameterQueryGenerator.cs` (803 lines) has no dedicated test file. Before Task 4 touches its token/system encoding logic, lock down current behavior for the five composite shapes it supports (`DetermineCompositeType`, `CompositeSearchParameterQueryGenerator.cs:46-113`): TokenToken, TokenQuantity, TokenDateTime, TokenString, ReferenceToken.

**Interfaces:**
- Consumes: `CompositeSearchParameterQueryGenerator` public constructor `(FhirDbContext context, SearchIndexReferenceDataCache cache, ILogger<CompositeSearchParameterQueryGenerator> logger)` and its public methods `GenerateTokenTokenQueryAsync`, `GenerateTokenQuantityQueryAsync`, `GenerateTokenStringQueryAsync`, `GenerateReferenceTokenQueryAsync`, `GenerateTokenDateTimeQueryAsync` (all `Task<IQueryable<long>> (short? resourceTypeId, short searchParamId, Expression component0, Expression component1, CancellationToken cancellationToken)`).
- `SearchIndexReferenceDataCache` has `GetOrCreateSystemIdAsync(string system)` used internally by the generator — tests do not call it directly, but inserted `SystemEntity` rows must use IDs the cache would assign; simplest is to let the generator create systems itself by passing system strings and asserting against the resulting query, not by pre-seeding `SystemEntity`.

- [ ] **Step 1: Write the test class skeleton**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Characterization tests pinning down current behavior of CompositeSearchParameterQueryGenerator
/// across its five supported composite shapes, before Task 4 of the SQL data layer cleanup plan
/// extracts its token/system encoding logic into a shared helper.
/// </summary>
public class CompositeSearchParameterQueryGeneratorTests : TestBase
{
    private readonly CompositeSearchParameterQueryGenerator _generator;

    public CompositeSearchParameterQueryGeneratorTests()
    {
        _generator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
    }
}
```

- [ ] **Step 2: Write TokenToken composite test**

```csharp
    [Fact]
    public async Task GivenTokenTokenComposite_WhenBothComponentsMatch_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 100;

        Context.TokenTokenCompositeSearchParams.Add(new TokenTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "8480-6",
            SystemId1 = null,
            Code2 = "final",
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "8480-6", false);
        var component1 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "final", false);

        // Act
        var query = await _generator.GenerateTokenTokenQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }
```

- [ ] **Step 3: Write TokenQuantity composite test (equality range)**

```csharp
    [Fact]
    public async Task GivenTokenQuantityComposite_WhenValueInRange_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 101;

        Context.TokenQuantityCompositeSearchParams.Add(new TokenQuantityCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "8462-4",
            SystemId1 = null,
            LowValue = 80m,
            HighValue = 80m,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "8462-4", false);
        var component1 = new MultiaryExpression(
            MultiaryOperator.And,
            new Expression[]
            {
                new BinaryExpression(BinaryOperator.GreaterThanOrEqual, FieldName.Quantity, null, 80m),
                new BinaryExpression(BinaryOperator.LessThanOrEqual, FieldName.Quantity, null, 80m),
            });

        // Act
        var query = await _generator.GenerateTokenQuantityQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }
```

- [ ] **Step 4: Write TokenDateTime composite test**

```csharp
    [Fact]
    public async Task GivenTokenDateTimeComposite_WhenDateMatches_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 102;
        var targetDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        Context.TokenDateTimeCompositeSearchParams.Add(new TokenDateTimeCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "status",
            SystemId1 = null,
            StartDateTime2 = targetDate,
            EndDateTime2 = targetDate,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "status", false);
        var component1 = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, null, targetDate);

        // Act
        var query = await _generator.GenerateTokenDateTimeQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }
```

- [ ] **Step 5: Write TokenString composite test**

```csharp
    [Fact]
    public async Task GivenTokenStringComposite_WhenStringPrefixMatches_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 103;

        Context.TokenStringCompositeSearchParams.Add(new TokenStringCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "component-code",
            SystemId1 = null,
            Text2 = "SMITH",
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "component-code", false);
        var component1 = new StringExpression(StringOperator.Equals, FieldName.String, null, "Smith", false);

        // Act
        var query = await _generator.GenerateTokenStringQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }
```

- [ ] **Step 6: Write ReferenceToken composite test, including the swapped-component-order case**

```csharp
    [Fact]
    public async Task GivenReferenceTokenComposite_WhenComponentsInExpectedOrder_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "docref-1");
        const short searchParamId = 104;

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            ReferenceResourceId1 = "practitioner-1",
            Code2 = "author",
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        var referenceComponent = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "practitioner-1", false);
        var tokenComponent = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "author", false);

        // Act: component0 = reference, component1 = token (expected order)
        var query = await _generator.GenerateReferenceTokenQueryAsync(resourceTypeId: 3, searchParamId, referenceComponent, tokenComponent, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReferenceTokenComposite_WhenComponentsSwapped_ThenStillReturnsResource()
    {
        // Arrange: reproduces the DocumentReference "relationship" case
        // (CompositeSearchParameterQueryGenerator.cs:318-346) where FHIR's spec-defined component
        // order is inconsistent and the generator must detect the swap at runtime.
        var resource = CreateResource(resourceTypeId: 3, resourceId: "docref-1");
        const short searchParamId = 105;

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            ReferenceResourceId1 = "docref-2",
            Code2 = "replaces",
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        var tokenComponent = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "replaces", false);
        var referenceComponent = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "docref-2", false);

        // Act: component0 = token, component1 = reference (swapped order)
        var query = await _generator.GenerateReferenceTokenQueryAsync(resourceTypeId: 3, searchParamId, tokenComponent, referenceComponent, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }
```

- [ ] **Step 7: Run the new tests**

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests" --nologo -v quiet`
Expected: all 6 tests pass. If any fails, read the actual generated query behavior and correct the test's Arrange/Assert to match real behavior (characterization, not aspiration) — do not change `CompositeSearchParameterQueryGenerator.cs` itself in this task.

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs
git commit -m "test(sql): characterize CompositeSearchParameterQueryGenerator across all five composite shapes"
```

---

## Task 3: Extract shared BinaryOperator-to-predicate helper

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`

**Context:** Nine near-identical `BinaryOperator → EF predicate` switch statements exist in `SearchParameterQueryGenerator.cs`: `ProcessResourceLastUpdatedExpressionAsync` (~478-517), `ProcessResourceLastUpdatedMultiaryExpressionAsync` (~552-591), `ProcessResourceTtlExpressionAsync` (two variants, ~629-680), `ProcessResourceTtlMultiaryExpressionAsync` (~737-788), `BuildSingleConditionDateTimeQuery` (~1157-1190, once per field). Extract one shared comparison function that all nine call. **Do not change the generated query shape** — this task must produce identical `IQueryable` filtering behavior to before, verified by Task 1's characterization tests staying green.

**Interfaces:**
- Produces (as actually shipped — see Step 2 below, which corrects this from an initial `Compare<T>(op, fieldValue, targetValue) : bool` sketch that turned out not to be EF-translatable): one static `Apply*Comparison` method per entity/value-type combination on `ComparisonPredicates` — `ApplySurrogateIdComparison`, `ApplyTtlComparison`, `ApplyDateTimeStartComparison`, `ApplyDateTimeEndComparison`, `ApplyNumberRangeComparison`, `ApplyQuantityRangeComparison` — each taking an `IQueryable<TEntity>` plus a `BinaryOperator` and returning the filtered `IQueryable`, with every `Where()` lambda body kept literal so EF Core's LINQ-to-SQL translator can see the operator directly in the expression tree.

- [ ] **Step 1: Read `SearchParameterQueryGenerator.cs` in full** (both the previously-read first 1200 lines and the remaining 1201-2113) to find every one of the nine duplicate switch sites precisely, since only the first 1200 lines were confirmed during the audit.

- [ ] **Step 2: Create `ComparisonPredicates.cs`**

EF Core's LINQ provider cannot translate an arbitrary static method call (e.g. one going through `IComparable<T>.CompareTo`) inside a `Where(x => ...)` lambda into SQL — every `Where` lambda body must contain the comparison operator literally in the expression tree. So this helper cannot be a single generic `Compare<T>` method called from inside a lambda; instead it exposes one method per entity/value-type combination, each an operator `switch` whose six arms are separately-written `Where` calls with literal lambda bodies. This still collapses the nine duplicate six-armed switches in `SearchParameterQueryGenerator.cs` down to one call site each — the deduplication is at the "which of six `Where` calls do I make" level, not inside the lambda bodies themselves:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Shared BinaryOperator-to-EF-predicate dispatch. Each overload's six Where() calls use literal
/// lambda bodies (required for EF Core's LINQ-to-SQL translator) rather than a shared delegate,
/// but the operator dispatch itself — previously duplicated nine times in SearchParameterQueryGenerator
/// — is written once per entity/value-type combination and called from every site that needs it.
/// </summary>
public static class ComparisonPredicates
{
    public static IQueryable<Entities.ResourceEntity> ApplySurrogateIdComparison(
        IQueryable<Entities.ResourceEntity> query, BinaryOperator op, long targetId) => op switch
    {
        BinaryOperator.Equal => query.Where(r => r.ResourceSurrogateId == targetId),
        BinaryOperator.NotEqual => query.Where(r => r.ResourceSurrogateId != targetId),
        BinaryOperator.GreaterThan => query.Where(r => r.ResourceSurrogateId > targetId),
        BinaryOperator.GreaterThanOrEqual => query.Where(r => r.ResourceSurrogateId >= targetId),
        BinaryOperator.LessThan => query.Where(r => r.ResourceSurrogateId < targetId),
        BinaryOperator.LessThanOrEqual => query.Where(r => r.ResourceSurrogateId <= targetId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for surrogate ID comparison"),
    };

    public static IQueryable<(Entities.ResourceEntity Resource, Entities.ResourceTtlEntity Ttl)> ApplyTtlComparison(
        IQueryable<(Entities.ResourceEntity Resource, Entities.ResourceTtlEntity Ttl)> query, BinaryOperator op, DateTimeOffset targetValue) => op switch
    {
        BinaryOperator.Equal => query.Where(x => x.Ttl.ExpiresAt == targetValue),
        BinaryOperator.NotEqual => query.Where(x => x.Ttl.ExpiresAt != targetValue),
        BinaryOperator.GreaterThan => query.Where(x => x.Ttl.ExpiresAt > targetValue),
        BinaryOperator.GreaterThanOrEqual => query.Where(x => x.Ttl.ExpiresAt >= targetValue),
        BinaryOperator.LessThan => query.Where(x => x.Ttl.ExpiresAt < targetValue),
        BinaryOperator.LessThanOrEqual => query.Where(x => x.Ttl.ExpiresAt <= targetValue),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for TTL comparison"),
    };

    public static IQueryable<long> ApplyDateTimeStartComparison(
        IQueryable<Entities.DateTimeSearchParamEntity> query, BinaryOperator op, DateTime value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.StartDateTime == value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.NotEqual => query.Where(sp => sp.StartDateTime != value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThan => query.Where(sp => sp.StartDateTime > value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.StartDateTime >= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThan => query.Where(sp => sp.StartDateTime < value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.StartDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        _ => Enumerable.Empty<long>().AsQueryable(),
    };

    public static IQueryable<long> ApplyDateTimeEndComparison(
        IQueryable<Entities.DateTimeSearchParamEntity> query, BinaryOperator op, DateTime value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.EndDateTime == value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.NotEqual => query.Where(sp => sp.EndDateTime != value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThan => query.Where(sp => sp.EndDateTime > value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.EndDateTime >= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThan => query.Where(sp => sp.EndDateTime < value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.EndDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        _ => Enumerable.Empty<long>().AsQueryable(),
    };
}
```

Note: the `ApplyTtlComparison` signature assumes the TTL join sites can be restructured to a tuple-projected `IQueryable<(ResourceEntity, ResourceTtlEntity)>` before filtering — read the actual current join shape in `ProcessResourceTtlExpressionAsync`/`ProcessResourceTtlMultiaryExpressionAsync` (they use `from r in ... join ttl in ... select r`, not a tuple projection) before assuming this signature compiles as written; adjust the helper's shape to match the real query structure if the join needs restructuring, since correctness beats matching this sketch verbatim — if adapting the signature, keep every `Where` lambda body literal for EF translation, which is the actual constraint driving this task.

- [ ] **Step 3: Replace all nine duplicate switch sites in `SearchParameterQueryGenerator.cs`** with calls to the appropriate `ComparisonPredicates` method, deleting the inline switches.

- [ ] **Step 4: Build**

Run: `dotnet build "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj" --nologo -v quiet`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Run Task 1's characterization tests plus this project's full search test suite**

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: all tests pass, including every `SearchParameterQueryGeneratorResourceLevelTests` case from Task 1 — any failure here means the dedup changed behavior, which this task must not do.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs
git commit -m "refactor(sql): extract ComparisonPredicates to deduplicate nine BinaryOperator switches"
```

---

## Task 4: Extract shared token-code storage convention helper

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/TokenCodeStorage.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenSearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`

**Context:** Two conventions are independently re-encoded on the write path (`TokenSearchParameterRowGenerator.cs:83-96,100-109`) and read path (`CompositeSearchParameterQueryGenerator.ExtractTokenValuesFromSingle`, lines 509-533): (1) an empty/null token system string means "explicitly no system," matched via NULL in the `SystemId` column; (2) token codes longer than 128 characters are split into a 128-char `Code` column plus a `CodeOverflow` column. Extract both into one shared static helper both files call.

**Interfaces:**
- Produces: `TokenCodeStorage.IsExplicitNoSystem(string? system) : bool`, `TokenCodeStorage.SplitCode(string code) : (string Code, string? CodeOverflow)`, `TokenCodeStorage.MaxInlineCodeLength` (const `128`).

- [ ] **Step 1: Create `TokenCodeStorage.cs`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Encodes the storage conventions for token search parameter system/code columns, shared by the
/// write path (RowGenerators) and read path (Search query generators) so the two are never allowed
/// to drift, per the SQL data layer cleanup plan's Task 4.
/// </summary>
public static class TokenCodeStorage
{
    /// <summary>
    /// Token codes at or under this length are stored inline in the Code column;
    /// longer codes are truncated to this length with the remainder in CodeOverflow.
    /// </summary>
    public const int MaxInlineCodeLength = 128;

    /// <summary>
    /// An empty or null system string means the token explicitly has no system —
    /// stored as a NULL SystemId, matched via the FHIR "|code" convention.
    /// </summary>
    public static bool IsExplicitNoSystem(string? system) => string.IsNullOrEmpty(system);

    /// <summary>
    /// Splits a token code into its inline and overflow parts per <see cref="MaxInlineCodeLength"/>.
    /// </summary>
    public static (string Code, string? CodeOverflow) SplitCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return code.Length > MaxInlineCodeLength
            ? (code[..MaxInlineCodeLength], code[MaxInlineCodeLength..])
            : (code, null);
    }
}
```

- [ ] **Step 2: Update `TokenSearchParameterRowGenerator.cs`** — replace the inline `string.IsNullOrEmpty(tokenValue.System)` check (line ~83) with `TokenCodeStorage.IsExplicitNoSystem(tokenValue.System)`, and replace the inline length-check-and-substring logic (lines ~100-109) with a call to `TokenCodeStorage.SplitCode(tokenValue.Code)`, using the returned tuple to set `record.SetString(4, code)` and either `record.SetString(5, codeOverflow)` or `record.SetDBNull(5)` when `codeOverflow` is null. Apply the same replacement to `ExtractExtensionData`'s equivalent inline truncation at line ~161.

- [ ] **Step 3: Update `CompositeSearchParameterQueryGenerator.ExtractTokenValuesFromSingle`** (lines 509-533) — replace its inline `string.IsNullOrEmpty(stringExpr.Value)` empty-system check with `TokenCodeStorage.IsExplicitNoSystem(stringExpr.Value)`, keeping the surrounding tuple-return logic unchanged (this method returns `(System, Code, SystemIsEmpty)`; only the emptiness check itself is replaced, not the method's shape).

- [ ] **Step 4: Build**

Run: `dotnet build "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj" --nologo -v quiet`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Run Task 2's characterization tests plus the full test suite**

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: all tests pass, including every `CompositeSearchParameterQueryGeneratorTests` case from Task 2.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/TokenCodeStorage.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenSearchParameterRowGenerator.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs
git commit -m "refactor(sql): extract TokenCodeStorage to unify write/read token encoding conventions"
```

---

## Task 5 (Phase 1): Adopt IExpressionVisitor in SearchExpressionQueryBuilder

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs`
- Create: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchExpressionQueryBuilderVisitorTests.cs`

**Context:** `SearchExpressionQueryBuilder.ApplySearchExpressionAsync` (`SearchExpressionQueryBuilder.cs:80-92`) currently dispatches via a hand-written `expression switch` type-pattern instead of Core's existing `IExpressionVisitor<TContext, TOutput>` double-dispatch contract (`src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs`). `IExpressionVisitor<in TContext, out TOutput>` covers exactly the nine `Expression` subtypes the current switch handles (`VisitMultiary`, `VisitSearchParameter`, `VisitChained`, `VisitCompartment`, `VisitPatientEverything`, `VisitUnion`, `VisitNotExpression`, `VisitMissingSearchParameter`, `VisitNotReferenced`) plus others this class doesn't need to act on (`VisitBinary`, `VisitMissingField`, `VisitString`, `VisitInclude`, `VisitSortParameter`, `VisitIn`) — those extra methods must still be implemented (interface requires all members) but can throw `NotSupportedException`, since `SearchExpressionQueryBuilder` only ever receives top-level expression shapes, never bare field-level expressions directly (those are handled inside `SearchParameterQueryGenerator`, out of scope for this task). `TOutput` is covariant (`out TOutput`), so `Task<IQueryable<ResourceEntity>>` (a reference type) is a valid `TOutput`. This task changes **dispatch mechanism only** — every `Apply*ExpressionAsync` method's body moves into the matching `Visit*` method unchanged; `CombineWithAnd`/`CombineWithOr` and all private helpers stay as-is.

**Interfaces:**
- Produces: `SearchExpressionQueryBuilder : IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>` where `SqlQueryContext` is a new `readonly record struct SqlQueryContext(IQueryable<ResourceEntity> BaseQuery, short? ResourceTypeId, CancellationToken CancellationToken)` — packs the three values every current private method takes as separate parameters, since `IExpressionVisitor`'s `Visit*` methods take only `(expression, context)`, no separate parameter list.
- The existing public entry point `Task<IQueryable<ResourceEntity>> ApplySearchExpressionAsync(IQueryable<ResourceEntity> baseQuery, short? resourceTypeId, Expression expression, CancellationToken ct)` **must keep its exact current signature** — every caller of `SearchExpressionQueryBuilder` outside this file is unaffected by this task. Its body becomes: construct a `SqlQueryContext`, call `expression.AcceptVisitor(this, context)`, return the result.

- [ ] **Step 1: Add the `SqlQueryContext` type and change the class declaration**

```csharp
public readonly record struct SqlQueryContext(
    IQueryable<ResourceEntity> BaseQuery,
    short? ResourceTypeId,
    CancellationToken CancellationToken);
```

Place this above the `SearchExpressionQueryBuilder` class in the same file. Change the class declaration to:

```csharp
public class SearchExpressionQueryBuilder : IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>
```

- [ ] **Step 2: Rewrite `ApplySearchExpressionAsync` to dispatch via `AcceptVisitor`**

```csharp
    public Task<IQueryable<ResourceEntity>> ApplySearchExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short? resourceTypeId,
        Expression expression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expression);

        _logger.LogDebug(
            "ApplySearchExpressionAsync: ExpressionType={ExpressionType}, ResourceTypeId={ResourceTypeId}",
            expression.GetType().Name,
            resourceTypeId);

        var context = new SqlQueryContext(baseQuery, resourceTypeId, ct);
        return expression.AcceptVisitor(this, context);
    }
```

- [ ] **Step 3: Rename each existing private `Apply*ExpressionAsync` method to the matching public `Visit*` method**, changing its parameter list from `(IQueryable<ResourceEntity> baseQuery, short? resourceTypeId, TExpression expression, CancellationToken ct)` to `(TExpression expression, SqlQueryContext context)`, and replacing every internal reference to `baseQuery`/`resourceTypeId`/`ct` with `context.BaseQuery`/`context.ResourceTypeId`/`context.CancellationToken`. Recursive calls (e.g. `ApplyMultiaryExpressionAsync` calling `ApplySearchExpressionAsync` on sub-expressions) become `subExpr.AcceptVisitor(this, context)` instead of a direct method call, so nested expressions also go through the visitor dispatch rather than a hand-written recursive call:

  - `ApplyMultiaryExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitMultiary(MultiaryExpression expression, SqlQueryContext context)`
  - `ApplySearchParameterExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitSearchParameter(SearchParameterExpression expression, SqlQueryContext context)`
  - `ApplyChainedExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitChained(ChainedExpression expression, SqlQueryContext context)`
  - `ApplyCompartmentSearchExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitCompartment(CompartmentSearchExpression expression, SqlQueryContext context)`
  - `ApplyPatientEverythingExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitPatientEverything(PatientEverythingExpression expression, SqlQueryContext context)`
  - `ApplyUnionExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitUnion(UnionExpression expression, SqlQueryContext context)`
  - `ApplyNotExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitNotExpression(NotExpression expression, SqlQueryContext context)`
  - `ApplyMissingSearchParameterExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitMissingSearchParameter(MissingSearchParameterExpression expression, SqlQueryContext context)`
  - `ApplyNotReferencedExpressionAsync` → `public Task<IQueryable<ResourceEntity>> VisitNotReferenced(NotReferencedExpression expression, SqlQueryContext context)`

  Inside `VisitMultiary` and `VisitUnion` and `VisitNotExpression` specifically, every `await ApplySearchExpressionAsync(baseQuery, resourceTypeId, subExpr, ct)` call becomes `await subExpr.AcceptVisitor(this, context)`.

- [ ] **Step 4: Implement the six interface members this class doesn't act on**, each throwing:

```csharp
    public Task<IQueryable<ResourceEntity>> VisitBinary(BinaryExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(BinaryExpression)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");

    public Task<IQueryable<ResourceEntity>> VisitMissingField(MissingFieldExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(MissingFieldExpression)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");

    public Task<IQueryable<ResourceEntity>> VisitString(StringExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(StringExpression)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");

    public Task<IQueryable<ResourceEntity>> VisitInclude(IncludeExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(IncludeExpression)} is handled by {nameof(IncludeProcessor)}/{nameof(RevIncludeProcessor)}, not by {nameof(SearchExpressionQueryBuilder)}.");

    public Task<IQueryable<ResourceEntity>> VisitSortParameter(SortExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SortExpression)} is applied to sort order separately, not through {nameof(SearchExpressionQueryBuilder)}.");

    public Task<IQueryable<ResourceEntity>> VisitIn<T>(InExpression<T> expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(InExpression<T>)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");
```

Before writing these, confirm via Step 1's build (next step) whether `ApplySearchExpressionAsync`'s original `_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported")` fallback (the old switch's default arm) is still reachable anywhere — it should no longer be needed since the visitor interface is now exhaustive over all `Expression` subtypes, but confirm no caller depended on that exact exception message/type before removing it entirely.

- [ ] **Step 5: Build**

Run: `dotnet build "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj" --nologo -v quiet`
Expected: 0 errors, 0 warnings. If the compiler reports a missing interface member, that member was missed in Steps 3-4 — the interface is the exhaustiveness check this task is specifically introducing, so a compile error here is expected mid-task, not a bug.

- [ ] **Step 6: Write a visitor-dispatch regression test**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Confirms SearchExpressionQueryBuilder's IExpressionVisitor-based dispatch (Task 5 of the SQL
/// data layer cleanup plan) produces identical results to the pre-refactor expression-switch
/// dispatch for representative expression shapes, including nested recursion through AcceptVisitor.
/// </summary>
public class SearchExpressionQueryBuilderVisitorTests : TestBase
{
    private readonly SearchExpressionQueryBuilder _builder;

    public SearchExpressionQueryBuilderVisitorTests()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
        var parameterGenerator = new SearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(), compositeGenerator);
        var chainedProcessor = new ChainedExpressionProcessor(
            Context, Cache, parameterGenerator, LoggerFactory.CreateLogger<ChainedExpressionProcessor>());
        var compartmentGenerator = new CompartmentSearchQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<CompartmentSearchQueryGenerator>());
        var patientEverythingGenerator = new PatientEverythingQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<PatientEverythingQueryGenerator>());

        _builder = new SearchExpressionQueryBuilder(
            Context,
            parameterGenerator,
            chainedProcessor,
            compartmentGenerator,
            patientEverythingGenerator,
            Substitute.For<Ignixa.Domain.Abstractions.ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<SearchExpressionQueryBuilder>());
    }

    [Fact]
    public async Task GivenSingleSearchParameterExpression_WhenApplied_ThenReturnsMatchingResource()
    {
        // Arrange
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateStringSearchParam(patient.ResourceSurrogateId, resourceTypeId: 1, searchParamId: 1, text: "Smith");

        var expression = new SearchParameterExpression(
            new SearchParameterInfo("name", "name", SearchParamType.String) { Uri = "http://hl7.org/fhir/SearchParameter/Patient-name" },
            new StringExpression(StringOperator.Equals, FieldName.String, null, "Smith", false));

        // Act
        var result = await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None);
        var results = await result.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ResourceSurrogateId.ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenNestedMultiaryOfMultiary_WhenApplied_ThenRecursesThroughAcceptVisitorCorrectly()
    {
        // Arrange: AND(OR(name=Smith, name=Jones), _type=Patient) exercises AcceptVisitor recursion
        // through two nested MultiaryExpression levels (Step 3's dispatch-via-AcceptVisitor change).
        var smith = CreateResource(resourceTypeId: 1, resourceId: "patient-smith");
        CreateStringSearchParam(smith.ResourceSurrogateId, resourceTypeId: 1, searchParamId: 1, text: "Smith");
        var other = CreateResource(resourceTypeId: 1, resourceId: "patient-other");
        CreateStringSearchParam(other.ResourceSurrogateId, resourceTypeId: 1, searchParamId: 1, text: "Taylor");

        var nameParameter = new SearchParameterInfo("name", "name", SearchParamType.String) { Uri = "http://hl7.org/fhir/SearchParameter/Patient-name" };
        var orExpression = new SearchParameterExpression(
            nameParameter,
            new MultiaryExpression(
                MultiaryOperator.Or,
                new Expression[]
                {
                    new StringExpression(StringOperator.Equals, FieldName.String, null, "Smith", false),
                    new StringExpression(StringOperator.Equals, FieldName.String, null, "Jones", false),
                }));

        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token);
        var typeExpression = new SearchParameterExpression(
            typeParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Patient", false));

        var andExpression = new MultiaryExpression(MultiaryOperator.And, new Expression[] { orExpression, typeExpression });

        // Act
        var result = await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, andExpression, CancellationToken.None);
        var results = await result.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ResourceSurrogateId.ShouldBe(smith.ResourceSurrogateId);
    }

    [Fact]
    public void GivenBareBinaryExpression_WhenApplied_ThenThrowsNotSupported()
    {
        // Arrange: proves the six unused interface members (Step 4) are wired up, not silently
        // returning null/default.
        var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, null, DateTime.UtcNow);

        // Act & Assert
        Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }
}
```

Check `SearchExpressionQueryBuilder`'s actual constructor parameter list (`SearchExpressionQueryBuilder.cs:41-57`, already confirmed during the audit: `context, parameterQueryGenerator, chainedExpressionProcessor, compartmentQueryGenerator, patientEverythingQueryGenerator, searchParameterDefinitionManager, logger`) and the real constructors of `ChainedExpressionProcessor`, `CompartmentSearchQueryGenerator`, `PatientEverythingQueryGenerator` (read each file's constructor before finalizing this test's arrange block — they were not all individually confirmed during the audit) to make sure this test file actually compiles; adjust constructor arguments to match if they differ from what's sketched here. Add `using NSubstitute;` for `Substitute.For<...>`.

- [ ] **Step 7: Run the new tests plus the full test suite**

Run: `dotnet test "test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj" --nologo -v quiet`
Expected: all tests pass, including every existing `Search/` test (`ChainedExpressionProcessorTests`, `IncludeProcessorTests`, `IterateProcessorTests`, `RevIncludeProcessorTests`, `NotReferencedSearchParameterTests`, `ReferenceSearchParameterTests`) — this task changes only `SearchExpressionQueryBuilder`'s internal dispatch, so every one of these must still pass unchanged, since it proves Task 5 didn't alter observable behavior anywhere in the query pipeline.

**This unit run is necessary but not sufficient** (added per the Phase 0 Fable review, 2026-07-11): `ChainedExpressionProcessor`, `CompartmentSearchQueryGenerator`, and `PatientEverythingQueryGenerator` — three of the nine `Visit*` targets this task adds — all use `EF.Constant()`, which the EF Core InMemory test provider cannot translate (confirmed in Phase 0: it's why `ChainedExpressionProcessorTests` has 4 pre-existing runtime failures under this provider). `CompartmentSearchQueryGenerator` and `PatientEverythingQueryGenerator` have **no unit tests at all** per the original audit. This means the InMemory unit suite structurally cannot verify `VisitChained`/`VisitCompartment`/`VisitPatientEverything` actually preserve behavior against a real relational provider — a green unit run here proves the *other* six Visit methods are safe, not all nine.

- [ ] **Step 7.5: Confirm the E2E (SQL Server) CI job is green on this task's PR before merging**

The repo's `pr-build.yml` runs a dedicated E2E job against a real SQL Server container (`IgnixaApiFixture.UseSqlServer` defaults to true), and `test/Ignixa.Api.E2ETests/Search/Chaining/ChainingTests.cs`/`ChainingAndSortTests.cs` already exercise chained search against it on every PR. This is the actual verification gate for the three InMemory-untestable dispatch paths — do not treat Step 7's unit-test pass alone as proof this task is safe to merge. If the E2E job is red, treat it as a Task 5 regression to diagnose before proceeding, even though the unit suite is green.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchExpressionQueryBuilderVisitorTests.cs
git commit -m "refactor(sql): adopt IExpressionVisitor dispatch in SearchExpressionQueryBuilder (Phase 1)"
```

**Implementation notes carried from the Phase 0 Fable review (2026-07-11), not yet reflected above:**
- The test sketch in Step 6 has two likely compile hazards: verify `SearchParameterInfo`'s `Uri url` constructor parameter and whatever property exposes it are what the sketch assumes, and note `CreateStringSearchParam` is not a `TestBase` member as of Phase 0 — write that seeding inline or add it as a private helper in the new test class, don't assume it exists.
- Prefer **explicit interface implementation** for the six throwing `Visit*` members (`VisitBinary`, `VisitMissingField`, `VisitString`, `VisitInclude`, `VisitSortParameter`, `VisitIn`) so they aren't invocable as public surface on `SearchExpressionQueryBuilder` — `ApplySearchExpressionAsync` should remain the only intended entry point.
- Routing nested sub-expressions through `AcceptVisitor` (Step 3) has two accepted micro-deltas from the current code: nested sub-expressions lose the per-expression debug log (keep the log line inside the `VisitMultiary` loop if traceability matters), and a null nested expression becomes an `NRE` instead of the current code's `ArgumentNullException`. Both are fine to accept as-is.

---

## Post-Plan: Not In This Plan

Per `staged-query-compiler.md`'s own verdict, Phase 2 (composite semantic leaf) and Phase 3 (data-driven catalog) are **not** tasked here — they get their own plan once Task 5's real cost and the Fable phase review's findings are known. Phase 4 (full logical/physical/typed-SQL pipeline) is explicitly not recommended without new evidence it's needed. Do not add tasks for these without a new planning pass.

### Findings carried forward from the Phase 0 Fable review (2026-07-11)

These are not tasks yet — they're evidence for whoever scopes Phase 2/3, or standalone follow-ups, so they aren't lost:

1. **Opening exhibit for Phase 2/3: quantity/number comparator semantics have already silently diverged between the single-parameter and composite paths.** `ComparisonPredicates.ApplyQuantityRangeComparison` (single-parameter path, faithfully preserved from the original `SearchParameterQueryGenerator` code in Task 3) implements `ge → LowValue >= value`, `le → HighValue <= value` — the entire stored range must satisfy the comparison. `CompositeSearchParameterQueryGenerator.ApplyQuantityFilter` (lines ~681-688) implements `ge → HighValue >= value`, `le → LowValue <= value` — range-*overlap* semantics, which its own comments say is deliberate. FHIR search-prefix semantics with implicit-precision ranges favor the overlap reading; the single-parameter path is stricter and can miss boundary matches whenever `LowValue != HighValue`. **This is not a Phase 0 defect** — Phase 0 correctly preserved both behaviors exactly as found — but it means one of the two paths is likely spec-nonconformant today, silently. Whoever scopes Phase 2/3's shared catalog must make a *deliberate* choice here (with a spec citation and characterization tests using `LowValue != HighValue` data), not mechanically unify the two copies as if they were always meant to agree. **Corollary:** do not fold the 3 residual operator switches remaining in `CompositeSearchParameterQueryGenerator` (~lines 682, 750, 765) into `ComparisonPredicates` in some future cleanup pass without resolving this first — two of them encode the deliberately-different semantics, and folding before the decision would either silently change search results or produce a helper that looks shared but isn't.
2. **`HybridTerminologyServiceTests.cs` needs a tracked follow-up, not silent aging.** Deleting its two tests in Task 0b was the right call for this plan's scope (no production-code seam exists to make them testable without a live SQL Server, and adding one was out of bounds for a compile-baseline-restoration task) — but `HybridTerminologyService` is ~195 lines / 7 routing methods with zero unit coverage now, and the root cause (concrete-class constructor dependency, no virtual members) is a real design smell. Someone should either introduce a test seam and restore coverage, or make a deliberate decision to leave the class as an untested tombstone — but it shouldn't just be forgotten.
3. **Silent-failure default arms exist inconsistently across the SQL search code and violate this repo's own no-silent-failures convention.** ~~`ComparisonPredicates.ApplyDateTimeStartComparison`/`ApplyDateTimeEndComparison` silently return empty on an unsupported operator; other `ComparisonPredicates` methods throw~~ **Fixed** (post-PR review pass, 2026-07-11): all six `ComparisonPredicates` methods now throw `NotSupportedException` uniformly, with regression tests in `ComparisonPredicatesTests.cs`. ~~**Still open**: the composite generator's remaining inline switches (`CompositeSearchParameterQueryGenerator.cs` ~lines 691, 755, 770) use `_ => query` for an unsupported operator, meaning **no filter is applied at all and the query returns everything** — the worst of the three failure modes, and now the sole remaining instance of this pattern. Unify to throw, once a behavior change is permitted (this plan's tasks were explicitly zero-behavior-change, so none of them could fix this).~~ **Fixed** (comparator-semantics unification, 2026-07-11): `ApplyQuantityFilterAsync`'s inline switch (the ~line 691 instance) now delegates to a new `ComparisonPredicates.ApplyQuantityRangeComparison(IQueryable<TokenQuantityCompositeSearchParamEntity>, ...)` overload, which throws `NotSupportedException` like every other `ComparisonPredicates` method. The two `ApplyDateTimeFilter` inline switches (~lines 755, 770) still use `_ => query` and remain open — out of scope for this task, which only touched the quantity path.
4. **`TokenCodeStorage` adoption (Task 4) is complete for its two named conventions but one inline duplicate of the empty-system check remains**: ~~`TokenSearchParameterRowGenerator.ExtractExtensionData` still has a standalone `!string.IsNullOrEmpty(tokenValue.System)` presence check (~line 160) that wasn't routed through `TokenCodeStorage.IsExplicitNoSystem`~~ **Fixed** (post-PR review pass, 2026-07-11).
5. **The EF Core InMemory provider's inability to translate SQL-Server-specific relational hints is a structural test-infrastructure limitation, not a one-off — now confirmed twice, and this is a hard prerequisite for Phase 2, not a someday-decision.** `EF.Constant()` affects at least `ChainedExpressionProcessor`, `CompartmentSearchQueryGenerator`, and `PatientEverythingQueryGenerator` (see Task 5's added Step 7.5 above). Task 5 independently found a second instance: `SearchParameterQueryGenerator`'s String/Token query paths (`GenerateStringQueryAsync`/`GenerateTokenQueryAsync`, using `EF.Functions.Collate` unconditionally) also can't be exercised end-to-end under InMemory — Task 5's own new tests had to use Number-type search parameters instead of String/Token to get real passing coverage, sidestepping rather than fixing the gap (correctly, since fixing it was out of scope).
   **Why this can no longer wait**: Phase 0/1 could safely lean on "unit-suite-green + E2E-SQL-Server-CI-as-the-real-gate" because both phases were mechanical dedup/dispatch changes that never touched the InMemory-untestable String/Token/composite paths. Phase 2's entire stated purpose is to change semantic-leaf code in exactly those paths. Scoping Phase 2 without first deciding — Testcontainers-backed real-SQL-Server fixture in the DataLayer unit test project, or deliberately accepting E2E-only coverage for that phase — means Phase 2 would ship with *no unit-level safety net at all* for the code it changes, which is a materially worse position than Phase 0/1 started from. Resolve this, and the quantity/number comparator semantics decision (finding #1 above), *before* writing Phase 2's task list, not while executing it.
   **One data point for Phase 2's own go/no-go, not to be over-extrapolated**: Task 5's realized cost was low (one file, three new tests, one pre-existing provider gap surfaced, zero incidents) — but Task 5's risk was mechanical (dispatch-only), while Phase 2's risk is semantic (actual query-generation behavior). Don't assume Phase 2 will be similarly cheap just because Phase 1 was.
6. **`IterateProcessorTests`'s one pre-existing runtime failure (`GivenIterateInclude_WhenChainOfReferences_ThenReturnsAllInChain`, expected 2 got 1) has never been investigated**, only documented as pre-existing and out of scope across every task in Phase 0. It's not a Task 5 blocker (Task 5's `VisitInclude` throws by design — includes are out of scope for this file), but an unexplained failing test is a hole in the characterization baseline. Worth a time-boxed standalone investigation.

### Findings from the post-PR-review Fable signoff review (2026-07-11)

A multi-agent code review of PR #328 found `TokenCodeStorage.MaxInlineCodeLength` was `128`, but `TokenSearchParam.Code` is a `VARCHAR(256)` column (confirmed via `97.sql`, the TVP's `SqlMetaData` width, and the entity's `[MaxLength(256)]`) — a pre-existing bug (traced to PR #147, predating this feature entirely) fixed in commit `b3ee5790`. A Fable architectural signoff review of that fix surfaced findings that change the picture and are recorded here so they aren't lost:

1. **`CHK_TokenSearchParam_CodeOverflow` is a phantom constraint.** It's declared in `FhirDbContext.cs` and appears in every migration's Designer/snapshot files (model *state*), but no migration `Up()` ever emits the DDL to create it, `97.sql` (the real schema bootstrap) has no `LEN()`-based check constraint anywhere, and `EnsureCreatedAsync` is explicitly banned for this context. It has never been enforced by any database this codebase can build. Confirmed: `TokenCodeStorage.cs`'s doc comment and the `b3ee5790` commit message both originally claimed a mismatch would "fail to insert" — that's false and has been corrected in the code (the commit message itself is already pushed and was not rewritten; this note is the correction of record).
2. **Consequently, the old 128-char split was internally self-consistent, not silently broken** — write and read both used 128 as the threshold, so old rows round-tripped correctly under the old code. The 128→256 fix changes the read-path branch boundary, which means any *legacy* row with an original token code 129–256 characters long (written under the old scheme, so `Code` holds only the first 128 characters) will silently stop matching search after this fix ships, until reindexed — a real, if narrow, backward-compatibility gap the "no migration needed" framing in the original fix missed.
3. **Resolved for this PR**: confirmed no production deployment has run the old 128-char-threshold code against real data, so this band is unpopulated and no backfill migration is required. If that assumption is ever wrong for some deployment, the fix is `Code = LEFT(Code + CodeOverflow, 256)` / recompute `CodeOverflow` for existing `TokenSearchParam` rows where `CodeOverflow IS NOT NULL`, followed by a reindex.
4. **Composite token tables are a second, independent, worse instance of the same drift** — `RowGenerators/TokenTokenCompositeRowGenerator.cs` (and its `TokenQuantityComposite`/`RefTokenComposite` siblings) still split at 128 on write, but `CompositeSearchParameterQueryGenerator.cs`'s composite read paths (~lines 165, 230, 281, 467) do a plain `Code1 == token.Code` compare with **no overflow-aware branch at all** — any composite token component over 128 characters is unsearchable today, unconditionally, not just near a boundary. Not fixed here (separate code paths, untouched by PR #328); worth its own follow-up to route through `TokenCodeStorage`.
5. **`CHK_TokenSearchParam_CodeOverflow`'s fate is an open decision**: either hand-write a migration that actually materializes it (after confirming/backfilling data, per #3), or remove it from the model so the schema stops asserting something untrue. Deliberately left alone for this PR — out of scope for a targeted bug fix.
6. **Minor, not blocking**: the inline read branch (`SearchParameterQueryGenerator.cs` ~lines 1265, 1482) doesn't check `CodeOverflow IS NULL`, so a search for an exactly-256-char code could false-positive against a longer code sharing that 256-char prefix. Pre-existing at the 128 threshold too, just relocated.

### Findings from Phase 2 prerequisite investigation (2026-07-11)

Per this plan's own gate ("resolve the quantity/number comparator semantics decision... before writing Phase 2's task list"), two parallel investigations ran before Phase 2 design started: FHIR spec research into the comparator-semantics divergence, and a Testcontainers feasibility spike for the InMemory-provider test gap. Both surfaced additional findings, two of which were verified real and fixed immediately (not deferred to Phase 2, per human decision) since they were independent, contained correctness bugs rather than architectural work.

1. **Comparator semantics: none of the three implementations that touch number/quantity ranges is fully correct**, each with a different partial-failure pattern against the canonical binding (`gt→High>v, ge→High>=v, lt→Low<v, le→Low<=v`, confirmed via `microsoft/fhir-server`'s `NumericRangeRewriter.cs`, the ancestor this codebase's schema and merge model derive from): `ComparisonPredicates.ApplyNumberRangeComparison`/`ApplyQuantityRangeComparison` has all four flipped; `CompositeSearchParameterQueryGenerator.ApplyQuantityFilterAsync` has `ge`/`le`/`eq` right but `gt`/`lt` wrong; `Ignixa.Search.InMemory.ComparisonValueVisitor` (backing FileSystem/BlobStorage/InMemoryIndex via `SearchQueryInterpreter`) has `gt`/`ge` right but `lt`/`le` wrong. This is the input for Phase 2's design, not yet acted on. `sa`/`eb` prefixes are separately, architecturally unimplemented (aliased to `gt`/`lt`) across the whole `BinaryOperator` enum lineage — inherited from ms-fhir-server itself, a shared gap not a divergence between the three.

   **Resolved**: canonicalized on the ms-fhir-server binding across all three implementations, and gave `sa`/`eb` real `BinaryOperator` values (previously aliased to `gt`/`lt`, and on the InMemory backend for DateTime, indistinguishable from them). See `docs/superpowers/specs/2026-07-11-comparator-semantics-design.md` for the full design and `docs/superpowers/plans/2026-07-11-comparator-semantics-canonicalization.md` for the implementation. This was a live search-behavior change for `gt`/`ge`/`lt`/`le` on any stored value with `Low != High` (implicit-precision values) - no data migration needed, call out in release notes.

2. **Fixed independently, ahead of Phase 2**: `SearchParameterQueryGenerator.GenerateQuantityAndQueryAsync` silently dropped the value filter entirely for unit-qualified `eq`/`ap`/`ne` quantity searches (e.g. `value-quantity=5.4|http://unitsofmeasure.org|mg` matched any value with that unit) because its extraction loop only inspected top-level children of the outer AND, missing the nested `MultiaryExpression` pair `eq`/`ap`/`ne` widen into. Fixed by recursing into nested expressions (mirroring `CompositeSearchParameterQueryGenerator`'s existing pattern) and correctly applying AND semantics for the eq/ap bound pair vs. OR semantics for the ne bound pair. Deliberately did **not** change the Low/High binding convention — that's finding #1's job. Regression tests: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorQuantityAndTests.cs`. Verified end-to-end via a Fable review that traced the exact expression shape back to its sole producer (`SearchValueExpressionBuilderHelper.GenerateNumberExpression`) to confirm the fix's operator-pair matching is exact, not heuristic.
3. **Fixed independently, ahead of Phase 2**: `DatabaseInitializer`'s first-ever-bootstrap sequence had a connection-pool poisoning bug — the expected-to-fail `CanConnectAsync()` probe against a not-yet-existing database trips SqlClient's connection-pool blocking period for that connection string, causing the very next step (`IsDatabaseEmptyAsync`) to receive a cached failure instead of a real round-trip and silently skip running `97.sql` (leaving a zero-table database, or a loud `SqlException 1801` if `MigrateAsync` then also hits the poisoned pool). `SqlConnection.ClearPool()` was tried first and empirically proven **not** to reset the blocking-period tracker (verified against a real Docker SQL Server before settling on the real fix). The actual fix: the existence probe itself now uses a dedicated non-pooled connection (`CanConnectNonPooledAsync`), so an expected failure there never poisons the pool the rest of bootstrap and the app rely on; `IsDatabaseEmptyAsync`'s old fail-conservative catch-and-assume-not-empty was also removed, so a genuine failure now propagates instead of being silently absorbed into a wrong guess. Verified end-to-end against a real Docker SQL Server (cold bootstrap: 84 batches of 97.sql + 6 migrations + 48 tables; idempotent re-run: clean, no-op) by two independent runs, including one by a Fable review. **Carried forward for Phase 2's test-infrastructure task**: this fix has no permanent automated regression test — the failure mode is a timing-dependent SqlClient blocking-period race that the current InMemory-only test suite structurally cannot represent. A future accidental revert to a pooled probe would surface as *intermittent* E2E flakiness, not a deterministic test failure. Phase 2's real-SQL-Server test fixture work (see finding #5) should include a first-boot bootstrap test covering this exact scenario.
4. **A third candidate bug (97.sql not creating `__EFMigrationsHistory`, breaking `MigrateAsync` on fresh bootstrap) was investigated and closed as a false lead** — it doesn't create that table, but that's by design and works correctly; the `SqlException 1801` initially observed was actually finding #3's pool-poisoning bug feeding EF a stale "database doesn't exist" answer, misattributed to the wrong cause. No separate fix needed; resolved by #3.
5. **Testcontainers feasibility spike: viable, hybrid recommended.** A real SQL Server via Testcontainers can close the EF-InMemory-provider gap for `EF.Constant()`/`EF.Functions.Collate()` paths (proven with a working spike reproducing a currently-InMemory-unrunnable `ChainedExpressionProcessor` test against a real container). Recommendation: keep InMemory as the default for the bulk of the test suite; add Testcontainers as opt-in only for the specific classes needing real-SQL coverage (`ChainedExpressionProcessor`, `CompartmentSearchQueryGenerator`, `PatientEverythingQueryGenerator`, and `SearchParameterQueryGenerator`'s String/Token paths) — measured marginal cost (~3.2-4.6s/test bootstrap, ~5-6s one-time container startup) is too slow to impose on the whole fast feedback loop. Recommended lifecycle: one container per test class, fresh uniquely-named database per test (matches the existing InMemory convention). Bootstrap must mirror the real `DatabaseInitializer` path (97.sql + migrations) — never `EnsureCreated()`, which would measure a schema no real deployment has (per finding #1 in the prior section). CI Docker availability confirmed not a blocker (`ubuntu-latest`, already used by the E2E job).
