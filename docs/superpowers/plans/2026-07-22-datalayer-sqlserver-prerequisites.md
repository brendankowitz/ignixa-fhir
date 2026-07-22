# DataLayer.SqlServer Prerequisites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close 5 small, previously-scoped prerequisite items in `Ignixa.DataLayer.SqlServer` and its immediate neighbors before sub-project 3 (the SqlServer-native search adapter) begins.

**Architecture:** Four independent-but-related fixes: (1) a naming-convention rename cascading through an interface and its 3 implementations, (2) a composition bug fix in an Application-layer handler plus a diagnostic-message improvement in the compiler, (3) a composition-root relocation from the EF project into a new `Ignixa.DataLayer.SqlServer` factory, (4) two structural cleanups of `SqlServerFhirRepository.cs`. Every item except the composition fix (item 2) and the diagnostic message (item 6) is behavior-preserving.

**Tech Stack:** C#/.NET, xunit + Shouldly, no new NuGet dependencies.

## Global Constraints

- Design doc: `docs/superpowers/specs/2026-07-22-datalayer-sqlserver-prerequisites-design.md` (2 review rounds, verdict "safe to plan from" — this plan implements it verbatim; if anything here conflicts with that doc, the doc governs).
- **CA1725 is enforced as a build error** (`Directory.Build.props`: `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-All`, no `NoWarn` for CA1725 anywhere in the touched projects) — any task renaming an interface-implementing parameter must rename the interface and every implementation together, in one commit, or the build fails.
- **Behavior-preserving unless stated otherwise**: items 1, 3, 4, 5 must produce zero functional change — proven by existing tests staying green, not new tests asserting new behavior. Item 2 is a genuine bug fix (new tests required). Item 6 (diagnostic message) is a text-only addition.
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA comment blocks, `Shouldly` assertions.
- No `#region` blocks (CLAUDE.md standard).
- Every task ends with a commit. Run the full solution build + the relevant test project(s) before each commit — for tasks touching `IFhirRepository`, this means `dotnet build All.sln` (0 warnings, 0 errors), not just the touched project, since CA1725 failures surface at the consuming project's build.

---

## File Structure

- `src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs` — `ct` → `cancellationToken` (Task 1).
- `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs` — `ct` rename (Task 1); class-doc/DeleteAsync/BatchWriteAsync cleanup (Task 4); history cluster removed (Task 5).
- `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepository.cs` — `ct` rename (Task 1).
- `src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs` — `ct` rename (Task 1).
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ComprehensiveWorkflowDifferentialTests.cs` — named-argument call site rename (Task 1).
- `src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs` — nested-composition splice fix (Task 2).
- `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` — `RejectResourceColumnCode` message improvement (Task 6).
- New: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs` (Task 3).
- `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` — calls into the new factory instead of constructing inline (Task 3).
- New: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerHistoryQueryExecutor.cs` (Task 5).

---

### Task 1: `ct` → `cancellationToken` rename cascade

**Files:**
- Modify: `src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs` (all 12 members)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs` (21 occurrences: lines 94, 159, 229, 338, 345, 354, 461, 495, 535, 573, 610, 721, 768, 820, 849, 934, 997, 1010, 1038, 1056, 1068)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepository.cs` (18 occurrences: lines 68, 131, 210, 320, 341, 635, 654, 690, 726, 760, 794, 826, 853, 937, 993, 1039, 1108 — note: 12 implement `IFhirRepository`, the remaining 6 are private helpers using the same abbreviated name, rename all for consistency)
- Modify: `src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs` (20 occurrences: lines 64, 115, 197, 284, 313, 348, 355, 390, 397, 427, 491, 635, 692, 749, 830, 902, 979, 1095, 1130, 1139 — note the real path has a nested `FileSystem/FileSystem/` subfolder, not directly under the project root)
- Modify: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ComprehensiveWorkflowDifferentialTests.cs:55`

**Interfaces:**
- Consumes: nothing from other tasks (fully independent).
- Produces: nothing new — pure rename, no new types/signatures for later tasks to consume.

- [ ] **Step 1: Confirm the exhaustive site list before editing**

Run these greps and confirm the counts match this task's file list exactly (21/18/20 occurrences respectively, 1 test call site) before making any edit — if the counts differ from what's listed above, STOP and report the discrepancy rather than proceeding on a stale list (code may have shifted since this plan was written):

