# Sub-project 3: SqlServer-Native Search Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `Ignixa.Search.Sql`'s compiler into `Ignixa.DataLayer.SqlServer` as a new `ISearchService` implementation (`SqlServerCompiledSearchService`), proven clean against a differential harness, then hard-cut-over as the only search path for SqlServer-storage tenants — no feature flag.

**Architecture:** A new `Ignixa.DataLayer.SqlServer/Search/` folder holds `SqlServerCompiledSearchService` (driving the compiler's Resolve→Lower→Emit pipeline via a new pre-built-`SearchOptions` `SearchCompiler` entry point) and `SqlServerSymbolResolver` (a read-only `ISymbolResolver` over the existing tenant-scoped reference-data cache). Two small, real `Ignixa.Search.Sql` feature additions (offset-based paging, an `OuterPredicate` surrogate-ID range filter) unblock parity with the legacy EF search path's pagination and export-partitioning behavior. `Ignixa.DataLayer.SqlEntityFramework`'s search path is left untouched throughout, as reference implementation and rollback lever.

**Tech Stack:** C#/.NET 10, raw ADO.NET (`Microsoft.Data.SqlClient`) via `ISqlExecutionService`, the `Ignixa.Search.Sql` compiler (Build/Resolve/Lower/Emit), xUnit + Shouldly.

## ⚠️ Task 1 is DONE — read this before dispatching Task 2

**Task 1 (branch reconciliation with `origin/main`/PR #353) has already been executed, outside the normal subagent-driven-development flow, because it needed to happen before this plan's Fable review could run against accurate line numbers.** A dedicated agent performed a real `git rebase origin/main` (the user explicitly chose rebase over the merge this task originally proposed — see below), resolved the 2 predicted signature conflicts plus a small number of others git actually surfaced, verified with the full test suite, and force-pushed. Branch tip is now `2262a578`. **Do not dispatch a Task 1 implementer** — proceed straight to Task 2's implementer, but treat this section as the record of what Task 1 actually produced, since every later task's literal code must still be re-verified against this real result (not the plan's original prediction) per each task's own re-verification step.

**What actually happened (differs from the merge-based plan below in mechanism, not outcome):**
- **Rebase, not merge** — the user explicitly chose a true rebase after being told it requires a force-push; this section's Step 2 (`git merge origin/main`) was superseded by that decision. `origin/main` is now this branch's effective base (`eead718e`); the branch tip is `2262a578` (117 commits replayed, including one post-rebase fixup commit).
- **Only 2 conflict stops actually occurred**, not the ~11 predicted from commit-count analysis (most of the 18 flagged files auto-merged cleanly via 3-way merge):
  - **Stop 1** (commit `5d3c4b9c`, system-level search): `Lowering/Lower.cs` resolved exactly per this section's Step 2a below (both `systemLevelSearch` and `approximationReferenceTime` kept, in that order). `Lowering/Leaf/LeafLoweringDispatcher.cs` was a pure textual overlap (origin/main's new XML doc `<remarks>` plus this branch's nullable `short? resourceTypeId`), not a real logic conflict. `Lowering/Leaf/UriLoweringRule.cs` was a **real, substantive conflict beyond what this plan predicted**: at the point of commit `5d3c4b9c`, this branch still had the old plain-equality-only URI stub (pre-dating PR #353's `:above`/`:below` hierarchical matching work); resolved by keeping origin/main's full hierarchical implementation and changing only its `resourceTypeId` parameter from `short` to `short?` to carry this branch's system-level-search capability forward.
  - **Stop 2** (commit `9a55ea3f`, system-level-search tests): `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` — both sides had independently appended new `[Fact]`s after the same shared test, git's diff3 merge muddied the markers around a byte-identical shared tail; reconstructed cleanly by diffing the raw `:2:`/`:3:` blobs directly. Both sides' new tests are present (origin/main's ~17 terminology/quantity/composite/URI-hierarchy/reference/string/number/date-`:ap` tests, then this branch's 9 system-level-search tests).
  - **`ISymbolResolver` (Step 2b below) needed no separate conflict-resolution stop** — it merged cleanly as a pure addition, exactly as predicted.
- **One post-rebase build fixup, not a merge conflict** (commit `2262a578`): `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/SqlEntityFrameworkSymbolResolverTests.cs` (from PR #353) referenced `DatabaseInitializer`, a class this branch's own Phase D work had already retired in favor of `TestSchemaInitializer.InitializeAsync`. The 4 affected tests (all `[Fact(Skip = "Manual integration test...")]`, not part of CI) were updated to the current pattern.
- **Verification, exceeding this task's own Step 5 bar**: `Ignixa.Search.Sql.Tests` 524/524 on both net9.0 and net10.0 (both PR #353's and this branch's feature work intact); `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` clean except 2 pre-existing, rebase-unrelated environment failures (`Ignixa.RepoGuards.Tests`: worktree `.git`-is-a-file vs. `RepoRoot.Find()` expecting a directory; `Ignixa.SqlOnFhir.Tests`: uninitialized git submodule, already showing in `git status` before this task); `Ignixa.DataLayer.SqlServer.IntegrationTests` 75/75 against a real local SQL Server (a stronger check than this task's own plan called for, run because SQL Server was available).

The remainder of this Task 1 section is preserved below as the historical record of the reconciliation's planned approach and the exact `Lower.Run`/`ISymbolResolver` resolutions that were, in fact, applied — useful context for later tasks, not a to-do list.

## Global Constraints

- `TreatWarningsAsErrors=true` + `AnalysisLevel=latest-All` (`Directory.Build.props`) — CA-series warnings are build errors (e.g. CA1725 for parameter-name mismatches against interface declarations).
- Environment quirk: `dotnet build`/`dotnet test` for net10.0 targets fails with CS8034 (analyzer architecture mismatch) unless `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT` env vars are unset first. PowerShell: `Remove-Item Env:\Platform,Env:\__DOTNET_PREFERRED_BITNESS,Env:\__DOTNET_ADD_32BIT -ErrorAction SilentlyContinue`. Known harmless, not a code defect — every task hits this before any `dotnet` command.
- `CancellationToken cancellationToken` naming (never `ct`) on every new or renamed member.
- AAA/Shouldly test pattern, `GivenContext_WhenAction_ThenResult` naming, no inline comments unless the WHY is non-obvious, file-scoped namespaces, primary constructors, one type per file.
- Every intermediate task (all but the final cutover task) must leave `SqlEntityFrameworkSearchService`/the write path byte-for-byte unaffected and the full existing test suite green — this plan adds new behavior, but nothing existing may regress before the explicit, final cutover flips the switch.
- A real local SQL Server instance is available for integration tests (`sqlcmd -S localhost -E`, or `TEST_SQL_CONNECTION_STRING` with `Database=`/`Initial Catalog=` plus `SqlServer__AutomaticSchemaDeploymentEnabled=true`).
- Design doc: `docs/superpowers/specs/2026-07-22-datalayer-sqlserver-search-adapter-design.md` (3 Fable review rounds, "safe to plan from"). Every task below cites the design doc section it implements.

---

### Task 1: Reconcile branch with `origin/main` (PR #353)

**Files:**
- Merge or rebase the entire `worktree-ignixa-datalayer-sqlserver` branch onto `origin/main`. Conflict surface is confined to `src/Core/Ignixa.Search.Sql/` and `src/Core/Ignixa.Search.Sql.Generators/` (verified: no conflicts expected outside these two projects, since PR #353 touched only `Ignixa.Search.Sql`/`Ignixa.Search`/a handful of `Ignixa.DataLayer.SqlEntityFramework` call sites this branch never touched).
- The 18 files independently modified by both sides (real conflict candidates, confirmed via `git diff --stat` against the merge-base `b4aa4295` on both branches): `Ast/PlanExplainer.cs`, `Builders/SqlBuilder.cs`, `Lowering/Composite/CompositeLoweringDispatcher.cs`, `Lowering/Composite/ReferenceTokenLoweringRule.cs`, `Lowering/Composite/TokenDateTimeLoweringRule.cs`, `Lowering/Composite/TokenNumberNumberLoweringRule.cs`, `Lowering/Composite/TokenQuantityLoweringRule.cs`, `Lowering/Composite/TokenStringLoweringRule.cs`, `Lowering/Composite/TokenTokenLoweringRule.cs`, `Lowering/Leaf/LeafLoweringDispatcher.cs`, `Lowering/Leaf/QuantityLoweringRule.cs`, `Lowering/Leaf/ReferenceLoweringRule.cs`, `Lowering/Leaf/StringLoweringRule.cs`, `Lowering/Leaf/TokenLoweringRule.cs`, `Lowering/Leaf/UriLoweringRule.cs`, `Lowering/Lower.cs`, `Lowering/StructuralContext.cs`, `Symbols/SymbolCollectingVisitor.cs`.

**Interfaces:**
- Consumes: nothing from earlier tasks (this is the first task).
- Produces: a reconciled `Ignixa.Search.Sql` where BOTH capability sets are present and tested — `origin/main`'s token/quantity/URI/string/`:ap` feature work (PR #353) AND this branch's sort/include/compartment/`$everything`/system-level-search feature work (Phases 1-9). Every later task in this plan assumes this reconciled state exists and is green.

- [x] **Step 1: Confirm the exact merge-base and conflict scope**

```bash
git fetch origin main
git merge-base HEAD origin/main
git diff --stat $(git merge-base HEAD origin/main) HEAD -- src/Core/Ignixa.Search.Sql src/Core/Ignixa.Search.Sql.Generators
git diff --stat $(git merge-base HEAD origin/main) origin/main -- src/Core/Ignixa.Search.Sql
```

Confirm the merge-base is `b4aa4295...` (or whatever it now is, if origin/main has moved further — re-run `git fetch` first) and that the file lists above still match. If origin/main has moved significantly further since this plan was written, re-derive the conflict list rather than trusting these citations blindly.

- [x] **Step 2: Merge (not rebase) `origin/main` into this branch**

```bash
git merge origin/main
```

A merge, not a rebase, is deliberate: this branch has already been pushed to `origin/worktree-ignixa-datalayer-sqlserver` and shared across multiple prior sub-projects' work (sub-project 1 and sub-project 2's commits are on this branch) — rewriting that history with a rebase risks breaking anyone/anything that has already fetched it, and this initiative's own established practice elsewhere in this session has been to merge/rebase deliberately and explain the choice, never silently. A merge commit here is the safe default; if a rebase is genuinely preferred, that's a call for the user to make explicitly before this task starts, not something to decide unilaterally mid-task.

Git will report conflicts in the 18 files listed above (and possibly others if origin/main moved since Step 1). Do not use `-X ours`/`-X theirs` or any other blanket auto-resolution — every conflict in this list needs a real, understood resolution that preserves both sides' capability, not a coin flip.

- [x] **Step 2a: Resolve `Lower.cs`'s signature conflict specifically**

The two `Lower.Run` signatures that conflict:

This branch (pre-merge):
```csharp
public static LoweredPlan Run(
    Expression? expression,
    SymbolTable symbols,
    string? targetResourceType,
    IReadOnlyList<IncludeExpression> includes,
    IReadOnlyList<IncludeExpression> revIncludes,
    int includeLimit,
    IReadOnlyList<SortExpression> sort,
    SortPhase sortPhase,
    PageSpec? page,
    bool countOnly = false,
    int? top = null,
    bool systemLevelSearch = false)
```

`origin/main` (post PR #353):
```csharp
public static LoweredPlan Run(
    Expression? expression,
    SymbolTable symbols,
    string? targetResourceType,
    IReadOnlyList<IncludeExpression> includes,
    IReadOnlyList<IncludeExpression> revIncludes,
    int includeLimit,
    IReadOnlyList<SortExpression> sort,
    SortPhase sortPhase,
    PageSpec? page,
    bool countOnly = false,
    int? top = null,
    DateTimeOffset? approximationReferenceTime = null)
```

**Resolution: keep both parameters, `systemLevelSearch` before `approximationReferenceTime`** (append-only ordering, matching this codebase's own convention of adding new optional parameters at the end rather than reordering existing call sites):

```csharp
public static LoweredPlan Run(
    Expression? expression,
    SymbolTable symbols,
    string? targetResourceType,
    IReadOnlyList<IncludeExpression> includes,
    IReadOnlyList<IncludeExpression> revIncludes,
    int includeLimit,
    IReadOnlyList<SortExpression> sort,
    SortPhase sortPhase,
    PageSpec? page,
    bool countOnly = false,
    int? top = null,
    bool systemLevelSearch = false,
    DateTimeOffset? approximationReferenceTime = null)
```

`origin/main`'s body constructs `new StructuralContext(symbols, approximationReferenceTime)` and this branch's body constructs `new StructuralContext(symbols)` with its own `RequireResourceType(targetResourceType, systemLevelSearch)` logic threaded through several call sites (`LowerResourceSource`, the wildcard-compartment guard, the null-type `_include`/`_revinclude` guard, the null-type `_sort` guard). Reconcile `StructuralContext`'s own constructor the same way (accept both `symbols` and an optional `approximationReferenceTime`), and keep every `systemLevelSearch`-driven branch from this branch's `Run` body intact, threading `approximationReferenceTime` through to `StructuralContext` alongside it. Read both full method bodies (`git show origin/main:src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` vs. the pre-merge working copy) side by side before writing the merged body — this is not a mechanical text merge, it requires understanding both bodies' control flow.

- [x] **Step 2b: Resolve `ISymbolResolver`'s interface conflict**

`origin/main` adds `GetSystemIdAsync`, `GetSystemIdsAsync` (default interface implementation, batches via sequential `GetSystemIdAsync` calls unless overridden), and `GetQuantityCodeIdAsync` to the 2 members this branch already has. This is a pure addition — merge it by taking `origin/main`'s full interface (5 members) verbatim; this branch's 2 pre-existing members are an exact subset with no conflicting changes. Every existing implementer of `ISymbolResolver` on this branch (`SqlEntityFrameworkSymbolResolver` in `Ignixa.DataLayer.SqlEntityFramework`) will fail to compile until it implements the 3 new members — check whether `origin/main`'s own PR #353 already updated `SqlEntityFrameworkSymbolResolver` with real implementations (it touched `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSymbolResolver.cs` per its file list) and take that side's version if so, rather than hand-writing new implementations.

- [x] **Step 3: Resolve the remaining conflicts**

For each of the other 16 files in the conflict list, read both sides' actual diffs against the merge-base (`git diff <merge-base> HEAD -- <file>` and `git diff <merge-base> origin/main -- <file>`) before resolving — most are independent additions to different methods/switch arms within the same file (e.g. both sides likely added different `case` arms to the same dispatcher switch) and should merge cleanly once understood, but verify each one rather than assuming.

- [x] **Step 4: Build and fix any remaining compile errors**

```bash
dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
dotnet build src/Core/Ignixa.Search.Sql.Generators/Ignixa.Search.Sql.Generators.csproj
```

Expected: eventually 0 warnings, 0 errors. Fix any call site across the rest of the solution that referenced the old 2-member `ISymbolResolver` or the old `Lower.Run` parameter list positionally (should be none, since `systemLevelSearch`/`approximationReferenceTime` are both optional and named at every existing call site in this codebase's own style — but verify).

```bash
dotnet build All.sln
```

Expected: 0 warnings, 0 errors across the whole solution.

- [x] **Step 5: Run the full existing test suite on both frameworks**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

Expected: every test that existed on EITHER side before the merge now passes — this is the real acceptance bar for "reconciled correctly," not just "compiles." If a test from `origin/main`'s side fails, the merge dropped or broke PR #353's feature work; if a test from this branch's side fails, the merge dropped or broke Phase 1-9's feature work. Investigate and fix root cause per this initiative's systematic-debugging practice — do not skip or delete a failing test to make this step pass.

```bash
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

Expected: 0 failures elsewhere in the solution (E2E tests need a running app instance, out of scope for this task).

- [x] **Step 6: Commit the merge**

```bash
git add -A
git commit -m "merge: reconcile Ignixa.Search.Sql with origin/main (PR #353)"
```

(A merge commit's message is intentionally brief — the reconciliation detail belongs in this task's report, not the commit trailer. Do not `git commit --amend` a merge commit.)

---

### Task 2: `ISearchService`'s `ct` → `cancellationToken` rename cascade

**Design doc:** §1 (Prerequisite / early-task work).

**Files:**
- Modify: `src/Application/Ignixa.Domain/Abstractions/ISearchService.cs` (3 members: `SearchStreamAsync`, `CountAsync`, `GetExportRangesAsync` already uses `CancellationToken cancellationToken` correctly — confirm this at Step 1, only the first two need the rename).
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSearchService.cs` (rename `ct` → `cancellationToken` throughout — both the 2 public method signatures and every internal usage of the parameter).
- Modify: `src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedSearchService.cs` (same — 3 public methods: `SearchAsync`, `SearchStreamAsync`, `CountAsync` all use `ct`; `GetExportRangesAsync` already uses `cancellationToken`... verify at Step 1, do not assume).
- Test: any existing test file that calls these methods with a named `ct:` argument (grep for it at Step 1 — same pattern as sub-project 2's Task 1).

**Interfaces:**
- Consumes: nothing from Task 1 (independent of the `Ignixa.Search.Sql` reconciliation — this is `Ignixa.Domain`/`Ignixa.DataLayer.SqlEntityFramework`/`Ignixa.DataLayer.FileSystem`).
- Produces: `ISearchService` with `CancellationToken cancellationToken` on every member — `SqlServerCompiledSearchService` (Task 8) implements this corrected interface from the start.

- [ ] **Step 1: Re-read the current file states and grep for every `ct` site**

```bash
grep -n "CancellationToken ct" src/Application/Ignixa.Domain/Abstractions/ISearchService.cs
grep -rn "CancellationToken ct\b" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSearchService.cs
grep -rn "CancellationToken ct\b" src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedSearchService.cs
grep -rln "\.SearchStreamAsync\|\.CountAsync<\|ct:" test/ src/ | xargs grep -ln "ISearchService\|SqlEntityFrameworkSearchService\|FileBasedSearchService"
```

Confirm the exact set of sites before editing — this initiative's established pattern (sub-project 2 Task 1) found the exact count matters for verifying completeness afterward.

- [ ] **Step 2: Rename in `ISearchService.cs`**

Rename every `CancellationToken ct = default` parameter (and its XML doc `<param name="ct">` tag, if present) to `CancellationToken cancellationToken = default`.

- [ ] **Step 3: Rename in `SqlEntityFrameworkSearchService.cs`**

Rename the parameter on `SearchStreamAsync`'s and `CountAsync`'s signatures (including the `[System.Runtime.CompilerServices.EnumeratorCancellation]` attribute placement, unaffected by the rename) and every internal usage (`ct` → `cancellationToken` throughout the method bodies — this file is long, ~1329 lines, with the parameter threaded through many private helper calls; a global find-and-replace of the exact token `ct` is unsafe here since `ct` could theoretically collide with an unrelated identifier — verify none exists via `grep -n '\bct\b'` restricted to this file before doing a blanket replace, then do the rename).

- [ ] **Step 4: Rename in `FileBasedSearchService.cs`**

Same treatment for `SearchAsync`, `SearchStreamAsync`, `CountAsync`.

- [ ] **Step 5: Fix any test call sites using a named `ct:` argument**

Update each to `cancellationToken:`.

- [ ] **Step 6: Build and verify**

```bash
dotnet build All.sln
```

Expected: 0 warnings, 0 errors (CA1725 would fire as a build error if any implementer's parameter name still disagrees with the now-renamed interface).

```bash
dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.SqlEntityFramework.Tests.csproj
dotnet test test/Ignixa.DataLayer.FileSystem.Tests/Ignixa.DataLayer.FileSystem.Tests.csproj
```

Expected: same pass count as before this task (pure rename, zero functional change) — record the baseline count before this task starts (Step 1's build) and compare.

- [ ] **Step 7: Commit**

```bash
git add src/Application/Ignixa.Domain/Abstractions/ISearchService.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSearchService.cs src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedSearchService.cs
git add -u
git commit -m "refactor(search): rename ct to cancellationToken across ISearchService and its implementations"
```

---

### Task 3: `SearchCompiler` pre-built-`SearchOptions` entry point + `EmittedSqlTrace.Parameters`

**Design doc:** §2.

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs`.
- Modify: `src/Core/Ignixa.Search.Sql/Tracing/EmittedSqlTrace.cs`.
- Test: `test/Ignixa.Search.Sql.Tests/Tracing/SearchCompilerCompileFromOptionsTests.cs` (new file).

**Interfaces:**
- Consumes: Task 1's reconciled `SearchCompiler.CompileWithTimeProviderAsync` (post-merge shape — re-verify its exact current signature before writing this task's code; the sketch below is based on `origin/main`'s pre-reconciliation shape and may shift slightly depending on how Task 1's merge landed).
- Produces: `SearchCompiler.CompileFromOptionsAsync(SearchOptions options, string resourceType, ISymbolResolver resolver, ICompartmentDefinitionManager? compartmentDefinitionManager, ISearchParameterDefinitionManager? searchParameterDefinitionManager, TimeProvider? timeProvider, CancellationToken cancellationToken) : Task<SearchTrace>` — Task 8's `SqlServerCompiledSearchService` is this method's first production caller. `EmittedSqlTrace` gains a `Parameters` property — Task 8 reads `SearchTrace.Sql!.Parameters` to bind `@pN` placeholders at execution time.

- [ ] **Step 1: Re-read the post-Task-1 current state**

```bash
cat src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs
cat src/Core/Ignixa.Search.Sql/Tracing/EmittedSqlTrace.cs
cat src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs
```

Confirm the exact current `CompileWithTimeProviderAsync` signature and `Resolve.RunAsync`'s exact current signature (both may differ slightly from what's sketched below, depending on how Task 1's merge landed) before writing the new method.

- [ ] **Step 2: Add `Parameters` to `EmittedSqlTrace`**

```csharp
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>The emitted SQL plus its bound parameters and section ranges.</summary>
public sealed record EmittedSqlTrace(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters, IReadOnlyList<SqlTextRange> Ranges);
```

Update `SearchCompiler.cs`'s one construction site (inside `CompileWithTimeProviderAsync`'s try block) from `new EmittedSqlTrace(emitted.Sql, emitted.TextRanges ?? [])` to `new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? [])` — `emitted` (the `EmittedSql` returned by `SqlBuilder.Run`) already has `.Parameters` in scope at that point.

- [ ] **Step 3: Write the failing test for `Parameters` carrying real values**

```csharp
using Ignixa.Search.Sql.Tracing;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

public class EmittedSqlTraceParametersTests
{
    [Fact]
    public async Task GivenACompiledSearchWithABoundValue_WhenTraced_ThenSqlTraceCarriesTheParameter()
    {
        // Arrange -- reuse this test file's existing fixture helpers for a real end-to-end compile
        // (a simple single-parameter Patient search, e.g. Patient?_id=abc), mirroring the fixture
        // construction pattern already used elsewhere in Tracing/SearchTraceTests.cs.
        var trace = await /* existing test fixture's real CompileAsync call for a simple Patient?_id=abc search */;

        // Act
        var sqlTrace = trace.Sql;

        // Assert
        sqlTrace.ShouldNotBeNull();
        sqlTrace!.Parameters.ShouldNotBeEmpty();
        sqlTrace.Sql.ShouldContain("@p0");
        sqlTrace.Parameters[0].Name.ShouldBe("@p0");
        sqlTrace.Parameters[0].Value.ShouldBe("abc");
    }
}
```

Read `test/Ignixa.Search.Sql.Tests/Tracing/SearchTraceTests.cs` and `test/Ignixa.Search.Sql.Tests/Tracing/SearchTraceFixtures.cs` first for this project's real fixture-construction pattern (fake `ISymbolResolver`, fake `ISearchOptionsBuilder`, etc.) and use it verbatim rather than inventing a new one — replace the placeholder Arrange line with the real call.

- [ ] **Step 4: Run test to verify it fails**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmittedSqlTraceParametersTests"
```

Expected: FAIL — `EmittedSqlTrace` doesn't yet have a `Parameters` property (compile error) until Step 2 is done; if Step 2 is already done, this specific test should already pass — reorder so this genuinely fails first (write the test against the OLD 2-argument `EmittedSqlTrace` shape, confirm the compile error, then apply Step 2's fix, matching strict TDD ordering).

- [ ] **Step 5: Add the pre-built-`SearchOptions` entry point**

```csharp
/// <summary>
/// Compiles an already-built <see cref="SearchOptions"/> — skipping the Build stage entirely, since the
/// caller (a production ISearchService implementation receiving a pre-built SearchOptions, not raw query
/// parameters) has already built it upstream. Runs Resolve, Lower, and Emit only, tracing every stage the
/// same way <see cref="CompileWithTimeProviderAsync"/> does. Failures are recorded as data on
/// <see cref="SearchTrace.Failure"/>, never thrown, matching CompileAsync's own contract.
/// </summary>
public static async Task<SearchTrace> CompileFromOptionsAsync(
    SearchOptions options,
    string resourceType,
    ISymbolResolver resolver,
    ICompartmentDefinitionManager? compartmentDefinitionManager,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager,
    TimeProvider? timeProvider,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(resourceType);
    ArgumentNullException.ThrowIfNull(resolver);

    var approximationReferenceTime = (timeProvider ?? TimeProvider.System).GetUtcNow();
    var outcomes = new List<ParameterTrace>();

    var resolved = await Resolve.RunAsync(
        options.Expression,
        options.Include,
        options.RevInclude,
        options.Sort,
        resolver,
        resourceType,
        cancellationToken,
        compartmentDefinitionManager,
        searchParameterDefinitionManager);

    MarkUnresolved(outcomes, resolved.Unresolved);

    QueryPlanTrace? planTrace = null;
    EmittedSqlTrace? sqlTrace = null;
    var failure = ResolveFailure(resolved.Unresolved);

    if (resolved.Unresolved.Count == 0)
    {
        LoweredPlan? lowered = null;

        try
        {
            lowered = Lower.Run(
                options.Expression,
                resolved.Symbols,
                resourceType,
                options.Include,
                options.RevInclude,
                includeLimit: 0,
                options.Sort,
                SortPhase.Valued,
                page: null,
                systemLevelSearch: string.IsNullOrEmpty(options.ResourceType),
                approximationReferenceTime: approximationReferenceTime);

            planTrace = BuildPlanTrace(lowered, outcomes);
            MarkKnownMisses(outcomes, lowered);

            var emitted = SqlBuilder.Run(lowered.Plan, new EmitOptions(IncludeTextRanges: true));
            sqlTrace = new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? []);
        }
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            failure = RecordFailure(outcomes, lowered is null ? TraceStage.Lower : TraceStage.Emit, ex);
        }
    }

    return new SearchTrace(resourceType, outcomes, planTrace, sqlTrace)
    {
        Failure = failure,
        Implicit = DetectImplicit(options),
    };
}
```

**This code assumes Task 1 landed the merged `Lower.Run` signature from Step 2a of Task 1's brief exactly (both `systemLevelSearch` and `approximationReferenceTime` present) and that `MarkKnownMisses` exists (added by PR #353's merge) — re-verify both before pasting this in.** `DetectImplicit`'s existing signature takes `(IReadOnlyList<QueryParameter> parameters, SearchOptions options)` — since this entry point has no raw `QueryParameter` list (that's the whole point), either add an overload of `DetectImplicit` that only reads `options` (its `parameters`-derived `supplied` set exists solely to detect *_count/_total were explicitly supplied*, information not available or needed here — a pre-built `SearchOptions` has no notion of "was this explicitly supplied" the trace can recover, so the simplest correct fix is skipping implicit-detection for this entry point and returning `Implicit = []` on this trace) — re-read `DetectImplicit`'s real current body (Task 1 may have changed it) before deciding; the sketch above assumes a `DetectImplicit(SearchOptions)` overload exists or is trivial to add, but confirm this doesn't silently misreport `_count`/`_total` as always-implicit when they were genuinely user-supplied. If in doubt, return `Implicit = []` explicitly with a one-line comment explaining pre-built `SearchOptions` doesn't carry supplied-ness, rather than guessing.

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

Expected: PASS on both net9.0 and net10.0, including the new test, zero regressions to the existing tracing suite.

- [ ] **Step 7: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs src/Core/Ignixa.Search.Sql/Tracing/EmittedSqlTrace.cs test/Ignixa.Search.Sql.Tests/Tracing/EmittedSqlTraceParametersTests.cs
git commit -m "feat(search-sql): add SearchCompiler.CompileFromOptionsAsync, carry EmittedSqlTrace.Parameters"
```

---

### Task 4: Offset-based paging in `Lower`/`Emit`

**Design doc:** §3 (read this section in full before starting — it went through 2 rounds of Fable review fixing real math errors in the two-phase sort disambiguation; transcribe its final, corrected formula exactly, do not re-derive it from scratch).

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs` (or wherever `PageSpec` ends up living post-Task-1 — re-verify) — add an `OffsetSpec` type.
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs` — add an `OffsetPage` field.
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` — add the new parameter, pairwise-exclusion guard.
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` — render `OFFSET ... FETCH NEXT`.
- Test: `test/Ignixa.Search.Sql.Tests/Builders/SqlBuilderOffsetPagingTests.cs` (new file), `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (add cases).

**Interfaces:**
- Consumes: Task 1's reconciled `Lower.Run`/`SqlBuilder.Run`.
- Produces: `OffsetSpec(int Offset, int Limit)` record; `Lower.Run`'s new `offsetPage: OffsetSpec? = null` parameter; `QueryPlan.OffsetPage`. Task 8's `SqlServerCompiledSearchService` constructs an `OffsetSpec` from the decoded `Ignixa.Search.Models.ContinuationToken` and drives the two-phase sort executor loop described below.

- [ ] **Step 1: Re-read current `PageSpec`/`QueryPlan`/`SqlBuilder` state post-Task-1**

```bash
cat src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs
cat src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs
cat src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs
```

- [ ] **Step 2: Add `OffsetSpec`**

In `SortSpec.cs` (alongside the existing `PageSpec` record):

```csharp
/// <summary>
/// An offset+count paging request — the alternative to <see cref="PageSpec"/>'s keyset boundary, for
/// callers bridging Ignixa.Search.Models.ContinuationToken's offset+count model (which this compiler's
/// own KeysetContinuationToken is explicitly not compatible with). Mutually exclusive with PageSpec and
/// with QueryPlan.Top at the Lower.Run call site — Lower.Run throws if more than one paging mechanism is
/// supplied.
/// </summary>
public sealed record OffsetSpec(int Offset, int Limit);
```

- [ ] **Step 3: Add `QueryPlan.OffsetPage`**

Add `OffsetSpec? OffsetPage = null` as a new optional positional parameter at the end of `QueryPlan`'s record declaration (append-only, matching this file's own established pattern for every prior additive field).

- [ ] **Step 4: Write the failing test for the pairwise-exclusion guard**

```csharp
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class OffsetPagingGuardTests
{
    [Fact]
    public void GivenBothOffsetPageAndKeysetPage_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange -- reuse this test file's existing symbol-table construction helper
        var symbols = /* existing fixture helper building a minimal SymbolTable with one resource type */;
        var page = new PageSpec([], new SqlParameterRef((short)1), new SqlParameterRef(1L));
        var offsetPage = new OffsetSpec(Offset: 10, Limit: 5);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page,
            offsetPage: offsetPage));
    }

    [Fact]
    public void GivenBothOffsetPageAndTop_WhenLowering_ThenThrowsNotSupportedException()
    {
        var symbols = /* same fixture helper */;
        var offsetPage = new OffsetSpec(Offset: 10, Limit: 5);

        Should.Throw<NotSupportedException>(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            top: 10,
            offsetPage: offsetPage));
    }

    [Fact]
    public void GivenPageAndTopTogetherWithNoOffsetPage_WhenLowering_ThenDoesNotThrow()
    {
        // Regression guard for the design doc's own corrected rule: page+top together is keyset
        // paging's own valid, existing call shape (top is keyset's page-size mechanism) and must remain
        // legal -- only offset-vs-page and offset-vs-top are mutually exclusive, not page-vs-top.
        var symbols = /* same fixture helper */;
        var page = new PageSpec([], new SqlParameterRef((short)1), new SqlParameterRef(1L));

        Should.NotThrow(() => Lower.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page,
            top: 10));
    }
}
```

Read `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs` first for this project's real `SymbolTable`-construction fixture helper and use it verbatim in place of the placeholder comments.

- [ ] **Step 5: Run tests to verify they fail**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~OffsetPagingGuardTests"
```

Expected: FAIL — `Lower.Run` has no `offsetPage` parameter yet (compile error).

- [ ] **Step 6: Add the parameter and guard to `Lower.Run`**

Add `OffsetSpec? offsetPage = null` as a new trailing optional parameter on `Lower.Run` (after `approximationReferenceTime`, append-only). At the very top of the method body, before any other logic:

```csharp
if (offsetPage is not null && (page is not null || top is not null))
{
    throw new NotSupportedException(
        "offsetPage cannot be combined with the keyset page boundary or with top -- T-SQL forbids TOP " +
        "and OFFSET in the same query (error 10741), and offset-mode paging and keyset paging are " +
        "distinct, non-composable pagination models. page+top together remains valid -- that is keyset " +
        "paging's own existing call shape (top is keyset's page-size mechanism), unaffected by this guard.");
}
```

Thread `offsetPage` through to the final `new QueryPlan(...)` construction as its `OffsetPage` value.

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~OffsetPagingGuardTests"
```

Expected: PASS, all 3 tests.

- [ ] **Step 8: Render `OFFSET ... FETCH NEXT` in `SqlBuilder`**

Read `SqlBuilder.Run`'s current 3-shape structure in full again (CountOnly / no-includes / includes-bearing) before editing — the exact insertion points depend on which shape is active. The operative rule (design doc §3, corrected): render `OFFSET`/`FETCH NEXT` wherever the keyset boundary predicate and its `ORDER BY` already render for that plan shape — the match-page CTE on an includes-bearing plan, or the single top-level `SELECT` on a plan with no includes.

For the **no-includes shape** (`plan.Includes is not { Count: > 0 }` branch), after the existing `ORDER BY` append:

```csharp
if (plan.OffsetPage is { } offsetPage)
{
    writer.Append($"\nOFFSET {EmitParam(new SqlParameterRef(offsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(offsetPage.Limit), parameters)} ROWS ONLY");
}
```

For the **includes-bearing shape**, apply the same append inside the match-page CTE's construction, immediately after `cteOrderBy` is appended (both `cteOrderBy` and this offset clause are gated the same way — legal together per SQL Server Msg 1033/1033's OFFSET-alongside-ORDER-BY-in-a-CTE allowance, confirmed by the design doc's own citation of this exact rule). Also extend the existing `plan.Top is not null` gate that currently decides whether `cteOrderBy` renders at all — with offset mode, an `ORDER BY` inside the match-page CTE is legal (and required) even when `plan.Top` is null, since `OFFSET` itself requires an `ORDER BY`. Change:

```csharp
var cteOrderBy = plan.Top is not null ? $"\n    ORDER BY {EmitOrderBy(plan.Sort)}" : string.Empty;
```

to:

```csharp
var cteOrderBy = plan.Top is not null || plan.OffsetPage is not null
    ? $"\n    ORDER BY {EmitOrderBy(plan.Sort)}"
    : string.Empty;
```

and append the same `OFFSET ... FETCH NEXT` fragment used above, right after `cteOrderBy`, inside the match-page CTE's `writer.Append(cteOrderBy);` call — add a second line immediately after it:

```csharp
writer.Append(cteOrderBy);
if (plan.OffsetPage is { } matchOffsetPage)
{
    writer.Append($"\n    OFFSET {EmitParam(new SqlParameterRef(matchOffsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(matchOffsetPage.Limit), parameters)} ROWS ONLY");
}
```

The `CountOnly` shape needs no change — `OffsetPage` is meaningless for a count query (the design doc's adapter never sets both `countOnly: true` and an `offsetPage` in the same compile call; add a defensive guard in `Lower.Run` too if one doesn't already exist for the analogous `page`/`countOnly` combination — check first, this may already be guarded).

- [ ] **Step 9: Write and run an end-to-end offset-paging compilation test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, following that file's existing pattern for a simple single-parameter search (read a few existing tests in this file first for its exact fixture-construction idiom):

```csharp
[Fact]
public void GivenAnOffsetPageRequest_WhenCompiledWithNoIncludesOrSort_ThenEmitsOffsetFetchNext()
{
    // Arrange -- mirror an existing simple Patient?_id=abc test's symbol table / expression construction.
    var offsetPage = new OffsetSpec(Offset: 20, Limit: 10);

    // Act
    var lowered = Lower.Run(/* same expression/symbols as the mirrored test */, targetResourceType: "Patient",
        includes: [], revIncludes: [], includeLimit: 0, sort: [], SortPhase.Valued, page: null, offsetPage: offsetPage);
    var emitted = SqlBuilder.Run(lowered.Plan);

    // Assert
    emitted.Sql.ShouldContain("OFFSET @p");
    emitted.Sql.ShouldContain("FETCH NEXT @p");
    emitted.Sql.ShouldContain("ROWS ONLY");
    emitted.Parameters.ShouldContain(p => (int)p.Value == 20);
    emitted.Parameters.ShouldContain(p => (int)p.Value == 10);
}
```

- [ ] **Step 10: Run the full compiler test suite**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

Expected: PASS on both net9.0 and net10.0, zero regressions.

- [ ] **Step 11: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Lowering/OffsetPagingGuardTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): add offset-based paging (OFFSET/FETCH NEXT) alongside keyset PageSpec"
```

---

### Task 5: `OuterPredicate` surrogate-ID range extension

**Design doc:** §4.

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`.
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (add cases), `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs` (add cases).

**Interfaces:**
- Consumes: Task 1's reconciled `Lower.Run`, Task 4's `Lower.Run` (this task adds one more trailing optional parameter to the same method — sequence after Task 4 to avoid two tasks editing the same signature concurrently).
- Produces: `Lower.Run`'s new `surrogateIdRange: (long Start, long End)? = null` parameter. Task 8's `SqlServerCompiledSearchService` passes this through when `SearchOptions.StartSurrogateId`/`EndSurrogateId` are both set.

- [ ] **Step 1: Re-read current `Lower.Run`/`ExtractResourceColumnPredicates` state post-Task-4**

```bash
cat src/Core/Ignixa.Search.Sql/Lowering/Lower.cs
```

- [ ] **Step 2: Write the failing end-to-end test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, mirroring an existing `_lastUpdated` or `_id` resource-column test in this file (read one first):

```csharp
[Fact]
public void GivenASurrogateIdRange_WhenCompiledAlongsideAnOrdinaryPredicate_ThenBothPredicatesAppearInOuterPredicate()
{
    // Arrange -- mirror an existing simple Patient?_id=abc test's symbol table / expression construction.

    // Act
    var lowered = Lower.Run(/* same expression/symbols as the mirrored test */, targetResourceType: "Patient",
        includes: [], revIncludes: [], includeLimit: 0, sort: [], SortPhase.Valued, page: null,
        surrogateIdRange: (Start: 1000L, End: 2000L));
    var emitted = SqlBuilder.Run(lowered.Plan);

    // Assert
    emitted.Sql.ShouldContain("ResourceSurrogateId >= @p");
    emitted.Sql.ShouldContain("ResourceSurrogateId <= @p");
    emitted.Parameters.ShouldContain(p => (long)p.Value == 1000L);
    emitted.Parameters.ShouldContain(p => (long)p.Value == 2000L);
}

[Fact]
public void GivenOnlyASurrogateIdRangeWithNoOtherExpression_WhenCompiled_ThenComposesWithTheBareResourceSource()
{
    // Arrange -- covers Lower.Run's expression == null path (the common export case: no search
    // predicate at all, just a resource-type + surrogate-range scan).

    // Act
    var lowered = Lower.Run(expression: null, /* symbols */, targetResourceType: "Patient",
        includes: [], revIncludes: [], includeLimit: 0, sort: [], SortPhase.Valued, page: null,
        surrogateIdRange: (Start: 1000L, End: 2000L));
    var emitted = SqlBuilder.Run(lowered.Plan);

    // Assert
    emitted.Sql.ShouldContain("ResourceSurrogateId >= @p");
    emitted.Sql.ShouldContain("ResourceSurrogateId <= @p");
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SurrogateIdRange"
```

Expected: FAIL — `Lower.Run` has no `surrogateIdRange` parameter yet (compile error).

- [ ] **Step 4: Add the parameter and composition logic**

Add `(long Start, long End)? surrogateIdRange = null` as a new trailing optional parameter on `Lower.Run` (after Task 4's `offsetPage`, append-only). Immediately after `outerPredicate` is computed (both in the `expression is null` branch, where it starts as the implicit `null`, and the `expression is not null` branch, where `ExtractResourceColumnPredicates` sets it), AND in the surrogate-id range predicate:

```csharp
if (surrogateIdRange is { } range)
{
    var table = SqlCatalog.Default.Table("Resource");
    var column = new SqlColumnRef(table.TableName, "ResourceSurrogateId");
    var rangePredicate = new Predicate.And(
        new Predicate.GreaterThanOrEqual(column, new SqlParameterRef(range.Start)),
        new Predicate.LessThanOrEqual(column, new SqlParameterRef(range.End)));
    outerPredicate = outerPredicate is null ? rangePredicate : new Predicate.And(outerPredicate, rangePredicate);
}
```

Place this composition right before the final `new QueryPlan(...)` construction (after both branches of the `if (expression is null) / else` have already set `outerPredicate` to whatever the search expression itself contributed), so it composes correctly regardless of which branch ran. Re-verify `SqlParameterRef`'s exact constructor shape (`SqlParameterRef(object value)` presumably, taking a `long` — confirm against `Predicate.cs`'s or `SqlParameterRef.cs`'s real current definition) before pasting this in verbatim.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SurrogateIdRange"
```

Expected: PASS, both tests.

- [ ] **Step 6: Run the full compiler test suite**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

Expected: PASS on both net9.0 and net10.0, zero regressions.

- [ ] **Step 7: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): extend OuterPredicate with an optional surrogate-ID range filter"
```

---

### Task 6: `_sort=_id` — add the missing `BuildSortKey` case

**Design doc:** the "Open item to confirm at plan time" paragraph in the Differential harness section.

**Confirmed during this plan's own research (not left open): `_id` IS reachable via `_sort=_id`.** `SearchParameterInfo`'s `SortStatus` is derived from the parameter's type (`IsSortableType`), and `Token` (which `_id` is) is in the sortable-type list — `SearchOptionsBuilder.cs:445`'s `SortStatus != Enabled` gate does NOT filter it out. Without this task, `Lower.BuildSortKey` (which has no `_id` case) would silently misroute `_sort=_id` to the generic `Aggregated` path over `TokenSearchParam` — a table `_id` is never indexed into — producing an `INNER JOIN` that matches nothing (an empty `Valued` phase, a silent wrong-result bug, not a thrown error).

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs` — add `SortKeyKind.ResourceId`.
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` — add the `_id` case to `BuildSortKey`.
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` — teach `EmitSortJoins`/`SortValueExpr` about the new kind (it needs a join to `dbo.Resource` for `ResourceId`, since the CTE graph's own `(T1, Sid1)` projection doesn't carry it).
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`, `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`.

**Interfaces:**
- Consumes: Task 1's reconciled `Lower.cs`/`SqlBuilder.cs`.
- Produces: `_sort=_id` compiles to a real, correct plan — required before Task 11 (the sort/paging differential harness task) can safely include `_sort=_id` in its query set, per the design doc's own explicit instruction.

- [ ] **Step 1: Re-read `BuildSortKey`, `SortKeyKind`, and `EmitSortJoins`/`SortValueExpr` in full**

```bash
grep -n "SortKeyKind\|BuildSortKey" -A 5 src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs
```

Re-read `EmitSortJoins`/`SortValueExpr`/`EmitMissingPrimaryFilter` in `SqlBuilder.cs` (already read once during this plan's own research — re-confirm nothing shifted in Task 1's merge) — note specifically how `SortKeyKind.LastUpdated` is special-cased as needing NO join (`m.Sid1` directly) versus `SortKeyKind.Aggregated`/`String`/`Date` all needing a join to a search-param table. `_id` needs a THIRD shape: no search-param-table join, but it DOES need a join (to `dbo.Resource`, unlike `LastUpdated` which needs none at all since `ResourceSurrogateId` already encodes ordering).

- [ ] **Step 2: Add `SortKeyKind.ResourceId`**

In `SortSpec.cs`:

```csharp
#pragma warning disable CA1720 // Identifier contains type name -- 'String' mirrors the FHIR sort-parameter type it represents.
public enum SortKeyKind
{
    String,
    Date,
    LastUpdated,
    ResourceId,
    Aggregated,
}
#pragma warning restore CA1720
```

- [ ] **Step 3: Write the failing test for `BuildSortKey`'s `_id` case**

```csharp
[Fact]
public void GivenSortByResourceId_WhenBuildingSortKey_ThenReturnsResourceIdKindWithNoSearchParamId()
{
    // Arrange
    var symbols = /* existing fixture helper */;
    var idParameter = new SearchParameterInfo("Resource-id", "_id") { Type = SearchParamType.Token };
    var sortExpression = new SortExpression(idParameter, SortOrder.Ascending);

    // Act
    var key = Lower.BuildSortKey(sortExpression, symbols);

    // Assert
    key.Kind.ShouldBe(SortKeyKind.ResourceId);
    key.SearchParamId.ShouldBeNull();
}
```

Read `LowerTests.cs`'s existing `BuildSortKey` tests (for `_lastUpdated`, String, Date) first and match their exact fixture-construction style.

- [ ] **Step 4: Run test to verify it fails**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~GivenSortByResourceId"
```

Expected: FAIL — `_id` currently falls through to the generic `Aggregated`/`TokenSearchParam` path (wrong `Kind`), not a compile error, since `BuildSortKey` has no code-based dispatch at the top — it treats `_id` identically to any other Token parameter today.

- [ ] **Step 5: Add the `_id` case to `BuildSortKey`**

```csharp
internal static SortKey BuildSortKey(SortExpression sortExpression, SymbolTable symbols)
{
    if (sortExpression.Parameter.Code == "_lastUpdated")
    {
        return new SortKey(null, SortKeyKind.LastUpdated, sortExpression.SortOrder);
    }

    if (sortExpression.Parameter.Code == "_id")
    {
        return new SortKey(null, SortKeyKind.ResourceId, sortExpression.SortOrder);
    }

    var searchParamId = symbols.SearchParamId(sortExpression.Parameter);
    // ... rest unchanged
}
```

This check must come before the generic `searchParamId = symbols.SearchParamId(sortExpression.Parameter)` line — `_id` has no `SearchParamId` (it is a resource-column code, same reasoning `ResourceColumnLoweringRule.IsResourceColumnCode` already establishes for `_id`/`_type`/`_lastUpdated`), so resolving one for it would either throw or return a meaningless value.

- [ ] **Step 6: Run test to verify it passes**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~GivenSortByResourceId"
```

Expected: PASS.

- [ ] **Step 7: Teach `SqlBuilder` to render the new kind**

In `EmitSortJoins`, add a case for `SortKeyKind.ResourceId` alongside the existing `LastUpdated` skip and `Aggregated`/String/Date join construction:

```csharp
if (key.Kind == SortKeyKind.ResourceId)
{
    var joinType = i == 0 ? "INNER" : "LEFT";
    joins.Add($"\n{joinType} JOIN dbo.Resource rid{i} ON rid{i}.ResourceTypeId = m.T1 AND rid{i}.ResourceSurrogateId = m.Sid1");
    continue;
}
```

In `SortValueExpr`, add the matching value-expression case (before the `Aggregated`/String/Date branches, since `ResourceId` needs neither an ISNULL-sentinel treatment nor the `key.Column`/`key.Table` fields those branches read — `ResourceId` on `dbo.Resource` is NOT NULL by schema, so no missing-value handling is needed here at all, unlike every other Aggregated/String/Date key):

```csharp
if (key.Kind == SortKeyKind.ResourceId)
{
    return $"rid{index}.ResourceId";
}
```

In `EmitMissingPrimaryFilter`, add a guard clause analogous to the existing `LastUpdated`/null-`SearchParamId` guard — `_id` is NEVER missing (every resource has a `ResourceId` by definition), so a `MissingPrimary` phase on `_id` is exactly as invalid as one on `_lastUpdated`:

```csharp
if (key.Kind == SortKeyKind.LastUpdated || key.Kind == SortKeyKind.ResourceId || key.SearchParamId is null)
{
    throw new InvalidOperationException(
        "SortSpec.Phase == MissingPrimary with a LastUpdated or ResourceId (or otherwise SearchParamId-less) " +
        "primary key reached Emit -- neither is ever \"missing\" (both are non-nullable resource columns), " +
        "so neither has a MissingPrimary segment. Lower.BuildSortSpec should reject this combination the same " +
        "way it already does for LastUpdated -- extend that guard to cover ResourceId too if it doesn't yet.");
}
```

Also extend `Lower.BuildSortSpec`'s existing `phase == SortPhase.MissingPrimary && keys[0].Kind == SortKeyKind.LastUpdated` guard to also check `keys[0].Kind == SortKeyKind.ResourceId`, so this is rejected as early and clearly as `_lastUpdated`'s equivalent case already is, rather than only being caught defensively at Emit time.

- [ ] **Step 8: Write and run an end-to-end `_sort=_id` compilation test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, mirroring an existing `_sort=_lastUpdated` test's structure:

```csharp
[Fact]
public void GivenSortByIdAscending_WhenCompiled_ThenJoinsResourceAndOrdersByResourceId()
{
    // Arrange -- mirror an existing _sort=_lastUpdated end-to-end test's symbol table / expression
    // construction, substituting a _sort=_id SortExpression.

    // Act
    var lowered = Lower.Run(/* ... */, sort: [/* _id ascending SortExpression */], SortPhase.Valued, page: null);
    var emitted = SqlBuilder.Run(lowered.Plan);

    // Assert
    emitted.Sql.ShouldContain("JOIN dbo.Resource rid0");
    emitted.Sql.ShouldContain("rid0.ResourceId");
}
```

- [ ] **Step 9: Run the full compiler test suite**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

Expected: PASS on both net9.0 and net10.0, zero regressions.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "fix(search-sql): add SortKeyKind.ResourceId so _sort=_id compiles correctly instead of silently matching nothing"
```

---

### Task 7: `SqlServerSymbolResolver`

**Design doc:** §5.

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs` — add read-only `TryGetSystemIdAsync`/`TryGetQuantityCodeIdAsync`, using the existing `MissingSentinel` negative-caching convention.
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerSymbolResolver.cs`.
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerSymbolResolverTests.cs` (new file).

**Interfaces:**
- Consumes: Task 1's reconciled `ISymbolResolver` (5 members).
- Produces: `SqlServerSymbolResolver : ISymbolResolver`, constructed from `(SqlServerSearchIndexReferenceDataCache cache)`. Task 8's `SqlServerCompiledSearchService` constructs one per repository (mirroring how `SqlServerHistoryQueryExecutor` is constructed inside `SqlServerFhirRepository`'s own primary constructor — no new factory method needed on `SqlServerRepositoryFactory` for this specific object, since it only needs the already-tenant-scoped cache).

- [ ] **Step 1: Re-read the current cache and `ISymbolResolver` shapes**

```bash
cat src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs
cat src/Core/Ignixa.Search.Sql/Symbols/ISymbolResolver.cs
```

Confirm `MissingSentinel = -1` (declared `short` today, for the `_resourceTypeCache`/`_searchParamCache` maps) — the new system/quantity-code negative-caching needs its own `int` sentinel, since `_systemCache`/`_quantityCodeCache` are `ConcurrentDictionary<string, int>`. `-1` is safe as an `int` sentinel too (real `SystemId`/`QuantityCodeId` values are positive identity-column values, confirmed by `GetOrCreateSystemIdAsync`'s `OUTPUT INSERTED.SystemId` pattern) — reuse the same literal value, declared as its own `private const int SystemQuantityMissingSentinel = -1;` (a second constant, not a reused `short` one, since the two dictionaries have different value types).

- [ ] **Step 2: Write the failing test for read-only, miss-returns-null lookups**

```csharp
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerSearchIndexReferenceDataCacheReadOnlyLookupTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenASystemNeverInserted_WhenLookedUpReadOnly_ThenReturnsNullAndDoesNotInsertARow()
    {
        // Arrange
        var cache = /* construct SqlServerSearchIndexReferenceDataCache the same way TestTenantDatabase's
                        other fixtures do, via _database.SqlExecutionService/_database.TenantId */;
        const string unknownSystem = "http://never-inserted.example.org/this-specific-test-run";

        // Act
        var id = await cache.TryGetSystemIdAsync(unknownSystem, CancellationToken.None);

        // Assert
        id.ShouldBeNull();

        // Assert no row was created as a side effect (this is the whole point -- get-or-create semantics
        // would have inserted one)
        var idAfterASecondLookup = await cache.TryGetSystemIdAsync(unknownSystem, CancellationToken.None);
        idAfterASecondLookup.ShouldBeNull();
    }

    [Fact]
    public async Task GivenASystemAlreadyInsertedByTheWritePath_WhenLookedUpReadOnly_ThenReturnsItsRealId()
    {
        // Arrange
        var cache = /* same construction */;
        const string knownSystem = "http://real-write-path-system.example.org/for-this-test";
        var insertedId = await cache.GetOrCreateSystemIdAsync(knownSystem, CancellationToken.None);

        // Act -- a FRESH cache instance, to prove this reads from the database, not the same
        // in-process dictionary the insert just warmed
        var freshCache = /* same construction, new instance */;
        var readId = await freshCache.TryGetSystemIdAsync(knownSystem, CancellationToken.None);

        // Assert
        readId.ShouldBe(insertedId);
    }
}
```

Read `TestTenantDatabase.cs` first for its real `SqlServerSearchIndexReferenceDataCache` construction pattern (already used by other integration tests in this project) and use it verbatim.

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheReadOnlyLookupTests"
```

Expected: FAIL — `TryGetSystemIdAsync` doesn't exist yet (compile error).

- [ ] **Step 4: Add the read-only lookup methods to the cache**

```csharp
private const int SystemQuantityMissingSentinel = -1;

public async Task<int?> TryGetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
{
    ArgumentException.ThrowIfNullOrEmpty(systemUri);

    if (_systemCache.TryGetValue(systemUri, out var cachedId))
    {
        return cachedId == SystemQuantityMissingSentinel ? null : cachedId;
    }

    await _dbLock.WaitAsync(cancellationToken);
    try
    {
        if (_systemCache.TryGetValue(systemUri, out cachedId))
        {
            return cachedId == SystemQuantityMissingSentinel ? null : cachedId;
        }

        using var command = new SqlCommand("SELECT SystemId FROM dbo.System WHERE Value = @Value");
        command.Parameters.Add("@Value", SqlDbType.NVarChar).Value = systemUri;
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            tenantId, command, reader => reader.GetInt32(0), cancellationToken);

        if (rows.Count == 0)
        {
            _systemCache[systemUri] = SystemQuantityMissingSentinel;
            return null;
        }

        var id = rows[0];
        _systemCache[systemUri] = id;
        return id;
    }
    finally
    {
        _dbLock.Release();
    }
}

public async Task<int?> TryGetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
{
    ArgumentException.ThrowIfNullOrEmpty(code);

    if (_quantityCodeCache.TryGetValue(code, out var cachedId))
    {
        return cachedId == SystemQuantityMissingSentinel ? null : cachedId;
    }

    await _dbLock.WaitAsync(cancellationToken);
    try
    {
        if (_quantityCodeCache.TryGetValue(code, out cachedId))
        {
            return cachedId == SystemQuantityMissingSentinel ? null : cachedId;
        }

        using var command = new SqlCommand("SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @Value");
        command.Parameters.Add("@Value", SqlDbType.NVarChar).Value = code;
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            tenantId, command, reader => reader.GetInt32(0), cancellationToken);

        if (rows.Count == 0)
        {
            _quantityCodeCache[code] = SystemQuantityMissingSentinel;
            return null;
        }

        var id = rows[0];
        _quantityCodeCache[code] = id;
        return id;
    }
    finally
    {
        _dbLock.Release();
    }
}
```

**A real, disclosed risk this task must reason about explicitly, not silently inherit:** `_systemCache`/`_quantityCodeCache` are shared, single dictionaries used by BOTH the get-or-create write path (`GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync`) and these new read-only methods. Once `TryGetSystemIdAsync` caches `SystemQuantityMissingSentinel` for a system that was genuinely absent at search time, a LATER write (indexing a resource that introduces that same system) calls `GetOrCreateSystemIdAsync`, which checks the cache first (`_systemCache.TryGetValue(systemUri, out var cachedId)` — **returns the stale `-1` sentinel value directly as if it were a real ID**, since `GetOrCreateSystemIdAsync`'s existing code has no sentinel-awareness of its own). This is a genuine bug this task would introduce if left as-is. **Fix `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync`'s existing cache-check lines to also treat `SystemQuantityMissingSentinel` as a cache miss** (mirroring exactly how `GetResourceTypeIdAsync`/`GetSearchParamIdAsync` already handle their own `MissingSentinel` correctly today): change `if (_systemCache.TryGetValue(systemUri, out var cachedId)) { return cachedId; }` to `if (_systemCache.TryGetValue(systemUri, out var cachedId) && cachedId != SystemQuantityMissingSentinel) { return cachedId; }` in both the fast-path and double-checked-locking-path checks, in both `GetOrCreateSystemIdAsync` and `GetOrCreateQuantityCodeIdAsync`. Write a test proving this specific interaction (a `TryGetSystemIdAsync` miss followed by a `GetOrCreateSystemIdAsync` for the same system correctly inserts and returns a real ID, not the stale sentinel) before considering this task done.

- [ ] **Step 5: Write the interaction test for the shared-cache sentinel fix**

```csharp
[Fact]
public async Task GivenASystemMissedByReadOnlyLookup_WhenLaterCreatedByTheWritePath_ThenReturnsTheRealIdNotTheStaleSentinel()
{
    // Arrange
    var cache = /* same construction, ONE shared instance across both calls -- this specifically
                    exercises the shared-dictionary sentinel interaction, not two fresh instances */;
    const string system = "http://later-created-system.example.org/for-this-test";
    var missedId = await cache.TryGetSystemIdAsync(system, CancellationToken.None);
    missedId.ShouldBeNull();

    // Act
    var createdId = await cache.GetOrCreateSystemIdAsync(system, CancellationToken.None);

    // Assert
    createdId.ShouldBeGreaterThan(0);
    var readBackId = await cache.TryGetSystemIdAsync(system, CancellationToken.None);
    readBackId.ShouldBe(createdId);
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerSearchIndexReferenceDataCache"
```

Expected: PASS, all new tests.

- [ ] **Step 7: Create `SqlServerSymbolResolver`**

```csharp
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.DataLayer.SqlServer.Search;

/// <summary>
/// Adapts <see cref="SqlServerSearchIndexReferenceDataCache"/> (this project's tenant-scoped reference-data
/// cache) to <see cref="ISymbolResolver"/> (Ignixa.Search.Sql's I/O contract). System/quantity-code lookups
/// route through the cache's read-only, miss-returns-null methods -- never the write path's get-or-create
/// methods, which would silently insert new catalog rows as a side effect of a search.
/// </summary>
public sealed class SqlServerSymbolResolver(SqlServerSearchIndexReferenceDataCache cache) : ISymbolResolver
{
    private readonly SqlServerSearchIndexReferenceDataCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        => _cache.GetSearchParamIdAsync(parameter.Url?.ToString() ?? string.Empty, cancellationToken);

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
        => _cache.GetResourceTypeIdAsync(resourceType, cancellationToken);

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        => _cache.TryGetSystemIdAsync(system, cancellationToken);

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        => _cache.TryGetQuantityCodeIdAsync(code, cancellationToken);
}
```

`GetSystemIdsAsync` is not overridden — `ISymbolResolver`'s default interface implementation (sequential `GetSystemIdAsync` calls) is correct and sufficient here; only override it later if a real performance need is measured, matching this codebase's YAGNI stance. **Re-verify `GetSearchParamIdAsync`'s exact real signature on `ISymbolResolver` before pasting this** — the design doc's own citation of `SqlEntityFrameworkSymbolResolver`'s implementation shows it takes the whole `SearchParameterInfo`, not a bare URL string; the code above's `_cache.GetSearchParamIdAsync(parameter.Url?.ToString() ?? ...)` assumes the CACHE's own method takes a string URL (confirmed against `SqlServerSearchIndexReferenceDataCache.GetSearchParamIdAsync(string uri, ...)`, read in Step 1) — the RESOLVER's method takes the full `SearchParameterInfo` per the interface, and only converts to a URL string when calling the cache, which is what this code already does; do not confuse the two signatures.

- [ ] **Step 8: Write and run the resolver-level test**

```csharp
[Fact]
public async Task GivenASearchParameterWithAKnownUrl_WhenResolved_ThenReturnsItsSearchParamId()
{
    // Arrange -- mirror TestTenantDatabase's real SqlServerSymbolResolver construction (new, added by
    // this task) and a real, known SearchParameterInfo (e.g. Patient's _id or a common core parameter
    // already loaded by schema deployment).

    // Act & Assert
}
```

- [ ] **Step 9: Run the full integration test suite**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

Expected: PASS, same baseline count as before this task plus the new tests.

- [ ] **Step 10: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerSymbolResolver.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerSymbolResolverTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerSearchIndexReferenceDataCacheReadOnlyLookupTests.cs
git commit -m "feat(datalayer-sqlserver): add SqlServerSymbolResolver with read-only system/quantity-code lookups"
```

---

### Task 8: `SqlServerCompiledSearchService` — `SearchStreamAsync`/`CountAsync`

**Design doc:** Architecture section, steps 1-5.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerCompiledSearchService.cs`.
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerCompiledSearchServiceTests.cs` (new file).

**Interfaces:**
- Consumes: Task 2's `ISearchService` (correct `cancellationToken` naming), Task 3's `SearchCompiler.CompileFromOptionsAsync`, Task 4's `OffsetSpec`, Task 5's `surrogateIdRange` parameter (both threaded through `CompileFromOptionsAsync`'s own `Lower.Run` call internally — re-verify Task 3's method actually exposes hooks for these, or extend it in this task if it doesn't yet — the design doc's §2 sketch of `CompileFromOptionsAsync` did not originally include offset/surrogate-range parameters since Tasks 4/5 didn't exist yet when §2 was designed; add them now as additional optional parameters on `CompileFromOptionsAsync`, threaded straight to the corresponding `Lower.Run` parameters), Task 7's `SqlServerSymbolResolver`, `RequestNotValidException` (`Ignixa.Domain.Exceptions`), `ISqlExecutionService`, `GzipResourceCompressor`.
- Produces: `SqlServerCompiledSearchService : ISearchService` (`SearchStreamAsync`/`CountAsync` only — `GetExportRangesAsync` is Task 9). Task 14 (cutover) wires this into `SqlServerRepositoryFactory`.

- [ ] **Step 1: Re-read current state of every consumed piece**

```bash
cat src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs
cat src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerSymbolResolver.cs
cat src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerHistoryQueryExecutor.cs
```

Confirm `CompileFromOptionsAsync`'s exact current parameter list (Task 3 built it without offset/surrogate-range support, since Tasks 4/5 landed after — extend it now per this task's Interfaces note above) and `SqlServerHistoryQueryExecutor`'s constructor/field pattern (this task's constructor should mirror it exactly: plain non-generic `ILogger` if `SqlServerFhirRepository`'s own `ILogger<SqlServerFhirRepository>` needs to pass through unchanged when this service is constructed alongside the repository — re-verify whether `SqlServerCompiledSearchService` is constructed by the SAME factory method that builds `SqlServerFhirRepository`, in which case the plain-`ILogger` pattern applies identically, or by a separate one with its own generic `ILogger<SqlServerCompiledSearchService>` — Task 14 decides this, but design this constructor flexibly enough to accept either without rework: accept `ILogger` (plain), matching the established `SqlServerHistoryQueryExecutor` precedent, since it's this project's most recent and most-reviewed sibling-collaborator pattern).

- [ ] **Step 2: Extend `SearchCompiler.CompileFromOptionsAsync` with offset/surrogate-range parameters**

Add two more trailing optional parameters to the method Task 3 created:

```csharp
public static async Task<SearchTrace> CompileFromOptionsAsync(
    SearchOptions options,
    string resourceType,
    ISymbolResolver resolver,
    ICompartmentDefinitionManager? compartmentDefinitionManager,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager,
    TimeProvider? timeProvider,
    OffsetSpec? offsetPage = null,
    (long Start, long End)? surrogateIdRange = null,
    CancellationToken cancellationToken = default)
```

Thread both straight through to the internal `Lower.Run(...)` call's `offsetPage:`/`surrogateIdRange:` arguments.

- [ ] **Step 3: Write the failing integration test for a basic search**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerCompiledSearchServiceTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _service = /* construct SqlServerCompiledSearchService the same way TestTenantDatabase's other
                       fixtures construct SqlServerFhirRepository -- SqlExecutionService, TenantId,
                       a SqlServerSymbolResolver wrapping the fixture's own cache, ICompartmentDefinitionManager/
                       ISearchParameterDefinitionManager from wherever the EF differential harness sources
                       them (read DifferentialTestHarness.cs and TestTenantDatabase.cs for the real,
                       already-wired instances this codebase uses in tests), a logger */;
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenAResourceMatchingASimplePredicate_WhenSearchStreamAsyncCalled_ThenReturnsItAsAMatch()
    {
        // Arrange -- create a Patient via _database.Repository.CreateOrUpdateAsync (mirroring this
        // project's other integration tests), then build a SearchOptions with Expression matching its
        // _id (a SearchParameterPredicateExpression for _id=<the created resource's id>).
        var options = new SearchOptions { ResourceType = "Patient", Expression = /* _id predicate */ };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Count.ShouldBe(1);
        results[0].SearchMode.ShouldBe(SearchEntryMode.Match);
    }

    [Fact]
    public async Task GivenAQueryThatFailsToCompile_WhenSearchStreamAsyncCalled_ThenThrowsRequestNotValidException()
    {
        // Arrange -- a _lastUpdated partial-precision predicate, per the design doc's confirmed
        // NotSupportedException-at-Lower-time failure mode (ResourceColumnLoweringRule.cs).
        var options = new SearchOptions { ResourceType = "Patient", Expression = /* _lastUpdated with Start != End */ };

        // Act & Assert
        await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var _ in _service.SearchStreamAsync(options, CancellationToken.None)) { }
        });
    }

    [Fact]
    public async Task GivenTwoMatchingResources_WhenCountAsyncCalled_ThenReturnsTwo()
    {
        // Arrange -- create 2 Patients matching a shared predicate.

        // Act
        var count = await _service.CountAsync(/* SearchOptions matching both */, CancellationToken.None);

        // Assert
        count.ShouldBe(2);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerCompiledSearchServiceTests"
```

Expected: FAIL — `SqlServerCompiledSearchService` doesn't exist yet.

- [ ] **Step 5: Implement `SqlServerCompiledSearchService`**

```csharp
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Tracing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Ignixa.DataLayer.SqlServer.Search;

/// <summary>
/// ISearchService implementation driving Ignixa.Search.Sql's compiler (Resolve->Lower->Emit) directly
/// against the SqlServer-native schema. Mirrors SqlEntityFrameworkSearchService's public contract exactly
/// (both cast TSearchOptions to Ignixa.Search.Models.SearchOptions), but executes the compiled T-SQL via
/// ISqlExecutionService instead of EF Core LINQ.
/// </summary>
public sealed class SqlServerCompiledSearchService(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    SqlServerSymbolResolver symbolResolver,
    ICompartmentDefinitionManager compartmentDefinitionManager,
    ISearchParameterDefinitionManager searchParameterDefinitionManager,
    GzipResourceCompressor compressor,
    ILogger logger) : ISearchService
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly SqlServerSymbolResolver _symbolResolver =
        symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
    private readonly ICompartmentDefinitionManager _compartmentDefinitionManager =
        compartmentDefinitionManager ?? throw new ArgumentNullException(nameof(compartmentDefinitionManager));
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager =
        searchParameterDefinitionManager ?? throw new ArgumentNullException(nameof(searchParameterDefinitionManager));
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly int _tenantId = tenantId;

    public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        var trace = await CompileAsync(options, cancellationToken);
        if (trace.Sql is not { } sql)
        {
            throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
        }

        await foreach (var result in ExecuteAndMaterializeAsync(sql, trace.Plan!, cancellationToken))
        {
            yield return result;
        }
    }

    public async ValueTask<int> CountAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken cancellationToken = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        var trace = await CompileAsync(options, cancellationToken, countOnly: true);
        if (trace.Sql is not { } sql)
        {
            throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
        }

        using var command = new SqlCommand(sql.Sql);
        BindParameters(command, sql.Parameters);
        var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt64(0), cancellationToken);
        var count = rows.Count > 0 ? rows[0] : 0L;
        return checked((int)count);
    }

    private async Task<SearchTrace> CompileAsync(SearchOptions options, CancellationToken cancellationToken, bool countOnly = false)
    {
        var resourceType = options.ResourceType ?? throw new NotSupportedException(
            "SqlServerCompiledSearchService requires a single ResourceType -- multi-type/system-level search " +
            "is not yet wired through this adapter (see the design doc's scope).");

        OffsetSpec? offsetPage = null;
        if (!string.IsNullOrWhiteSpace(options.ContinuationToken)
            && Ignixa.Search.Models.ContinuationToken.TryDecode(options.ContinuationToken, out var offset, out var count))
        {
            offsetPage = new OffsetSpec(offset, count);
        }

        (long Start, long End)? surrogateIdRange = options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue
            ? (options.StartSurrogateId.Value, options.EndSurrogateId.Value)
            : null;

        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options with { }, // countOnly is not a SearchOptions field -- see note below
            resourceType,
            _symbolResolver,
            _compartmentDefinitionManager,
            _searchParameterDefinitionManager,
            timeProvider: null,
            offsetPage,
            surrogateIdRange,
            cancellationToken);

        return trace;
    }

    // ... ExecuteAndMaterializeAsync, BindParameters, row-shape branching, decompress -- see Step 6 below.
}
```

**`countOnly` is a real gap in the sketch above, not a placeholder to leave for later — resolve it now.** `SearchCompiler.CompileFromOptionsAsync` (as designed by Task 3/extended by this task's Step 2) has no `countOnly` parameter, but `Lower.Run`'s own `countOnly: bool = false` parameter is exactly what `CountAsync` needs (renders `SELECT COUNT_BIG(...)` instead of row-returning SQL). Add one more trailing parameter to `CompileFromOptionsAsync`, `bool countOnly = false`, threaded straight to the internal `Lower.Run(..., countOnly: countOnly, ...)` call — go back and amend Task 3's/Step 2's method rather than leaving this sketch's `options with { }` no-op in place; that expression does nothing useful and must not survive into the real implementation.

- [ ] **Step 6: Implement execution, row-shape branching, and the corrected `IsMatch`/`IsPartial` mapping**

```csharp
private async IAsyncEnumerable<SearchEntryResult> ExecuteAndMaterializeAsync(
    Ignixa.Search.Sql.Tracing.EmittedSqlTrace sql,
    Ignixa.Search.Sql.Ast.QueryPlan plan,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var command = new SqlCommand(sql.Sql);
    BindParameters(command, sql.Parameters);

    var hasIncludes = plan.Includes is { Count: > 0 };
    var hasSort = plan.Sort is not null;

    var rows = await _sqlExecutionService.ExecuteReaderAsync(
        _tenantId,
        command,
        reader => ReadMatchRow(reader, hasIncludes, hasSort),
        cancellationToken);

    var surrogateIds = rows.Select(r => (r.ResourceTypeId, r.SurrogateId)).ToList();

    foreach (var batch in surrogateIds.Chunk(100))
    {
        var fetched = await FetchResourcesAsync(batch, cancellationToken);
        var fetchedById = fetched.ToDictionary(f => (f.ResourceTypeId, f.SurrogateId));

        foreach (var (resourceTypeId, surrogateId) in batch)
        {
            if (!fetchedById.TryGetValue((resourceTypeId, surrogateId), out var resource))
            {
                _logger.LogWarning("Resource {ResourceTypeId}/{SurrogateId} matched the search but was not found on batch fetch -- likely deleted concurrently.", resourceTypeId, surrogateId);
                continue;
            }

            var matchRow = rows.First(r => r.ResourceTypeId == resourceTypeId && r.SurrogateId == surrogateId);
            yield return new SearchEntryResult(
                ResourceType: resource.ResourceTypeName,
                ResourceId: resource.ResourceId,
                VersionId: resource.Version.ToString(),
                LastModified: resource.LastUpdated,
                ResourceBytes: _compressor.DecompressBytes(resource.RawResource))
            {
                IsDeleted = resource.IsDeleted,
                // IsMatch == 0 -> Include, IsMatch == 1 (or no-includes plan, where every row is
                // implicitly a match, IsMatch column absent) -> Match. Never derive this from IsPartial,
                // which is a truncation marker on included rows, not the include/match discriminator.
                SearchMode = matchRow.IsMatch is false ? SearchEntryMode.Include : SearchEntryMode.Match,
            };
        }
    }
}