```bash
grep -n "CancellationToken ct" src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs
grep -n "CancellationToken ct" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepository.cs
grep -n "CancellationToken ct" src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs
grep -rn "ct: " src/
```

The last command (`ct: ` production-wide grep) should return zero hits — confirmed during planning that no production (non-test) call site uses the named argument `ct:`. If it returns any hit, investigate before proceeding — it means a call site this plan didn't account for exists.

- [ ] **Step 2: Rename `IFhirRepository`'s 12 members**

In `src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs`, rename every `CancellationToken ct` parameter to `CancellationToken cancellationToken` (preserving `= default` where present), and rename every `<param name="ct">` XML doc tag to `<param name="cancellationToken">` to match. This file has 12 members total; every one uses `ct` today (confirmed during planning) — rename all of them in this single step, not incrementally, since a partial rename here breaks the build immediately on any implementation that hasn't caught up yet (which is every implementation, until Step 3).

- [ ] **Step 3: Rename all 3 implementations and the test call site, in the same commit**

In `SqlServerFhirRepository.cs`, `SqlEntityFrameworkRepository.cs`, and `FileBasedFhirRepository.cs`: rename every `CancellationToken ct` (both the parameter declarations at the listed line numbers and every usage of the bare identifier `ct` inside each method body) to `CancellationToken cancellationToken`/`cancellationToken`. This is a pure token substitution — do not change any logic. Several listed lines are continuation lines of multi-line signatures (e.g. `SqlServerFhirRepository.cs:229,354,461,849,934,1010`) where the parameter name is the only token on that line — confirm each edit lands on the correct line by reading surrounding context, not by blind line-number replacement, since line numbers may have drifted by the time this task executes (re-run Step 1's greps immediately before editing each file if more than a few tasks have landed since this plan was written).

In `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ComprehensiveWorkflowDifferentialTests.cs`, change the named argument at line 55 from `ct: CancellationToken.None` to `cancellationToken: CancellationToken.None`.

- [ ] **Step 4: Build the full solution to verify the rename is complete**

Run: `dotnet build All.sln`
Expected: `0 Warning(s)`, `0 Error(s)`. If CA1725 (or any other error) fires, it means a `ct` occurrence was missed somewhere outside this task's file list — find it via `grep -rn "CancellationToken ct" src/ test/` and fix it, even if it's in a file this task didn't originally list (the build error is the authoritative signal of completeness here, not the pre-enumerated list).

- [ ] **Step 5: Run the affected test suites**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj` (unset `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT` first if a CS8034 analyzer-architecture error occurs — a known environment quirk in this repo, unrelated to this change).
Expected: same pass count as before this task (pure rename, zero behavior change — confirm the count against a baseline run if one isn't already known).

- [ ] **Step 6: Commit**

```bash
git add src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepository.cs src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ComprehensiveWorkflowDifferentialTests.cs
git commit -m "refactor(domain): rename ct to cancellationToken across IFhirRepository and its implementations"
```

---

### Task 2: `SearchCompartmentHandler` nested-composition fix

**Files:**
- Modify: `src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs:83-85`
- Test: `test/Ignixa.Application.Tests/Features/Compartment/SearchCompartmentHandlerTests.cs` (new file — no existing test file for this class was found during planning)
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (add to existing file)

**Interfaces:**
- Consumes: nothing from Task 1 (independent).
- Produces: nothing new for later tasks.

- [ ] **Step 1: Re-confirm the current composition code**

Re-read `src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs` around lines 83-85. As of plan-writing time it reads:

```csharp
Expression finalExpression = request.SearchOptions.Expression != null
    ? Expression.And(compartmentExpression, request.SearchOptions.Expression)
    : compartmentExpression;
```

Confirm this is still accurate before editing (re-run if drifted).

- [ ] **Step 2: Write the failing Application-layer test**

Create `test/Ignixa.Application.Tests/Features/Compartment/SearchCompartmentHandlerTests.cs`. Since no existing test file for this class exists, this test constructs the minimal request/handler shape directly — read `SearchCompartmentHandler.cs`'s full constructor and `HandleAsync` (or equivalent) signature first (re-read the file in full, not just lines 83-85) to get the exact real dependencies to construct/fake, and read an existing `Ignixa.Application.Tests` test for a sibling handler (grep for other `*HandlerTests.cs` files in `test/Ignixa.Application.Tests/Features/`) to match this project's established handler-test construction style (likely NSubstitute fakes for injected services, per this codebase's established mocking library).

```csharp
[Fact]
public void GivenACompartmentSearchWithMultipleOrdinaryParameters_WhenComposed_ThenSplicesIntoTheExistingAndInsteadOfNesting()
{
    // Arrange -- construct a SearchOptions.Expression that is already a flat And of 2 ordinary
    // params (mirroring GET /Patient/123/Observation?_id=X&category=lab), matching this test
    // file's or a sibling handler-test's real construction pattern for SearchOptions/request
    // objects -- fill in the exact real types/fakes once Step 1's full-file re-read is done.

    // Act
    // (call the same composition logic SearchCompartmentHandler.cs:83-85 uses -- if it's
    // private/inline, extract it to a small internal/testable method first, or test through
    // whatever the handler's real public entry point is if that's more natural given the real
    // class shape)

    // Assert -- the composed Expression is a SINGLE flat MultiaryExpression{And} containing 3
    // children (compartment + the 2 original params), NOT a MultiaryExpression{And} containing
    // 2 children where one child is itself a nested MultiaryExpression{And}. Assert this by
    // inspecting the composed expression's real shape (cast to MultiaryExpression, check
    // .Expressions.Count == 3 and that none of the 3 children is itself a MultiaryExpression
    // with the same And operator), not by string/ToString comparison.
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchCompartmentHandlerTests"`
Expected: FAIL — the current code always wraps a new 2-child `And`, producing 2 children (compartment, nested-And) not 3 flat children.

- [ ] **Step 4: Implement the splice fix**

Modify `SearchCompartmentHandler.cs:83-85`:

```csharp
Expression finalExpression = request.SearchOptions.Expression switch
{
    null => compartmentExpression,
    MultiaryExpression { MultiaryOperation: MultiaryOperator.And } existingAnd =>
        Expression.And([compartmentExpression, .. existingAnd.Expressions]),
    var other => Expression.And(compartmentExpression, other),
};
```

Confirm `MultiaryExpression`'s real property name is `MultiaryOperation` (not `Operator` or similar) by re-reading `src/Core/Ignixa.Search/Expressions/MultiaryExpression.cs` before finalizing this pattern match — the property name was confirmed during planning but re-verify against the live file. Confirm `Expression.And(IReadOnlyList<Expression>)` (the list-taking overload, `src/Core/Ignixa.Search/Expressions/Expression.cs:52-65` as of plan-writing) is the correct overload for the collection-expression spread syntax used above.

- [ ] **Step 5: Run the Application-layer test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchCompartmentHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Write and pass the compiler-side end-to-end proof**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (read an existing compartment end-to-end test, e.g. around lines 1133-1168 as of plan-writing, and an existing `_id`-nested-in-And test, e.g. around lines 523-549, for the exact construction/`Explain()`-pinning patterns to combine):

```csharp
[Fact]
public void GivenACompartmentSearchWithANestedResourceColumnPredicate_WhenComposedByTheHandlerAndCompiled_ThenLowersSuccessfully()
{
    // Arrange -- build the SAME flat, spliced expression shape Task 2's fix now produces
    // (compartment + _id + an ordinary predicate, all as direct children of ONE top-level And),
    // matching the real construction pattern from the two reference tests cited above.

    // Act
    // Resolve.RunAsync(...) then Lower.Run(...), per this file's established pattern.

    // Assert -- compiles successfully (no NotSupportedException), and the resulting plan's
    // Explain() output is pinned exactly, proving the compartment membership, the _id
    // resource-column predicate (correctly extracted into OuterPredicate), and the ordinary
    // predicate are all present and correctly composed -- not that "no exception was thrown"
    // alone, which per this project's own documented recurring bug class could pass even if a
    // predicate were silently dropped.
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~GivenACompartmentSearchWithANestedResourceColumnPredicate"` then the full `Ignixa.Search.Sql.Tests` and `Ignixa.Application.Tests` suites with no filter, to confirm zero regressions.
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs test/Ignixa.Application.Tests/Features/Compartment/SearchCompartmentHandlerTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "fix(application): splice compartment expression into an existing And instead of nesting"
```

---

### Task 3: Composition-root relocation

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` (lines ~350-404, inside `CreateServiceFactory`)

**Interfaces:**
- Consumes: nothing from Tasks 1-2 (independent — this task's changes are in an entirely different area of the codebase).
- Produces: `SqlServerRepositoryFactory` — a new public class other sub-projects (specifically sub-project 3's search adapter) may later extend with a search-service construction method, though that's explicitly out of scope here.

- [ ] **Step 1: Re-confirm the current construction code**

Re-read `SqlEntityFrameworkRepositoryFactory.cs`'s `CreateServiceFactory` method (~lines 271-488) in full, focusing on the SqlServer-relevant portion (~lines 350-404). As of plan-writing time:

```csharp
#pragma warning disable CA2000 // Dispose objects before losing scope
        var sqlServerSearchIndexCache = new SqlServerSearchIndexReferenceDataCache(
            _sqlExecutionService,
            tenantId,
            _loggerFactory.CreateLogger<SqlServerSearchIndexReferenceDataCache>());
#pragma warning restore CA2000
        sqlServerSearchIndexCache.PreloadResourceTypesAsync(CancellationToken.None).GetAwaiter().GetResult();
        sqlServerSearchIndexCache.PreloadSearchParamsAsync(maxRows: null, CancellationToken.None).GetAwaiter().GetResult();

        Func<FhirDbContext, IFhirRepository> createRepository = (_) =>
        {
            var compressor = new Ignixa.DataLayer.SqlServer.Compression.GzipResourceCompressor(_memoryStreamManager);

            var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
                _sqlExecutionService, tenantId, _loggerFactory.CreateLogger<SqlServerPostMergeExtensionUpdater>());

            var sqlServerMergeRepository = new SqlServerMergeRepository(
                _sqlExecutionService, tenantId, compressor, sqlServerSearchIndexCache, extensionUpdater,
                _loggerFactory.CreateLogger<SqlServerMergeRepository>());

            return new SqlServerFhirRepository(
                _sqlExecutionService, tenantId, compressor, sqlServerSearchIndexCache, sqlServerMergeRepository,
                _loggerFactory.CreateLogger<SqlServerFhirRepository>());
        };
```

Confirm this still matches before editing. Note line 382's `GzipResourceCompressor` is fully-qualified `Ignixa.DataLayer.SqlServer.Compression.GzipResourceCompressor` — this is the ONLY compressor this task touches. A different, unrelated, same-named `Ignixa.DataLayer.SqlEntityFramework.Compression.GzipResourceCompressor` is constructed elsewhere in this same file (inside `createSearchService`, ~line 409, for the unrelated EF read path) — do not touch that construction site or import that namespace into the new factory.

- [ ] **Step 2: Create `SqlServerRepositoryFactory`, preserving the exact two-scope construction split**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs`:

```csharp
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Composition root for the SqlServer-native write path, relocated here from
/// Ignixa.DataLayer.SqlEntityFramework's SqlEntityFrameworkRepositoryFactory (which now calls
/// into this class instead of constructing these types inline). Preserves the original's
/// two-scope construction split exactly: <see cref="CreateReferenceDataCache"/> is called ONCE
/// PER TENANT (outside any per-request scope), immediately followed by both eager preloads;
/// <see cref="CreateRepository"/> is called PER REQUEST, reusing the tenant-scoped cache passed
/// in. Flattening these into one per-request call would change the cache's cardinality and
/// re-run both preloads on every repository creation -- a real, silent behavior/performance
/// regression, not a refactor-neutral change.
/// </summary>
public static class SqlServerRepositoryFactory
{
    public static async Task<SqlServerSearchIndexReferenceDataCache> CreateReferenceDataCacheAsync(
        ISqlExecutionService sqlExecutionService,
        int tenantId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var cache = new SqlServerSearchIndexReferenceDataCache(
            sqlExecutionService,
            tenantId,
            loggerFactory.CreateLogger<SqlServerSearchIndexReferenceDataCache>());

        await cache.PreloadResourceTypesAsync(cancellationToken);
        await cache.PreloadSearchParamsAsync(maxRows: null, cancellationToken);

        return cache;
    }

    public static IFhirRepository CreateRepository(
        ISqlExecutionService sqlExecutionService,
        int tenantId,
        SqlServerSearchIndexReferenceDataCache cache,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILoggerFactory loggerFactory)
    {
        var compressor = new GzipResourceCompressor(memoryStreamManager);

        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            sqlExecutionService, tenantId, loggerFactory.CreateLogger<SqlServerPostMergeExtensionUpdater>());

        var mergeRepository = new SqlServerMergeRepository(
            sqlExecutionService, tenantId, compressor, cache, extensionUpdater,
            loggerFactory.CreateLogger<SqlServerMergeRepository>());

        return new SqlServerFhirRepository(
            sqlExecutionService, tenantId, compressor, cache, mergeRepository,
            loggerFactory.CreateLogger<SqlServerFhirRepository>());
    }
}
```

The async version of the cache-creation step (using real `await` instead of `GetAwaiter().GetResult()`) is a deliberate, narrow improvement over the original's sync-over-async pattern — confirm this doesn't change observable behavior in the synchronous call site it replaces (Step 3 below still calls `.GetAwaiter().GetResult()` at the EF factory's own call site, since that method is not being made async by this task — only the new factory method's own signature changes to genuinely async, matching this codebase's general preference for real `async`/`await` over sync-over-async wrapping, without forcing an unrelated async-signature change onto `CreateServiceFactory` itself, which is out of this task's scope).

- [ ] **Step 3: Update `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` to call the new factory**

Replace the block from Step 1 with:

```csharp
        var sqlServerSearchIndexCache = SqlServerRepositoryFactory
            .CreateReferenceDataCacheAsync(_sqlExecutionService, tenantId, _loggerFactory, CancellationToken.None)
            .GetAwaiter().GetResult();

        Func<FhirDbContext, IFhirRepository> createRepository = (_) =>
            SqlServerRepositoryFactory.CreateRepository(
                _sqlExecutionService, tenantId, sqlServerSearchIndexCache, _memoryStreamManager, _loggerFactory);
```

Add `using Ignixa.DataLayer.SqlServer;` to this file's usings if not already present (it already references `Ignixa.DataLayer.SqlServer.*` types directly, so the project reference exists — only the using directive may need adding, confirm against the real current usings list).

- [ ] **Step 4: Decide `TestTenantDatabase.cs` — explicit, scoped decision, not silent**

`test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/TestTenantDatabase.cs`'s `CreateSqlServerFhirRepositoryAsync` (~lines 125-142) does its own separate inline construction of the same 5 objects, and was found during planning to already call only `PreloadResourceTypesAsync` — NOT `PreloadSearchParamsAsync` — an existing, pre-existing divergence from production, unrelated to this task. **Do not modify this file in this task.** Leave it as its own independent construction: migrating it to call `SqlServerRepositoryFactory` would either (a) preserve its existing missing-preload divergence through an extra layer of indirection for no behavioral gain, or (b) silently add the missing preload as a side effect, which is a real behavior change to a widely-used test fixture that needs its own justification and review, not something to bundle into a "pure relocation" task. This is a deliberate scope boundary, not an oversight — if a future task wants to reconcile the fixture with production behavior, it should do so explicitly and separately.

- [ ] **Step 5: Build and run the full integration suite**

Run: `dotnet build All.sln` — expect 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`
Expected: same pass count as before this task — this is the regression proof for a pure relocation (the test suite doesn't go through `SqlEntityFrameworkRepositoryFactory` at all per Step 4's decision, so this mainly proves the new factory compiles and nothing else broke; if there's a test that DOES exercise `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` directly, find and run it specifically too).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
git commit -m "refactor(datalayer-sqlserver): relocate composition root from SqlEntityFrameworkRepositoryFactory"
```

---

### Task 4: `SqlServerFhirRepository.cs` cleanup — comment diet + `BuildResourceWrappers`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs` (class doc ~17-53, `DeleteAsync`'s essay ~258-285, `BatchWriteAsync` ~351-456)

**Interfaces:**
- Consumes: nothing from Tasks 1-3 (independent — Task 1 renamed `ct` in this file already, so re-read the file fresh before this task to get post-Task-1 line numbers; this task doesn't touch the constructor or any method Task 3 depends on).
- Produces: `BuildResourceWrappers` — a new private method other tasks don't consume, but keep its name stable in case a later sub-project references it.

- [ ] **Step 1: Re-read the current file (post Task 1's rename) for exact line numbers**

Re-read `SqlServerFhirRepository.cs` in full. Task 1 renamed `ct`→`cancellationToken` throughout, which shifts some lines slightly (longer parameter names may wrap differently) — get the CURRENT exact line numbers for the class doc, `DeleteAsync`, and `BatchWriteAsync` before editing; do not trust this plan's pre-Task-1 line numbers literally.

- [ ] **Step 2: Condense the class doc comment**

Replace the ~37-line Phase-D changelog class doc (as of plan-writing, lines 17-53) with:

```csharp
/// <summary>
/// Raw-ADO.NET port of <c>SqlEntityFrameworkRepository</c> (Ignixa.DataLayer.SqlEntityFramework)
/// against the same legacy fhir-server schema, using <see cref="ISqlExecutionService"/> instead of
/// EF Core. Delegates bulk/index writes to <see cref="SqlServerMergeRepository"/>, history queries
/// to <see cref="SqlServerHistoryQueryExecutor"/>. Full port history and task-by-task rationale:
/// <c>docs/superpowers/plans/2026-07-20-ignixa-datalayer-sqlserver-phase-d.md</c>.
/// </summary>
```

(This references `SqlServerHistoryQueryExecutor`, which doesn't exist until Task 5 — this is fine, the doc comment is prose, not a compiled reference; if Task 4 runs before Task 5 in execution order, the sentence is still accurate as a forward statement of the file's near-term shape. If this bothers a reviewer, the sentence can be adjusted to drop the `SqlServerHistoryQueryExecutor` mention and only note it once Task 5 lands — controller's call at review time, not a blocking issue.)

- [ ] **Step 3: Condense `DeleteAsync`'s legacy-divergence essay**

Replace the ~28-line essay comment inside/around `DeleteAsync` with a few lines pointing at its pinning test:

```csharp
// Deliberate divergence from the legacy EF port: this method never allocates a transactionId
// (no transaction-scoped delete), matching the documented semantics pinned directly by
// SqlServerFhirRepositoryCrudTests -- see that file for the exact behavioral contract this
// comment used to restate in full.
```

Re-read the original essay first to confirm the condensed version doesn't drop any claim that ISN'T also captured by the pinning test it references — if the original comment states something the test doesn't actually pin, keep that specific detail rather than dropping it silently.

- [ ] **Step 4: Extract `BuildResourceWrappers` from `BatchWriteAsync`**

Re-read `BatchWriteAsync`'s full current body (the inline wrapper-building/validation loop, ~lines 383-431 as of plan-writing: `foreach` over `operations`, resolving `resourceTypeId`, validating version/surrogate-id constraints, building `ResourceWrapper`, appending to a list). Extract this loop's body into a new private method:

```csharp
private async Task<IReadOnlyList<ResourceWrapper>> BuildResourceWrappersAsync(
    IReadOnlyList<...> operations, // exact parameter type = whatever BatchWriteAsync's real loop iterates over -- read the real signature first
    CancellationToken cancellationToken)
{
    // exact body moved from BatchWriteAsync's inline loop, unchanged logic
}
```

Read the ACTUAL current loop body before writing this method — do not guess its exact contents or parameter types from this plan's paraphrase; the goal is a pure extraction (move the code, call the new method from `BatchWriteAsync`, zero logic change), not a rewrite.

- [ ] **Step 5: Build and run tests**

Run: `dotnet build All.sln` — 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`
Expected: same pass count as before this task (pure refactor — comment changes and a method extraction, zero logic change).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs
git commit -m "refactor(datalayer-sqlserver): condense SqlServerFhirRepository comments, extract BuildResourceWrappers"
```

---

### Task 5: `SqlServerFhirRepository.cs` cleanup — extract `SqlServerHistoryQueryExecutor`

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerHistoryQueryExecutor.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs` (removes the history cluster, ~lines 606-712 pre-Task-4; 3 public history methods become thin delegators)
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerHistoryQueryExecutorTests.cs` (new file)

**Interfaces:**
- Consumes: nothing from Tasks 1-4 that changes this task's shape — confirmed during planning that the history cluster only touches `_sqlExecutionService`/`_tenantId`/`_compressor`/`_logger`, all already available on `SqlServerFhirRepository`'s existing primary constructor, so `SqlServerHistoryQueryExecutor` is constructed internally with **no constructor signature change** to `SqlServerFhirRepository` — this task is independent of Task 3's factory relocation (which never needs to know about this internal restructuring).
- Produces: `SqlServerHistoryQueryExecutor` — a new public class.

- [ ] **Step 1: Re-read the current file (post Tasks 1 and 4) for exact line numbers and content**

Re-read `SqlServerFhirRepository.cs` in full. Get current exact line numbers for: the primary constructor/fields, the 3 public history methods (`GetResourceHistoryAsync`/`GetTypeHistoryAsync`/`GetSystemHistoryAsync`), and the history cluster (`ExecuteHistoryQueryAsync`/`BuildHistorySql`/`AddSharedHistoryParameters`/`TryMapHistoryRow`/`ReadHistoryRow`/`HistoryRow`).

- [ ] **Step 2: Write the failing test for the new executor**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerHistoryQueryExecutorTests.cs`. Read `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryHistoryTests.cs` in full first (the existing history test file, using `TestTenantDatabase.CreateSqlServerFhirRepositoryAsync()` and exercising `_repository.GetResourceHistoryAsync(...)` through the public delegator) — the new test file constructs `SqlServerHistoryQueryExecutor` directly from the same fixture's underlying pieces (`_database.SqlExecutionService`, `_database.TenantId`, a compressor, a `NullLogger<SqlServerHistoryQueryExecutor>`) rather than going through the repository, proving the extracted class works standalone:

```csharp
public class SqlServerHistoryQueryExecutorTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerHistoryQueryExecutor _executor = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _executor = new SqlServerHistoryQueryExecutor(
            _database.SqlExecutionService,
            _database.TenantId,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger<SqlServerHistoryQueryExecutor>.Instance);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenAResourceWithHistory_WhenQueriedDirectlyThroughTheExecutor_ThenReturnsTheExpectedEntries()
    {
        // Arrange -- mirror SqlServerFhirRepositoryHistoryTests.cs's real setup pattern for
        // creating a resource with multiple versions (read that file's Arrange section and reuse
        // its exact real helper calls/fixture methods).

        // Act
        // var results = await _executor.ExecuteHistoryQueryAsync(...); -- exact real method
        // signature confirmed in Step 1's re-read.

        // Assert
        // matches SqlServerFhirRepositoryHistoryTests.cs's existing assertion style for the
        // equivalent through-the-repository test.
    }
}
```

Confirm `TestTenantDatabase`'s real property names (`SqlExecutionService`, `TenantId`) via Step 1-equivalent re-read of `TestTenantDatabase.cs` before finalizing this test — this plan's earlier research confirmed these exist but re-verify exact casing/names.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerHistoryQueryExecutorTests"`
Expected: FAIL — `SqlServerHistoryQueryExecutor` doesn't exist yet.

- [ ] **Step 4: Extract `SqlServerHistoryQueryExecutor`**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerHistoryQueryExecutor.cs`, moving the ENTIRE history cluster's real current content (read verbatim from Step 1's re-read, do not paraphrase) into a new class matching the sibling collaborator convention (`SqlServerMergeRepository`/`SqlServerPostMergeExtensionUpdater`'s file-scoped namespace, primary-constructor style, one file per class):

```csharp
namespace Ignixa.DataLayer.SqlServer;

public class SqlServerHistoryQueryExecutor(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    ILogger<SqlServerHistoryQueryExecutor> logger)
{
    // Move ExecuteHistoryQueryAsync, BuildHistorySql, AddSharedHistoryParameters,
    // TryMapHistoryRow, ReadHistoryRow, and the HistoryRow record here verbatim from
    // SqlServerFhirRepository.cs, changing only field-access syntax (_sqlExecutionService ->
    // sqlExecutionService via a private readonly field this class now owns, matching the same
    // ArgumentNullException.ThrowIfNull-in-constructor pattern SqlServerFhirRepository's own
    // primary constructor already uses for its fields -- read that pattern from
    // SqlServerFhirRepository.cs's constructor body and mirror it here exactly) -- zero logic
    // change to the SQL text, parameter binding, or row-mapping behavior.
}
```

Add whatever `using` statements the moved code needs (confirmed during planning: at minimum `System.Data`, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.Logging`, `Ignixa.DataLayer.SqlServer.Compression` — verify against the actual moved code's real type references, don't guess).

In `SqlServerFhirRepository.cs`: remove the moved cluster; add a private field `private readonly SqlServerHistoryQueryExecutor _historyExecutor = new(sqlExecutionService, tenantId, compressor, logger);` (constructed internally from the class's own existing primary-constructor parameters — no constructor signature change); change the 3 public history methods to thin delegators that resolve the resource-type ID (unchanged logic, stays on the repository) then call `_historyExecutor`'s equivalent method.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`
Expected: PASS, including both the new `SqlServerHistoryQueryExecutorTests` and the pre-existing `SqlServerFhirRepositoryHistoryTests` (proving the thin delegators preserve identical public behavior) — same total pass count as before this task, plus the new tests.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerHistoryQueryExecutor.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerHistoryQueryExecutorTests.cs
git commit -m "refactor(datalayer-sqlserver): extract SqlServerHistoryQueryExecutor collaborator"
```

---

### Task 6: Diagnostic message improvement

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` (`RejectResourceColumnCode`, confirmed at line 45 as of plan-writing, called from 3 sites: lines 45, 58, 71)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/StructuralContextTests.cs` (add to existing file if present, else find the appropriate existing test file exercising this guard, e.g. via `LowerTests.cs` or `EndToEndCompilationTests.cs`)

**Interfaces:**
- Consumes: nothing from other tasks (fully independent — different project, `Ignixa.Search.Sql`, not `Ignixa.DataLayer.SqlServer`).
- Produces: nothing new.

- [ ] **Step 1: Re-read the current message text**

Re-read `RejectResourceColumnCode` in `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` (confirmed at line 126-137 as of plan-writing, called from lines 45/58/71). Confirm the exact current message text before editing.

- [ ] **Step 2: Write the failing test for the improved message**

Find or create a test asserting the exact new message text. If an existing test already asserts `Should.Throw<NotSupportedException>()` without checking the message (per this project's own established review pattern of catching assertion-looseness), either strengthen that existing test or add a new one:

```csharp
[Fact]
public void GivenAResourceColumnPredicateNestedInsideAnAnd_WhenLowered_ThenTheExceptionNamesTheLikelyCause()
{
    // Arrange -- construct the exact scenario RejectResourceColumnCode's guard fires for
    // (a resource-column predicate like _id nested one level inside an And it doesn't scan into
    // -- reuse Task 2's compiler-side test construction pattern if that task already landed, or
    // build the minimal standalone case directly).

    // Act & Assert
    var ex = Should.Throw<NotSupportedException>(() => /* the lowering call that fires the guard */);
    ex.Message.ShouldContain("nested"); // exact substring to assert once Step 3's real message text is finalized -- update this to match verbatim
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~TheExceptionNamesTheLikelyCause"`
Expected: FAIL — the current message doesn't yet contain the new root-cause sentence.

- [ ] **Step 4: Add the root-cause sentence**

Append to the existing message in `RejectResourceColumnCode` (read the real current message string first, then append — do not replace the existing, already-correct explanation of the guard's structural purpose):

```
" This commonly happens when a resource-column predicate arrives nested inside an And/Or that " +
"wasn't flattened before reaching Lower.Run -- e.g. a caller composing And(otherExpression, " +
"existingAnd) instead of splicing into existingAnd's own children. Flatten the composed " +
"expression before calling Lower."
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` (full suite, no filter, both `net9.0` and `net10.0` — unset `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT` first if net10.0 hits CS8034).
Expected: PASS, zero regressions — confirmed during planning that no existing test asserts this message's text verbatim, so appending is safe.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs test/Ignixa.Search.Sql.Tests/Lowering/StructuralContextTests.cs
git commit -m "fix(search-sql): name the likely root cause in RejectResourceColumnCode's exception message"
```

(Adjust the test file path in this `git add` to wherever Step 2 actually placed the test, if different from the guessed path.)

---

## Post-Plan

After all 6 tasks: dispatch the final whole-branch review (most capable model available, per this initiative's standing practice) covering the full diff from this plan's base commit to its tip. Update the roadmap/ledger to record this sub-project's completion. This sub-project's completion unblocks sub-project 3 (the SqlServer-native search adapter), which was blocked on both sub-projects 1 (compiler feature-parity, already complete) and this one.