private readonly record struct MatchRow(short ResourceTypeId, long SurrogateId, bool? IsMatch);

private static MatchRow ReadMatchRow(SqlDataReader reader, bool hasIncludes, bool hasSort)
{
    var resourceTypeId = reader.GetInt16(0);
    var surrogateId = reader.GetInt64(1);
    var isMatch = hasIncludes ? (bool?)reader.GetBoolean(2) : null;
    return new MatchRow(resourceTypeId, surrogateId, isMatch);
}
```

**This sketch needs the same re-verification discipline as every compiler-facing task in this plan** — `EmittedSql`'s exact column ORDER for the includes-bearing shape is `(T1, Sid1, IsMatch, IsPartial, SortValue0..N)` per `SqlBuilder.Run`'s own doc comment (re-confirm against Task 4's/Task 1's actual landed `SqlBuilder.cs` before finalizing column ordinals) — `IsPartial` at ordinal 3 is read nowhere in the sketch above because this task doesn't need it (it's informational for callers wanting to know if an include stage was truncated; `ISearchService`'s own contract has no field for surfacing that today, so it is legitimately unused here, not an oversight — confirm this against `SearchEntryResult`'s real current fields before assuming). `FetchResourcesAsync`'s real implementation (querying `dbo.Resource` by a batch of `(ResourceTypeId, SurrogateId)` pairs, joined to `dbo.ResourceType` for the type name, `dbo.Transactions` for `LastUpdated`) should mirror `SqlServerHistoryQueryExecutor`'s own row-reading conventions (`SqlDbType` binding, `TryMapHistoryRow`'s try/catch-and-skip pattern for a malformed row) — write it as its own private method, following that established pattern rather than inventing a new one.

- [ ] **Step 7: Implement `BindParameters`**

```csharp
private static void BindParameters(SqlCommand command, IReadOnlyList<Ignixa.Search.Sql.Builders.EmittedSqlParameter> parameters)
{
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerCompiledSearchServiceTests"
```

Expected: PASS, all 3 tests.

- [ ] **Step 9: Run the full integration test suite**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

Expected: PASS, baseline count plus the new tests, zero regressions.

- [ ] **Step 10: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerCompiledSearchService.cs src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerCompiledSearchServiceTests.cs
git commit -m "feat(datalayer-sqlserver): add SqlServerCompiledSearchService (SearchStreamAsync/CountAsync)"
```

---

### Task 9: `SqlServerCompiledSearchService.GetExportRangesAsync`

**Design doc:** Architecture section, `GetExportRangesAsync` paragraph.

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerCompiledSearchService.cs`.
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerCompiledSearchServiceTests.cs`.

**Interfaces:**
- Consumes: `ISqlExecutionService`, `SqlServerSymbolResolver` (for the resource-type-name-to-id lookup).
- Produces: `SqlServerCompiledSearchService`'s third and final `ISearchService` method — this task completes the class.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GivenResourcesAcrossASurrogateIdSpan_WhenGetExportRangesAsyncCalled_ThenReturnsNonOverlappingExhaustiveRanges()
{
    // Arrange -- create 3 Patients (distinct surrogate ids by construction).

    // Act
    var ranges = await _service.GetExportRangesAsync("Patient", numberOfRanges: 2, CancellationToken.None);

    // Assert
    ranges.Count.ShouldBeGreaterThan(0);
    ranges.ShouldAllBe(r => r.StartId <= r.EndId);
    // Ranges are contiguous and exhaustive: each range's start is the previous range's end + 1.
    for (var i = 1; i < ranges.Count; i++)
    {
        ranges[i].StartId.ShouldBe(ranges[i - 1].EndId + 1);
    }
}

[Fact]
public async Task GivenAResourceTypeWithNoResources_WhenGetExportRangesAsyncCalled_ThenReturnsEmpty()
{
    var ranges = await _service.GetExportRangesAsync("Observation", numberOfRanges: 4, CancellationToken.None);
    ranges.ShouldBeEmpty();
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~GetExportRangesAsync"
```

Expected: FAIL — method doesn't exist yet.

- [ ] **Step 3: Implement `GetExportRangesAsync`**

```csharp
public async Task<IReadOnlyList<(long StartId, long EndId)>> GetExportRangesAsync(
    string resourceType,
    int numberOfRanges,
    CancellationToken cancellationToken = default)
{
    var resourceTypeId = await _symbolResolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
    if (resourceTypeId is null)
    {
        _logger.LogWarning("ResourceType not found: {ResourceType}", resourceType);
        return [];
    }

    using var command = new SqlCommand(
        "SELECT MIN(ResourceSurrogateId), MAX(ResourceSurrogateId), COUNT(*) " +
        "FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND IsHistory = 0 AND IsDeleted = 0");
    command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;

    var rows = await _sqlExecutionService.ExecuteReaderAsync(
        _tenantId,
        command,
        reader => (MinId: reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
                   MaxId: reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                   Count: reader.GetInt32(2)),
        cancellationToken);

    var stats = rows.Count > 0 ? rows[0] : (MinId: null, MaxId: null, Count: 0);
    if (stats.Count == 0 || stats.MinId is not { } minId || stats.MaxId is not { } maxId)
    {
        return [];
    }

    var rangeSize = (long)Math.Ceiling((double)(maxId - minId + 1) / numberOfRanges);
    var ranges = new List<(long, long)>();
    var currentStart = minId;

    for (var i = 0; i < numberOfRanges && currentStart <= maxId; i++)
    {
        var currentEnd = i == numberOfRanges - 1 ? maxId : Math.Min(currentStart + rangeSize - 1, maxId);
        ranges.Add((currentStart, currentEnd));
        currentStart = currentEnd + 1;
    }

    return ranges;
}
```

Mirrors `SqlEntityFrameworkSearchService.GetExportRangesAsync`'s exact range-generation algorithm (single min/max/count aggregation, same loop shape) — re-verify this transcription against that method's real current body (re-read at Step 1 of Task 8, still fresh) rather than trusting this plan's copy alone.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~GetExportRangesAsync"
```

Expected: PASS, both tests.

- [ ] **Step 5: Run the full integration test suite**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

Expected: PASS, zero regressions.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerCompiledSearchService.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerCompiledSearchServiceTests.cs
git commit -m "feat(datalayer-sqlserver): add SqlServerCompiledSearchService.GetExportRangesAsync"
```

---

### Task 10: Two-phase missing-value sort executor loop

**Design doc:** §3, the two-phase sort paragraph — read this in full again, it is the single most-corrected section of the design doc (2 review rounds' worth of fixes to its exact formula). Transcribe the corrected formula, do not re-derive it.

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerCompiledSearchService.cs`.
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerCompiledSearchServiceSortTests.cs` (new file).

**Interfaces:**
- Consumes: Task 8's `SqlServerCompiledSearchService`, `SortPhase.Valued`/`SortPhase.MissingPrimary`, Task 3's `CompileFromOptionsAsync`'s implicit use of `SortPhase.Valued` (hard-coded in Task 3's sketch — this task must make it a real, driven loop, not a hard-coded constant).
- Produces: `SearchStreamAsync` correctly returns a full page even when the requested offset straddles the Valued/MissingPrimary boundary.

- [ ] **Step 1: Re-read Task 8's current `CompileAsync`/`ExecuteAndMaterializeAsync` and the design doc's exact corrected formula**

The design doc's final, corrected algorithm (transcribe exactly): run the `Valued` phase at the requested offset first. If it returns at least one row, the phase boundary is inside or past this page and `MissingPrimary`'s offset for this page is `0` — no further work needed. Only when `Valued` returns **zero rows** does the adapter run a `CountOnly` compile of the `Valued` phase to learn the exact `Valued` total, then compute `MissingPrimary`'s offset as `max(0, requestedOffset - valuedTotal)`. Either way, `MissingPrimary`'s fetch limit is `Limit - (rows already returned by Valued)`, not the full requested `Limit`.

This loop only applies when `options.Sort` is non-empty AND the compiled plan's paging is offset-mode (this loop has no meaning for keyset paging, which the compiler already handles correctly in one compile via its own boundary mechanism — do not apply this loop when `options.ContinuationToken` decodes to a keyset token or when there's no sort at all).

- [ ] **Step 2: Write the failing test for a page straddling the phase boundary**

```csharp
[Fact]
public async Task GivenAPageStraddlingTheValuedMissingPrimaryBoundary_WhenSearchStreamAsyncCalled_ThenReturnsExactlyThePageWithNoDuplicatesOrGaps()
{
    // Arrange -- create 10 Patients with a sortable String parameter set (Valued), then 5 more Patients
    // WITHOUT that parameter set (MissingPrimary). Sort ascending by that parameter, page size 5,
    // request offset=8 (straddles: rows 8-9 come from Valued, rows 10-12 come from MissingPrimary).
    var options = new SearchOptions
    {
        ResourceType = "Patient",
        Sort = [/* the String sort parameter, ascending */],
        MaxItemCount = 5,
        ContinuationToken = Ignixa.Search.Models.ContinuationToken.Encode(offset: 8, count: 5),
    };

    // Act
    var results = new List<SearchEntryResult>();
    await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
    {
        results.Add(result);
    }

    // Assert -- exactly 5 rows (2 from the tail of Valued, 3 from the head of MissingPrimary), no
    // duplicates against an adjacent page, no gap.
    results.Count.ShouldBe(5);
    results.Select(r => r.ResourceId).Distinct().Count().ShouldBe(5);
}

[Fact]
public async Task GivenAPageEntirelyWithinMissingPrimary_WhenSearchStreamAsyncCalled_ThenComputesTheCorrectMissingPrimaryOffset()
{
    // Arrange -- same 10 Valued + 5 MissingPrimary setup. Request offset=12, count=5 -- entirely past
    // the Valued phase's 10 rows, needing MissingPrimary offset 2 (12 - 10), not 0 and not 12.
    var options = new SearchOptions
    {
        ResourceType = "Patient",
        Sort = [/* same sort */],
        MaxItemCount = 5,
        ContinuationToken = Ignixa.Search.Models.ContinuationToken.Encode(offset: 12, count: 5),
    };

    // Act
    var results = new List<SearchEntryResult>();
    await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
    {
        results.Add(result);
    }

    // Assert -- exactly 3 rows (rows 12, 13, 14 of the combined 15; MissingPrimary only has 5 total,
    // rows 10-14 in the combined ordering, so offset 12 within MissingPrimary is its own offset 2,
    // yielding its rows 2, 3, 4 -- 3 rows, not 5, since the page runs past the end of all data).
    results.Count.ShouldBe(3);
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~StraddlingTheValued|EntirelyWithinMissingPrimary"
```

Expected: FAIL — today's implementation hard-codes `SortPhase.Valued` and never runs a `MissingPrimary` phase at all, so the straddling test returns only 2 rows (Valued's tail) instead of 5, and the entirely-past test returns 0 rows instead of 3.

- [ ] **Step 4: Implement the two-phase loop**

Replace `CompileAsync`'s single hard-coded-`SortPhase.Valued` call (inside `SearchStreamAsync`, not `CountAsync` — `CountAsync` never pages, so it has no phase-boundary concern at all) with a loop:

```csharp
private async IAsyncEnumerable<SearchEntryResult> SearchStreamWithPhaseHandlingAsync(
    SearchOptions options,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    var resourceType = options.ResourceType ?? throw new NotSupportedException(/* same message as Task 8's CompileAsync */);
    var appliesTwoPhaseSort = options.Sort.Count > 0 && !string.IsNullOrWhiteSpace(options.ContinuationToken);

    if (!appliesTwoPhaseSort)
    {
        var trace = await CompileAsync(options, cancellationToken);
        if (trace.Sql is not { } sql)
        {
            throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
        }

        await foreach (var result in ExecuteAndMaterializeAsync(sql, trace.Plan!, cancellationToken))
        {
            yield return result;
        }

        yield break;
    }

    Ignixa.Search.Models.ContinuationToken.TryDecode(options.ContinuationToken!, out var requestedOffset, out var requestedCount);

    var valuedTrace = await CompileAsync(options, cancellationToken, sortPhase: SortPhase.Valued);
    if (valuedTrace.Sql is not { } valuedSql)
    {
        throw new RequestNotValidException(valuedTrace.Failure?.Message ?? "The search could not be compiled.");
    }

    var valuedResults = new List<SearchEntryResult>();
    await foreach (var result in ExecuteAndMaterializeAsync(valuedSql, valuedTrace.Plan!, cancellationToken))
    {
        valuedResults.Add(result);
        yield return result;
    }

    if (valuedResults.Count > 0)
    {
        yield break; // Boundary is inside or past this page; MissingPrimary offset for THIS page is 0 -- nothing further to fetch.
    }

    var valuedCountOptions = options with { ContinuationToken = null };
    var valuedCountTrace = await CompileAsync(valuedCountOptions, cancellationToken, countOnly: true, sortPhase: SortPhase.Valued);
    var valuedTotal = /* execute valuedCountTrace.Sql the same way CountAsync does, extract the scalar */;

    var missingPrimaryOffset = Math.Max(0, requestedOffset - valuedTotal);
    var missingPrimaryLimit = requestedCount; // Valued contributed 0 rows to this page, so the full requested count is still needed.
    var missingPrimaryOptions = options with
    {
        ContinuationToken = Ignixa.Search.Models.ContinuationToken.Encode(missingPrimaryOffset, missingPrimaryLimit),
    };

    var missingTrace = await CompileAsync(missingPrimaryOptions, cancellationToken, sortPhase: SortPhase.MissingPrimary);
    if (missingTrace.Sql is not { } missingSql)
    {
        throw new RequestNotValidException(missingTrace.Failure?.Message ?? "The search could not be compiled.");
    }

    await foreach (var result in ExecuteAndMaterializeAsync(missingSql, missingTrace.Plan!, cancellationToken))
    {
        yield return result;
    }
}
```

This requires `CompileAsync` (Task 8) to gain a `sortPhase: SortPhase = SortPhase.Valued` parameter, threaded to `SearchCompiler.CompileFromOptionsAsync`'s own `sortPhase` argument (which Task 3's original sketch also hard-coded to `SortPhase.Valued` — fix that hard-coding now, since this is the first task that actually needs to vary it). `SearchStreamAsync` (Task 8) should now call `SearchStreamWithPhaseHandlingAsync` instead of its own inline compile-and-execute logic.

**The `results.Count.ShouldBe(3)` assertion in Step 2's second test encodes a specific, worked-out arithmetic example — verify it by hand-tracing the algorithm above against that test's exact Arrange data before trusting it; if the real numbers don't match, fix the test's Arrange/Assert, not the algorithm (which is the design doc's own twice-reviewed formula).**

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~StraddlingTheValued|EntirelyWithinMissingPrimary"
```

Expected: PASS, both tests.

- [ ] **Step 6: Run the full integration test suite**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

Expected: PASS, zero regressions (including Task 8's own tests, which don't use sort+pagination together and so should be unaffected by this loop's new branch).

- [ ] **Step 7: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerCompiledSearchService.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerCompiledSearchServiceSortTests.cs
git commit -m "feat(datalayer-sqlserver): implement two-phase missing-value sort executor loop"
```

---

### Task 11: Differential harness — leaf/composite types, count, `:missing`

**Design doc:** Differential harness section.

**Files:**
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/CompiledSearchDifferentialTests.cs`.

**Interfaces:**
- Consumes: Task 8's `SqlServerCompiledSearchService`, `DifferentialTestHarness` (existing, Phase D's pattern — read it in full again, already done during this plan's own research).
- Produces: a proven-clean differential baseline for every leaf/composite search-parameter type, `:missing`, and count — the first of 3 harness tasks (chain/include/compartment is Task 12, sort/paging is Task 13).

- [ ] **Step 1: Extend `DifferentialTestHarness` (or a sibling helper) to construct both search services**

`DifferentialTestHarness` today wires `LegacyRepository`/`NewRepository` (write-path comparison, Phase D). This task needs the equivalent for SEARCH: `LegacySearchService` (`SqlEntityFrameworkSearchService`, wired against `_legacyDatabase`) and `NewSearchService` (`SqlServerCompiledSearchService`, wired against `_newDatabase`). Read `DifferentialTestHarness.CreateAsync`'s existing construction pattern for both databases' repositories and mirror it for both search services — add `LegacySearchService`/`NewSearchService` properties and construct them inside `CreateAsync`, following the exact same "construct database A's real production wiring, then database B's" structure this class already establishes. This is a modification to the shared harness class, not a new one — keep it in `DifferentialTestHarness.cs` since every future differential-search test (Tasks 11-13) needs it.

- [ ] **Step 2: Write a result-comparison helper**

```csharp
private static void AssertSameResults(IReadOnlyList<SearchEntryResult> legacy, IReadOnlyList<SearchEntryResult> @new)
{
    legacy.Count.ShouldBe(@new.Count);
    var legacyIds = legacy.Select(r => (r.ResourceType, r.ResourceId)).OrderBy(x => x).ToList();
    var newIds = @new.Select(r => (r.ResourceType, r.ResourceId)).OrderBy(x => x).ToList();
    legacyIds.ShouldBe(newIds);
}
```

(A set-membership comparison, not an exact-order comparison — order is a separate concern Task 13's sort tests own.)

- [ ] **Step 3: Write leaf-type differential tests**

One test per leaf type this compiler supports (string, token, reference, uri, number, quantity, date — 7 types), each creating 2-3 resources where a known subset matches a specific predicate, running the SAME `SearchOptions` through both `LegacySearchService.SearchStreamAsync` and `NewSearchService.SearchStreamAsync`, asserting `AssertSameResults`. Example for one type (repeat the shape for the other 6, varying only the resource/parameter/value):

```csharp
[Fact]
public async Task GivenATokenSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- create 2 Patients via harness.LegacyRepository AND harness.NewRepository with
    // identical content (a shared helper each future differential test can reuse -- read whether
    // Phase D's own write-path tests already have one).
    var options = new SearchOptions { ResourceType = "Patient", Expression = /* token predicate matching 1 of 2 */ };

    // Act
    var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
    var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

    // Assert
    AssertSameResults(legacyResults, newResults);
}
```

- [ ] **Step 4: Write composite-type differential tests**

One test per composite type this compiler supports (token-token, token-number-number, token-string, token-quantity, token-date, reference-token — 6 types), same shape as Step 3.

- [ ] **Step 5: Write `:missing` differential tests**

One test for a leaf parameter's `:missing=true`/`:missing=false`, plus one for a **composite** parameter's `:missing` — this second one is a KNOWN, EXPECTED DIVERGENCE per the design doc: legacy has no `Composite` arm in `ApplyMissingSearchParameterExpressionAsync` and returns empty with a logged warning, while the compiler returns real results. Assert the divergence explicitly, do not use `AssertSameResults` for this one case:

```csharp
[Fact]
public async Task GivenACompositeParametersMissingModifier_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- create a resource missing a composite parameter's value.
    var options = new SearchOptions { ResourceType = "Observation", Expression = /* composite :missing=true */ };

    // Act
    var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
    var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

    // Assert -- documented divergence per the design doc: legacy has no Composite arm (returns empty
    // with a warning log), the compiler returns real results.
    legacyResults.ShouldBeEmpty();
    newResults.ShouldNotBeEmpty();
}
```

- [ ] **Step 6: Write count differential tests**

```csharp
[Fact]
public async Task GivenAMatchingSetOfResources_WhenCountedOnBothEngines_ThenReturnsTheSameCount()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- create 3 matching resources.
    var options = new SearchOptions { ResourceType = "Patient", Expression = /* predicate matching all 3 */ };

    // Act
    var legacyCount = await harness.LegacySearchService.CountAsync(options, CancellationToken.None);
    var newCount = await harness.NewSearchService.CountAsync(options, CancellationToken.None);

    // Assert
    legacyCount.ShouldBe(newCount);
}
```

- [ ] **Step 7: Run all new tests**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~CompiledSearchDifferentialTests"
```

Expected: PASS, all leaf/composite/`:missing`/count tests. **Any test that fails here because the two engines genuinely disagree, on a shape NOT in the design doc's known-divergence list, is a real compiler bug — per the design doc's non-negotiable instruction, fix it in `Ignixa.Search.Sql`, re-run this task's tests, never special-case it in the harness or the adapter.**

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/DifferentialTestHarness.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/CompiledSearchDifferentialTests.cs
git commit -m "test(datalayer-sqlserver): differential harness for leaf/composite search types, :missing, count"
```

---

### Task 12: Differential harness — chain, include/revinclude, compartment

**Design doc:** Differential harness section (the `_include`/compartment known-divergence entries specifically).

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/CompiledSearchDifferentialTests.cs` (or a sibling file if this makes the single file too large — split if it does, matching this initiative's own established practice of splitting oversized files).

**Interfaces:**
- Consumes: Task 11's harness extensions.
- Produces: a proven-clean differential baseline for chain, include/revinclude (+`:iterate` within one hop), and compartment search.

- [ ] **Step 1: Write chain differential tests**

Forward and reverse chain, one test each (e.g. `Observation?subject:Patient.name=Smith` and its reverse), same `AssertSameResults` shape as Task 11.

- [ ] **Step 2: Write single-type `_include`/`_revinclude` differential tests**

Standard single-type includes (using a real, single non-null `ResourceType` in `SearchOptions`) — per the design doc's corrected finding, `BuildIncludeQuery` (the single-type streaming path) ALREADY filters by `SearchParamId` correctly, so these should show NO divergence; assert `AssertSameResults` normally, not the divergence pattern.

- [ ] **Step 3: Write the multi-type `_include`'s `SearchParamId`-filter known-divergence test**

```csharp
[Fact]
public async Task GivenAMultiTypeWildcardIncludeOverlappingAnUnrelatedReferenceParameter_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- a multi-type (ResourceType null/empty) search with _include=*, where two DIFFERENT
    // reference search parameters both point at a shared target resource type, such that an unfiltered
    // include (legacy's multi-type IncludeProcessor path) pulls in a resource the compiler's
    // SearchParamId-filtered version correctly excludes.

    // Act & Assert -- documented divergence: IncludeProcessor (multi-type path only) never filters by
    // SearchParamId; the compiler filters correctly. This is the ONLY include shape where this
    // divergence is expected -- do not generalize this assertion to single-type includes (Step 2 already
    // covers those, and asserts equivalence there, not divergence).
}
```

- [ ] **Step 4: Write single-hop `:iterate` differential tests**

One hop only (per the design doc's explicit scope boundary — the compiler doesn't support recursion beyond one hop, and this harness's query set must not exercise that).

- [ ] **Step 5: Write compartment search differential tests, including the `ReferenceResourceTypeId` known-divergence test**

```csharp
[Fact]
public async Task GivenACompartmentSearchWithANaturalIdCollisionAcrossResourceTypes_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- two resources of DIFFERENT types sharing the same natural ResourceId value, one of
    // which is a genuine compartment member and one of which is not, such that legacy's
    // ReferenceResourceTypeId-blind CompartmentSearchQueryGenerator incorrectly includes both while the
    // compiler's CompartmentSource correctly includes only the real member.

    // Act & Assert -- documented divergence: compiler is right (filters ReferenceResourceTypeId), legacy
    // is wrong (doesn't). Assert new count < legacy count for this specific collision case.
}
```

Also write an ordinary, non-colliding compartment search test asserting normal `AssertSameResults` equivalence (most compartment searches have no such collision and should show no divergence).

- [ ] **Step 6: Run all new tests**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~CompiledSearchDifferentialTests"
```

Expected: PASS. Same "any undocumented divergence is a real bug" instruction as Task 11.

- [ ] **Step 7: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/
git commit -m "test(datalayer-sqlserver): differential harness for chain, include/revinclude, compartment"
```

---

### Task 13: Differential harness — sort, paging, `_lastUpdated` partial-precision

**Design doc:** Differential harness section (the missing-value sort-order and `_lastUpdated` partial-precision known-divergence entries).

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/CompiledSearchDifferentialTests.cs` (or its sibling, per Task 12's own split decision).

**Interfaces:**
- Consumes: Task 10's two-phase sort executor loop, Task 6's `_sort=_id` fix.
- Produces: a proven-clean differential baseline for sort (including `_sort=_id`), offset paging across a page boundary, and the `_lastUpdated` partial-precision known divergence — the last harness task before cutover.

- [ ] **Step 1: Write descending-sort differential tests (no missing values, no divergence expected)**

A sort where every resource has the sorted parameter present — should show `AssertSameResults`-style equivalence (with order asserted too, unlike Tasks 11-12's set-based comparisons: compare the two result lists' `ResourceId` sequences directly, not just as sets).

- [ ] **Step 2: Write the missing-value sort-order known-divergence test**

```csharp
[Fact]
public async Task GivenAnAscendingSortWithSomeResourcesMissingTheSortKey_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDivergeInOrder()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- 2 resources WITH the sort parameter set, 2 WITHOUT, ascending sort.

    // Act
    var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
    var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

    // Assert -- documented divergence: legacy sorts NULL/missing keys FIRST in ascending (SQL Server
    // default); the compiler's two-phase model always places missing-value rows LAST regardless of
    // direction. Same 4 resources on both sides (set-equal), but the FIRST result differs: legacy's
    // first result has a missing sort key, the compiler's first result has a valued one.
    legacyResults.Select(r => r.ResourceId).OrderBy(x => x).ShouldBe(newResults.Select(r => r.ResourceId).OrderBy(x => x));
    legacyResults[0].ResourceId.ShouldNotBe(newResults[0].ResourceId);
}
```

- [ ] **Step 3: Write `_sort=_id` differential tests**

Now safe to include per Task 6's fix — assert `AssertSameResults` WITH order (legacy's native `ResourceId` ordering should match the compiler's new `SortKeyKind.ResourceId` join-based ordering exactly, since both order by the same underlying string column).

- [ ] **Step 4: Write offset-paging-across-a-page-boundary differential tests**

Create N resources (N large enough to span 2+ pages at a small `MaxItemCount`), request page 1, then decode/re-encode a continuation token for page 2 (mirroring how the real Application-layer handler would), assert page 2's results on both engines match and that page 1 + page 2 together cover all N resources with no duplicates or gaps — this is the paging equivalent of Task 10's straddling test, but run against BOTH engines for genuine differential proof (Task 10 only proved the new engine's own internal correctness against itself).

- [ ] **Step 5: Write the `_lastUpdated` partial-precision known-divergence test**

```csharp
[Fact]
public async Task GivenAPartialPrecisionLastUpdatedSearch_WhenSearchedOnBothEngines_ThenLegacySucceedsAndCompiledThrowsRequestNotValidException()
{
    await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

    // Arrange -- a _lastUpdated=2026 (year-only) search, which flattens to a single instant on the
    // legacy path but has Start != End on the compiler's typed IR.
    var options = new SearchOptions { ResourceType = "Patient", Expression = /* _lastUpdated partial-precision */ };

    // Act
    var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));

    // Assert -- documented divergence: legacy silently flattens and searches only that single instant
    // (returns SOME result, possibly wrong/incomplete but doesn't throw); the compiler throws
    // RequestNotValidException naming ResourceColumnLoweringRule's specific message.
    var ex = await Should.ThrowAsync<RequestNotValidException>(async () =>
    {
        await foreach (var _ in harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None)) { }
    });
    ex.Message.ShouldContain("_lastUpdated only supports an exact instant");
}
```

- [ ] **Step 6: Run all new tests**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~CompiledSearchDifferentialTests"
```

Expected: PASS. Same "any undocumented divergence is a real bug" instruction as Tasks 11-12.

- [ ] **Step 7: Run the FULL differential suite one more time, all 3 tasks' tests together**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

Expected: PASS, entire suite green. This is the acceptance gate the design doc names explicitly — Task 14 (cutover) does not start until this is unambiguously true.

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/
git commit -m "test(datalayer-sqlserver): differential harness for sort, paging, _lastUpdated partial-precision"
```

---

### Task 14: Cutover

**Design doc:** Cutover section. **Do not start this task until Task 13's differential suite is unambiguously green — this is the design doc's own explicit, non-negotiable sequencing gate.**

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs` — add a search-service construction method.
- Modify: whichever composition root currently decides `ISearchServiceFactory`'s storage-type dispatch (re-verify at Step 1 — the design doc names `SqlEntityFrameworkRepositoryFactory`'s `createSearchService` closure as the current owner, same file sub-project 2's Task 3 already modified for the write-path composition root).
- Test: full E2E suite re-run.

**Interfaces:**
- Consumes: every prior task's output.
- Produces: `SqlServerCompiledSearchService` is the live search path for every SqlServer-storage tenant.

- [ ] **Step 1: Re-read the current search-service composition root**

```bash
grep -n "createSearchService\|ISearchServiceFactory\|GetSearchServiceAsync" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
cat src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs
```

Confirm exactly how `SqlEntityFrameworkSearchService` is constructed today and what storage-type gate the write path's own cutover (`CreateServiceFactory`, sub-project 2) already established — mirror that exact gate for search, per the design doc's "same storage-type gate the write-path cutover already uses, no feature flag" instruction.

- [ ] **Step 2: Add a `CreateSearchService` method to `SqlServerRepositoryFactory`**

```csharp
public static ISearchService CreateSearchService(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    SqlServerSearchIndexReferenceDataCache cache,
    ICompartmentDefinitionManager compartmentDefinitionManager,
    ISearchParameterDefinitionManager searchParameterDefinitionManager,
    RecyclableMemoryStreamManager memoryStreamManager,
    ILoggerFactory loggerFactory)
{
    var compressor = new GzipResourceCompressor(memoryStreamManager);
    var symbolResolver = new SqlServerSymbolResolver(cache);

    return new SqlServerCompiledSearchService(
        sqlExecutionService,
        tenantId,
        symbolResolver,
        compartmentDefinitionManager,
        searchParameterDefinitionManager,
        compressor,
        loggerFactory.CreateLogger<SqlServerCompiledSearchService>());
}
```

Re-verify `SqlServerCompiledSearchService`'s real final constructor parameter list (Task 8) before pasting this — it may have gained/lost parameters during Tasks 9-10's own edits to that class.

- [ ] **Step 3: Wire the cutover into the existing composition root**

Following whatever exact shape Step 1 found, change the storage-type dispatch so SqlServer-storage tenants construct `SqlServerCompiledSearchService` via `SqlServerRepositoryFactory.CreateSearchService(...)` instead of the EF project's own `createSearchService` closure — unconditionally, no feature flag, matching the write path's own precedent. The EF project's `createSearchService` closure and `SqlEntityFrameworkSearchService` are left in place, untouched, for any non-SqlServer storage type.

- [ ] **Step 4: Build and run the full solution test suite**

```bash
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

Expected: 0 warnings, 0 errors, 0 failures.

- [ ] **Step 5: Run the full E2E suite**

```bash
dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```

Expected: PASS, matching this session's established practice of a full E2E re-run for anything touching the live search path. If any E2E test fails, treat it with the same rigor as the differential harness — a real regression to fix, not something to special-case or skip.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
git commit -m "feat(datalayer-sqlserver): hard-cut-over SqlServerCompiledSearchService for SqlServer-storage tenants"
```

---

## Post-Plan

After all 14 tasks: dispatch the final whole-branch review (most capable model available, per this initiative's standing practice) covering the full diff from this plan's base commit to its tip. Update the roadmap/ledger to record this sub-project's completion. This completes the full 3-sub-project "Phase E" initiative (compiler feature-parity → DataLayer prerequisites → this adapter).
