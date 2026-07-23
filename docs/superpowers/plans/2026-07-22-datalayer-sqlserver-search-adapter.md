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
- Modify: `src/Application/Ignixa.Domain/Abstractions/ISearchService.cs` (**correction: all 3 members use `CancellationToken ct = default` today, including `GetExportRangesAsync` — the earlier draft of this brief incorrectly claimed `GetExportRangesAsync` was already correct; it isn't, confirmed by direct inspection. Rename all 3.**).
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSearchService.cs` (rename `ct` → `cancellationToken` throughout — both the 2 public method signatures and every internal usage of the parameter).
- Modify: `src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedSearchService.cs` (**correction: all 4 public methods — `SearchAsync`, `SearchStreamAsync`, `CountAsync`, and `GetExportRangesAsync` — use `CancellationToken ct = default` today; none is already correct. Rename all 4.**).
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

Same treatment for `SearchAsync`, `SearchStreamAsync`, `CountAsync`, **and `GetExportRangesAsync`** (all 4 use `ct` today — do not skip the last one on the assumption it's already correct).

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
- Modify: `src/Core/Ignixa.Search.Sql/Tracing/SearchTrace.cs` (adds a `CompiledPlan` field — see Step 5a).
- Test: `test/Ignixa.Search.Sql.Tests/Tracing/EmittedSqlTraceParametersTests.cs` (new file, Step 3).
- Test: `test/Ignixa.Search.Sql.Tests/Tracing/SearchCompilerCompileFromOptionsTests.cs` (new file, Step 6 — directly tests the new entry point; a prior draft of this brief named this file in the Files list but never actually created it, leaving `CompileFromOptionsAsync` untested until Task 8 used it two tasks later. Fixed below.).

**Interfaces:**
- Consumes: Task 1's reconciled `SearchCompiler.CompileWithTimeProviderAsync` (post-merge shape — re-verify its exact current signature before writing this task's code; the sketch below is based on `origin/main`'s pre-reconciliation shape and may shift slightly depending on how Task 1's merge landed).
- Produces: `SearchCompiler.CompileFromOptionsAsync(SearchOptions options, string? resourceType, ISymbolResolver resolver, ICompartmentDefinitionManager? compartmentDefinitionManager, ISearchParameterDefinitionManager? searchParameterDefinitionManager, TimeProvider? timeProvider, CancellationToken cancellationToken) : Task<SearchTrace>` — **`resourceType` is nullable, not `string`, deliberately.** `Resolve.RunAsync`/`Lower.Run` both already accept `string? targetResourceType` for multi-type/system-level search (`systemLevelSearch: true`), and this entry point must support that too — a caller passing `null`/empty `resourceType` (a multi-type search) is a real, supported case, not an error this method should reject. (Task 8's adapter is the concrete caller that needs this — see its own Step 2 for how it computes `systemLevelSearch` from the same value.) Task 8's `SqlServerCompiledSearchService` is this method's first production caller. `EmittedSqlTrace` gains a `Parameters` property — Task 8 reads `SearchTrace.Sql!.Parameters` to bind `@pN` placeholders at execution time. `SearchTrace` gains a `QueryPlan? CompiledPlan` property (populated from the same `lowered.Plan` this method already has in scope) — Task 8 and Task 10 read `trace.CompiledPlan!.Includes`/`.Sort` directly to pick the correct result-row shape, rather than inferring it from `SearchOptions` (which can diverge from what `Lower` actually produced — e.g. `BuildIncludeStages` silently drops a degenerate stage and returns null even when `options.Include` is non-empty).

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
    string? resourceType,
    ISymbolResolver resolver,
    ICompartmentDefinitionManager? compartmentDefinitionManager,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager,
    TimeProvider? timeProvider,
    CancellationToken cancellationToken = default)
{
    // resourceType is deliberately NOT null-checked here -- null/empty means a multi-type/system-level
    // search, a real supported case (see this task's Interfaces note), not a caller error.
    ArgumentNullException.ThrowIfNull(options);
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

    // Declared here, not inside the `if` block below, even though it's only ever assigned inside it --
    // the final `return`'s `CompiledPlan = lowered?.Plan` needs it in scope whether or not that block
    // ran at all (an unresolved parameter skips the block entirely and this stays null, which is
    // correct: no Lower call means no plan to report).
    LoweredPlan? lowered = null;

    if (resolved.Unresolved.Count == 0)
    {
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
                systemLevelSearch: string.IsNullOrEmpty(resourceType),
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
        CompiledPlan = lowered?.Plan,
    };
}
```

**This code assumes Task 1 landed the merged `Lower.Run` signature from Step 2a of Task 1's brief exactly (both `systemLevelSearch` and `approximationReferenceTime` present) and that `MarkKnownMisses` exists (added by PR #353's merge) — re-verify both before pasting this in.** `DetectImplicit`'s existing signature takes `(IReadOnlyList<QueryParameter> parameters, SearchOptions options)` — since this entry point has no raw `QueryParameter` list (that's the whole point), either add an overload of `DetectImplicit` that only reads `options` (its `parameters`-derived `supplied` set exists solely to detect *_count/_total were explicitly supplied*, information not available or needed here — a pre-built `SearchOptions` has no notion of "was this explicitly supplied" the trace can recover, so the simplest correct fix is skipping implicit-detection for this entry point and returning `Implicit = []` on this trace) — re-read `DetectImplicit`'s real current body (Task 1 may have changed it) before deciding; the sketch above assumes a `DetectImplicit(SearchOptions)` overload exists or is trivial to add, but confirm this doesn't silently misreport `_count`/`_total` as always-implicit when they were genuinely user-supplied. If in doubt, return `Implicit = []` explicitly with a one-line comment explaining pre-built `SearchOptions` doesn't carry supplied-ness, rather than guessing.

- [ ] **Step 5a: Add `CompiledPlan` to `SearchTrace`**

`SearchTrace`'s own doc comment already explains why `Failure`/`Implicit` sit outside its positional constructor: "the constructor stays the four always-meaningful fields, so a further optional field can be added without touching every construction site." Add `CompiledPlan` the same way — an init-only property, not a fifth positional parameter, so no existing construction site (in `Ignixa.Search.Sql.Tests`) needs to change.

**This step also widens `ResourceType` from `string` to `string?`.** `CompileFromOptionsAsync` (Step 5) passes its own `resourceType` parameter straight through to `new SearchTrace(resourceType, ...)`, and that parameter is deliberately nullable (a multi-type/system-level search has no single resource type — see this task's Interfaces note). Under this project's nullable-enabled, `TreatWarningsAsErrors` build, passing a `string?` into a non-nullable positional `string ResourceType` is a real CS8604 build error, not a style nit — leaving `ResourceType` non-nullable here would make Step 5's own code fail to build. Confirmed safe: grepped this plan for every place a `SearchTrace.ResourceType` is read, found none outside this file's own construction — no other task's code needs updating for this widening.

```csharp
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tracing;

public sealed record SearchTrace(
    string? ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,
    EmittedSqlTrace? Sql)
{
    public TraceFailure? Failure { get; init; }

    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];

    /// <summary>
    /// The real <see cref="QueryPlan"/> Lower produced, or null when compilation stopped before Lower ran.
    /// Declared outside the positional list for the same reason as <see cref="Failure"/>/<see cref="Implicit"/>.
    /// A production caller that needs to branch on the plan's own structure (e.g. whether <c>Includes</c> or
    /// <c>Sort</c> is populated, to pick the right result-row shape) reads this directly, rather than
    /// re-deriving it from the caller's own <c>SearchOptions</c> — <c>Lower.BuildIncludeStages</c> can drop a
    /// degenerate stage and return null even when the caller's <c>options.Include</c> is non-empty, so the
    /// two can diverge; <see cref="QueryPlanTrace"/> (<see cref="Plan"/>) is a display-only projection with
    /// no <c>Includes</c>/<c>Sort</c> structure of its own and cannot substitute for this.
    /// </summary>
    public QueryPlan? CompiledPlan { get; init; }
}
```

Add the `Ignixa.Search.Sql.Ast` `using` if not already present (needed for the `QueryPlan` type). Leave `CompileWithTimeProviderAsync`'s own construction site unchanged for now — this field is genuinely optional and only `CompileFromOptionsAsync` needs to populate it for this sub-project's purposes; retrofitting `CompileWithTimeProviderAsync` to also set it is a one-line, zero-risk addition but not required by anything in this plan, so leave it out unless it's free to add while you're already in this file (if you do add it, it's simply `CompiledPlan = lowered?.Plan` on that method's own `return new SearchTrace(...)` too, for consistency — your call, either is correct).

- [ ] **Step 6: Write a direct test for `CompileFromOptionsAsync`**

The design doc requires this entry point to be provably correct standalone, before Task 8 becomes its first production caller two tasks later. Create `test/Ignixa.Search.Sql.Tests/Tracing/SearchCompilerCompileFromOptionsTests.cs`:

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Tracing;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

public class SearchCompilerCompileFromOptionsTests
{
    [Fact]
    public async Task GivenAnAlreadyBuiltSearchOptions_WhenCompiledFromOptions_ThenTheTraceHasSqlParametersAndCompiledPlan()
    {
        // Arrange -- reuse this test file's sibling SearchTraceFixtures.cs helpers (the same fake
        // ISymbolResolver/ICompartmentDefinitionManager/ISearchParameterDefinitionManager this project's
        // other tracing tests already use) to build a SearchOptions equivalent to Patient?_id=abc by hand
        // -- i.e. skip SearchOptionsBuilder.Build entirely and construct the SearchOptions object directly,
        // proving this entry point genuinely does not need a QueryParameter list or an ISearchOptionsBuilder.
        var resolver = /* existing fixture's fake ISymbolResolver, resolving "_id" and "Patient" */;
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = /* the same _id=abc SearchParameterExpression shape EmittedSqlTraceParametersTests.cs's fixture builds */,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Patient",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            CancellationToken.None);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain("@p0");
        trace.Sql.Parameters.ShouldNotBeEmpty();
        trace.CompiledPlan.ShouldNotBeNull();
        trace.CompiledPlan!.Match.ShouldNotBeNull();
    }
}
```

Fill in the two placeholder lines with the real fixture-construction calls from `SearchTraceFixtures.cs` / `EmittedSqlTraceParametersTests.cs` (Step 3) — both tests need the same resolved `_id`/`Patient` symbols, so the fake resolver setup should be identical or trivially shared.

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

Expected: PASS on both net9.0 and net10.0, including both new tests (`EmittedSqlTraceParametersTests` and `SearchCompilerCompileFromOptionsTests`), zero regressions to the existing tracing suite.

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs src/Core/Ignixa.Search.Sql/Tracing/EmittedSqlTrace.cs src/Core/Ignixa.Search.Sql/Tracing/SearchTrace.cs test/Ignixa.Search.Sql.Tests/Tracing/EmittedSqlTraceParametersTests.cs test/Ignixa.Search.Sql.Tests/Tracing/SearchCompilerCompileFromOptionsTests.cs
git commit -m "feat(search-sql): add SearchCompiler.CompileFromOptionsAsync, carry EmittedSqlTrace.Parameters and SearchTrace.CompiledPlan"
```

---

### Task 4: Offset-based paging in `Lower`/`Emit`

**Design doc:** §3 (read this section in full before starting — it went through 2 rounds of Fable review fixing real math errors in the two-phase sort disambiguation; transcribe its final, corrected formula exactly, do not re-derive it from scratch).

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs` (or wherever `PageSpec` ends up living post-Task-1 — re-verify) — add an `OffsetSpec` type.
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs` — add an `OffsetPage` field **and a `CountPhaseScoped` field (Step 8a)**.
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` — add the new `offsetPage` parameter, its pairwise-exclusion guard, **and a new `countPhaseScoped` parameter with its own guard (Step 8a)**.
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` — render `OFFSET ... FETCH NEXT`, **and extend the `CountOnly` branch to respect `CountPhaseScoped` (Step 8a)**.
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/OffsetPagingGuardTests.cs` (new file — **corrected from an earlier draft of this brief, which named `Builders/SqlBuilderOffsetPagingTests.cs` here; Step 4 below actually creates the file at this corrected path**), `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (add cases), `test/Ignixa.Search.Sql.Tests/Builders/SqlBuilderCountPhaseScopedTests.cs` (new file, Step 8a).

**Interfaces:**
- Consumes: Task 1's reconciled `Lower.Run`/`SqlBuilder.Run`.
- Produces: `OffsetSpec(int Offset, int Limit)` record; `Lower.Run`'s new `offsetPage: OffsetSpec? = null` parameter; `QueryPlan.OffsetPage`. Task 8's `SqlServerCompiledSearchService` constructs an `OffsetSpec` from the decoded `Ignixa.Search.Models.ContinuationToken` and drives the two-phase sort executor loop described below. **Also produces: `Lower.Run`'s new `countPhaseScoped: bool = false` parameter and `QueryPlan.CountPhaseScoped` — Task 10's two-phase sort executor loop uses `countPhaseScoped: true` to learn exactly how many rows the `Valued` phase's own join produces (a materially different question than the existing unscoped `countOnly`, which intentionally counts the WHOLE match set regardless of sort — see Step 8a for why these cannot be the same flag).**

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

The `CountOnly` shape needs no change for `OffsetPage` — `OffsetPage` is meaningless for a count query (the design doc's adapter never sets both `countOnly: true` and an `offsetPage` in the same compile call; add a defensive guard in `Lower.Run` too if one doesn't already exist for the analogous `page`/`countOnly` combination — check first, this may already be guarded). It DOES need a change for `CountPhaseScoped` — see Step 8a.

- [ ] **Step 8a: Add `countPhaseScoped` — a distinct, narrower capability `CountOnly` alone cannot provide**

**Why this can't just be the existing `CountOnly` flag:** `SqlBuilder`'s `CountOnly` branch deliberately ignores `plan.Sort`/`plan.Page` entirely — this is not an oversight, it's how `_total=accurate` combined with `_sort=X` correctly reports the TRUE total match count (the whole match set), not a sort-phase subset. Task 10's two-phase sort executor loop needs a genuinely different number: "how many rows would the `Valued` phase's own join produce" (a subset — only rows where the primary sort key is present), used to disambiguate the `MissingPrimary` phase's correct offset when the `Valued` phase's own page comes back short. Changing `CountOnly` to always respect sort would silently break the existing, tested `_total=accurate&_sort=X` composition (a real regression, not this task's to make). So this needs a new, separate, explicitly opt-in flag — `countPhaseScoped` — defaulting to `false`, preserving every existing caller's behavior byte-for-byte.

Add `countPhaseScoped: bool = false` as a new trailing optional parameter on `Lower.Run` (after `offsetPage`, append-only). Guard it — it is only meaningful paired with `countOnly: true` and a non-empty `sort`, since "which phase" has no meaning without both:

```csharp
if (countPhaseScoped && !(countOnly && sort.Count > 0))
{
    throw new ArgumentException(
        "countPhaseScoped is only meaningful combined with countOnly: true and a non-empty sort -- it asks " +
        "'how many rows would this specific sort phase's own join produce', not the whole match set's count " +
        "(that's what countOnly alone already does, unconditionally). Without both, there is no phase to " +
        "scope the count to.",
        nameof(countPhaseScoped));
}
```

Thread `countPhaseScoped` through to `new QueryPlan(..., CountPhaseScoped: countPhaseScoped)`. Add `bool CountPhaseScoped = false` as a new trailing optional field on `QueryPlan`'s record declaration (append-only, same pattern as every other additive field).

In `SqlBuilder.cs`'s `plan.CountOnly` branch, apply the sort-phase's own join/filter construction (the SAME construction the non-count paths already use — `EmitSortJoins`, `EmitMissingPrimaryFilter`) when `plan.CountPhaseScoped` is also true, but skip `ORDER BY`/paging entirely (a count query never orders or pages). Replace the branch's body with:

```csharp
if (plan.CountOnly)
{
    writer.Append(";WITH ");
    writer.AppendJoin(",\n", cteBlocks, CteLabel, SqlRangeKind.Cte);
    writer.Append("\n");

    var countSortJoins = plan.CountPhaseScoped ? EmitSortJoins(plan.Sort) : string.Empty;
    writer.Append($"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM {CteLabel(plan.Match.Index)} m{countSortJoins}");

    var countWhereClauses = new List<string>();
    if (plan.OuterPredicate is not null)
    {
        countWhereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
    }

    if (plan.CountPhaseScoped && plan.Sort is { Phase: SortPhase.MissingPrimary })
    {
        countWhereClauses.Add(EmitMissingPrimaryFilter(plan.Sort));
    }

    if (countWhereClauses.Count > 0)
    {
        var resourceJoin = plan.OuterPredicate is null
            ? string.Empty
            : "\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
        writer.Append(resourceJoin);
        writer.Append("\nWHERE ");
        using (writer.Section(Where, SqlRangeKind.Where))
        {
            writer.Append(string.Join(" AND ", countWhereClauses));
        }
    }

    return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
}
```

**When `CountPhaseScoped` is `false` (the default, every existing caller), this is byte-for-byte identical to today's rendering** — `countSortJoins` is empty and `countWhereClauses` only ever gets `OuterPredicate` when present, exactly as before. Write two tests in a new file, `test/Ignixa.Search.Sql.Tests/Builders/SqlBuilderCountPhaseScopedTests.cs` (read `EndToEndCompilationTests.cs`'s existing `_summary=count`/`_total=accurate` combined-with-`_sort` test — from sub-project 1's Phase 9 completeness work — for the exact fixture shape to mirror):

```csharp
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Builders;

public class SqlBuilderCountPhaseScopedTests
{
    [Fact]
    public void GivenCountPhaseScopedTrueOnAValuedPhasePlan_WhenEmitted_ThenTheCountQueryJoinsTheSortKey()
    {
        // Arrange -- mirror an existing sorted end-to-end test's expression/symbols/SortSpec construction
        // (a single-key ascending sort, SortPhase.Valued), then lower with countOnly: true, countPhaseScoped: true.

        // Act
        var emitted = SqlBuilder.Run(/* the lowered plan */);

        // Assert -- the count query must join the sort key's table (proving it's phase-scoped, not the
        // whole match set), matching the same join shape EmitSortJoins renders for the non-count path.
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldContain("JOIN");
        emitted.Sql.ShouldNotContain("ORDER BY");
        emitted.Sql.ShouldNotContain("OFFSET");
    }

    [Fact]
    public void GivenCountPhaseScopedFalse_WhenEmittedAlongsideASort_ThenTheCountQueryIsUnaffectedByTheSort()
    {
        // Regression guard: proves this task did NOT change unscoped CountOnly's existing behavior --
        // _total=accurate & _sort=X (Phase 9's own tested composition) must still report the TRUE total
        // match count, ignoring sort entirely, exactly as before this task.

        // Arrange -- same sorted plan as above, but countPhaseScoped left at its default (false).

        // Act
        var emitted = SqlBuilder.Run(/* the lowered plan, countPhaseScoped: false (default) */);

        // Assert -- no sort-key join appears; this is the exact rendering CountOnly has always produced.
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldNotContain("JOIN");
    }
}
```

Run `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SqlBuilderCountPhaseScopedTests"` — expect both to fail first (compile error, `countPhaseScoped`/`CountPhaseScoped` don't exist yet), then pass once the guard/field/branch changes above are in place. Also re-run the existing Phase 9 `_total=accurate&_sort` combined-proof test — `GivenACountOnlyPlanWithSortAndTopAndIncludesAllSet_WhenEmitted_ThenTheyAreAllIgnored` in `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs` (`dotnet test ... --filter "FullyQualifiedName~GivenACountOnlyPlanWithSortAndTopAndIncludesAllSet"`) — and confirm it still passes unmodified (it asserts the full emitted SQL string via `ShouldBe`, so any byte-drift in the default-`countPhaseScoped: false` rendering path fails it immediately). This is the real regression check, not the two new tests above.

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
git add src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Lowering/OffsetPagingGuardTests.cs test/Ignixa.Search.Sql.Tests/Builders/SqlBuilderCountPhaseScopedTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): add offset-based paging and phase-scoped CountOnly alongside keyset PageSpec"
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

Add `(long Start, long End)? surrogateIdRange = null` as a new trailing optional parameter on `Lower.Run` (after Task 4's `offsetPage`/`countPhaseScoped`, append-only — Task 4 also adds a trailing parameter to this same method; since every parameter here is optional and always passed by name at every call site in this codebase's own style, the exact relative order between this task's `surrogateIdRange` and Task 4's `countPhaseScoped` doesn't matter functionally, only that both land after everything Task 1 already established. `CompileFromOptionsAsync`'s own consolidated signature, written across Tasks 3/4/5, is the actual source of truth for the final parameter order — re-read it before assuming this task's snippet's ordering is final). Immediately after `outerPredicate` is computed (both in the `expression is null` branch, where it starts as the implicit `null`, and the `expression is not null` branch, where `ExtractResourceColumnPredicates` sets it), AND in the surrogate-id range predicate:

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
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerSortKeyTests.cs` (**corrected from an earlier draft's `LowerTests.cs` citation — the real `BuildSortKey` tests for `_lastUpdated`/String/Date live in `LowerSortKeyTests.cs`, with shared fixtures in `LowerTestFixtures.cs`; confirmed by direct inspection**), `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`.

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

Read `LowerSortKeyTests.cs`'s existing `BuildSortKey` tests (for `_lastUpdated`, String, Date) and `LowerTestFixtures.cs`'s shared fixture helpers first and match their exact construction style.

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

**No new logic is needed in `EmitMissingPrimaryFilter` — only its message.** `_id` is NEVER missing (every resource has a `ResourceId` by definition), exactly like `_lastUpdated` — but the existing guard already catches it for free: `SortKeyKind.ResourceId` keys have `SearchParamId == null` (per Step 5's `BuildSortKey` case above), and the guard's existing `key.SearchParamId is null` arm already fires for any `SearchParamId`-less key, `ResourceId` included. Adding `key.Kind == SortKeyKind.ResourceId` as a THIRD explicit condition would be redundant — it can never trigger on a code path the `SearchParamId is null` arm doesn't already cover. Only update the message text to name the new case, so a future reader hitting this guard for `_id` isn't confused by a message that only mentions `LastUpdated`:

```csharp
if (key.Kind == SortKeyKind.LastUpdated || key.SearchParamId is null)
{
    throw new InvalidOperationException(
        "SortSpec.Phase == MissingPrimary with a LastUpdated, ResourceId, or otherwise SearchParamId-less " +
        "primary key reached Emit -- none of these are ever \"missing\" (all are non-nullable resource " +
        "columns), so none has a MissingPrimary segment. Lower.BuildSortSpec should reject this combination " +
        "the same way it already does for LastUpdated -- extend that guard to cover ResourceId too if it " +
        "doesn't yet.");
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
git add src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerSortKeyTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "fix(search-sql): add SortKeyKind.ResourceId so _sort=_id compiles correctly instead of silently matching nothing"
```

---

### Task 7: `SqlServerSymbolResolver`

**Design doc:** §5.

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs` — add read-only `TryGetSystemIdAsync`/`TryGetQuantityCodeIdAsync`, using the existing `MissingSentinel` negative-caching convention; make `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync`'s own cache checks sentinel-aware; make the nested `OnDemandResolvingDictionary<TKey, TValue>`'s `TryGetValue` fast path sentinel-aware too (this is the actual object the write path's row generators read through via `SystemMappings`/`QuantityCodeMappings` — fixing only the get-or-create methods and leaving this fast path alone would still let a stale negative-cache entry reach the write path as if it were a real ID).
- Modify: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs` — its existing direct construction of `OnDemandResolvingDictionary<string, int>` (a 3-argument call today) needs a 4th argument once this task adds the sentinel parameter, or it stops compiling.
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

- [ ] **Step 4a: Fix `OnDemandResolvingDictionary`'s own sentinel blindness — the actual object the write path reads through**

Step 4's fix above covers `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` being called directly, but the write path's row generators don't call those methods directly — they read through `SystemMappings`/`QuantityCodeMappings` (`SqlServerMergeRepository.cs:44-64,160-161` construct/consume these two properties), which wrap `_systemCache`/`_quantityCodeCache` in `OnDemandResolvingDictionary<TKey, TValue>` (`SqlServerSearchIndexReferenceDataCache.cs`, the private nested class near the bottom of the file). Its `TryGetValue` fast path today is:

```csharp
public bool TryGetValue(TKey key, out TValue value)
{
    if (cache.TryGetValue(key, out value!))
    {
        return true;
    }
    // ... resolveAsync fallback
}
```

This is sentinel-blind by construction — it has no idea `-1` means "confirmed missing," so it happily returns a stale sentinel as if it were a real ID, exactly the same class of bug Step 4 just fixed on the two get-or-create methods, just one layer further down. **This is the object `SystemMappings`/`QuantityCodeMappings` actually construct and hand to row generators — fixing only Step 4's methods and leaving this alone does not close the bug.** Fix: give `OnDemandResolvingDictionary` its own sentinel parameter and skip a cached sentinel exactly like every other cache check in this file already does.

```csharp
internal sealed class OnDemandResolvingDictionary<TKey, TValue>(
    ConcurrentDictionary<TKey, TValue> cache,
    Func<TKey, CancellationToken, Task<TValue>> resolveAsync,
    ILogger logger,
    TValue missingSentinel) : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    public TValue this[TKey key] => TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

    public IEnumerable<TKey> Keys => cache.Keys;

    public IEnumerable<TValue> Values => cache.Values;

    public int Count => cache.Count;

    public bool ContainsKey(TKey key) => cache.ContainsKey(key);

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (cache.TryGetValue(key, out value!) && !EqualityComparer<TValue>.Default.Equals(value, missingSentinel))
        {
            return true;
        }

        try
        {
            value = resolveAsync(key, CancellationToken.None).GetAwaiter().GetResult();
            cache[key] = value;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve {Key} on demand -- row skipped", key);
            value = default!;
            return false;
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => cache.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

Update the two constructing properties to pass the same `SystemQuantityMissingSentinel` this task already introduced in Step 4:

```csharp
public IReadOnlyDictionary<string, int> SystemMappings =>
    new OnDemandResolvingDictionary<string, int>(_systemCache, GetOrCreateSystemIdAsync, _logger, SystemQuantityMissingSentinel);

public IReadOnlyDictionary<string, int> QuantityCodeMappings =>
    new OnDemandResolvingDictionary<string, int>(_quantityCodeCache, GetOrCreateQuantityCodeIdAsync, _logger, SystemQuantityMissingSentinel);
```

**This changes an existing constructor's arity** — `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs` already constructs `OnDemandResolvingDictionary<string, int>` directly (its `GivenAResolverThatThrows_WhenTryGetValueMisses_ThenAWarningIsLoggedAndFalseIsReturned` test) with 3 arguments — it needs a 4th now. That test's own scenario never touches the sentinel path (the backing dictionary starts empty, the resolver always throws), so any `int` value works; pass `-1` for consistency with the real sentinel value used elsewhere, even though this particular test doesn't exercise sentinel behavior:

```csharp
var wrapper = new SqlServerSearchIndexReferenceDataCache.OnDemandResolvingDictionary<string, int>(
    backingCache,
    (_, _) => Task.FromException<int>(new InvalidOperationException("simulated resolve failure")),
    logger,
    -1);
```

- [ ] **Step 4b: Write a test proving the sentinel never leaks through `SystemMappings`/`QuantityCodeMappings`**

The interaction test in Step 5 below proves `GetOrCreateSystemIdAsync` itself is safe, but per this step's own point, that is not the object the write path actually calls — prove the real object is safe too:

```csharp
[Fact]
public async Task GivenASystemMissedByReadOnlyLookup_WhenTheWritePathLaterCreatesItThroughSystemMappings_ThenTheRealIdIsReturnedNotTheStaleSentinel()
{
    // Arrange
    var cache = /* same construction as the other tests in this file, ONE shared instance */;
    const string system = "http://later-created-via-systemmappings.example.org/for-this-test";
    var missedId = await cache.TryGetSystemIdAsync(system, CancellationToken.None);
    missedId.ShouldBeNull();

    // Act -- this is the actual call shape SqlServerMergeRepository's row generators use, not
    // GetOrCreateSystemIdAsync directly
    var found = cache.SystemMappings.TryGetValue(system, out var resolvedId);

    // Assert
    found.ShouldBeTrue();
    resolvedId.ShouldBeGreaterThan(0);

    var readBackId = await cache.TryGetSystemIdAsync(system, CancellationToken.None);
    readBackId.ShouldBe(resolvedId);
}
```

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
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs src/DataLayer/Ignixa.DataLayer.SqlServer/Search/SqlServerSymbolResolver.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerSymbolResolverTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerSearchIndexReferenceDataCacheReadOnlyLookupTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs
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

- [ ] **Step 2: Extend `SearchCompiler.CompileFromOptionsAsync` with offset/surrogate-range/count/include-limit parameters**

Add four more trailing optional parameters to the method Task 3 created:

```csharp
public static async Task<SearchTrace> CompileFromOptionsAsync(
    SearchOptions options,
    string? resourceType,
    ISymbolResolver resolver,
    ICompartmentDefinitionManager? compartmentDefinitionManager,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager,
    TimeProvider? timeProvider,
    OffsetSpec? offsetPage = null,
    (long Start, long End)? surrogateIdRange = null,
    bool countOnly = false,
    int includeLimit = 0,
    CancellationToken cancellationToken = default)
```

Thread all four straight through to the internal `Lower.Run(...)` call's `offsetPage:`/`surrogateIdRange:`/`countOnly:`/`includeLimit:` arguments — Task 3's original sketch hardcoded `includeLimit: 0` and had no `countOnly:`/`offsetPage:`/`surrogateIdRange:` arguments at all (those parameters didn't exist on `Lower.Run` yet when Task 3 was written); replace the hardcoded `includeLimit: 0` literal with the new `includeLimit` parameter, and add the other three as named arguments to the same `Lower.Run(...)` call.

**Why all four land here instead of staying scattered:** they're all the same kind of gap — Task 3 built this entry point before Tasks 4/5 (which added `offsetPage`/`surrogateIdRange` to `Lower.Run`) existed, and before this task's own adapter logic surfaced the `countOnly`/`includeLimit` needs. Consolidating them into one Step 2 edit (rather than patching `CompileFromOptionsAsync` bit by bit across several steps) keeps the method's real final shape visible in one place.

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
using Ignixa.Search.Sql.Ast;
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

        // trace.CompiledPlan, not trace.Plan (the latter is QueryPlanTrace, a display-only projection with
        // no Includes/Sort structure of its own -- see Task 3's SearchTrace.CompiledPlan addition).
        await foreach (var result in ExecuteAndMaterializeAsync(sql, trace.CompiledPlan!, cancellationToken))
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
        // resourceType may legitimately be null/empty (a multi-type/system-level search) -- both
        // CompileFromOptionsAsync (Task 3, widened to accept string? resourceType) and the underlying
        // Lower.Run already support this via systemLevelSearch. An earlier draft of this method rejected
        // a null/empty ResourceType with NotSupportedException, which was both wrong (the compiler already
        // supports this case) and made it impossible to exercise multi-type search through this adapter at
        // all -- removed.
        var resourceType = options.ResourceType;

        OffsetSpec? offsetPage = null;
        if (!countOnly)
        {
            // Must match SqlEntityFrameworkSearchService.BuildQueryAsync's exact pagination convention:
            // options.MaxItemCount arrives from the caller ALREADY "+1'd" for hasMore detection when there is
            // no continuation token (the handler layer adds that +1 before building SearchOptions at all) --
            // so the no-token branch uses it as-is. A decoded continuation token, by contrast, stores the
            // caller's ORIGINAL (non-+1'd) count ("Token stores original user-requested count (without +1),
            // but handler adds +1 for hasMore detection - so we add it back here"), so THIS branch must add
            // the +1 back explicitly, or every page after the first would come back one row short and the
            // Application layer's hasMore detection would misfire.
            if (!string.IsNullOrWhiteSpace(options.ContinuationToken)
                && Ignixa.Search.Models.ContinuationToken.TryDecode(options.ContinuationToken, out var tokenOffset, out var tokenCount))
            {
                offsetPage = new OffsetSpec(tokenOffset, tokenCount + 1);
            }
            else
            {
                offsetPage = new OffsetSpec(0, options.MaxItemCount);
            }
        }
        // else: countOnly (CountAsync) never pages -- SqlEntityFrameworkSearchService.CountAsync ignores
        // ContinuationToken/MaxItemCount entirely (confirmed by direct inspection: it ends every code path
        // in a bare .CountAsync() call with no Skip/Take), so this adapter matches that by leaving
        // offsetPage null whenever countOnly is true, never by constructing one and hoping Lower.Run
        // tolerates the combination.

        (long Start, long End)? surrogateIdRange = options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue
            ? (options.StartSurrogateId.Value, options.EndSurrogateId.Value)
            : null;

        // Legacy has no hard cap on include results at all -- BuildIncludeQuery has no .Take/TOP of its own,
        // so there is no legacy default to literally mirror. Fall back to the primary page size when the
        // caller didn't specify one explicitly, rather than inventing an unrelated magic number.
        var includeLimit = options.IncludesMaxItemCount ?? options.MaxItemCount;

        return await SearchCompiler.CompileFromOptionsAsync(
            options,
            resourceType,
            _symbolResolver,
            _compartmentDefinitionManager,
            _searchParameterDefinitionManager,
            timeProvider: null,
            offsetPage,
            surrogateIdRange,
            countOnly,
            includeLimit,
            cancellationToken);
    }

    // ... ExecuteAndMaterializeAsync, BindParameters, row-shape branching, decompress -- see Step 6 below.
}
```

**Every value threaded into this method must be re-verified against Task 3's/Step 2's actual landed `CompileFromOptionsAsync` signature before pasting this in** — in particular, confirm the parameter order matches exactly (positional arguments above rely on it).

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

    var rows = await _sqlExecutionService.ExecuteReaderAsync(
        _tenantId,
        command,
        reader => ReadMatchRow(reader, hasIncludes),
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

private static MatchRow ReadMatchRow(SqlDataReader reader, bool hasIncludes)
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
- Consumes: Task 8's `SqlServerCompiledSearchService`, `SortPhase.Valued`/`SortPhase.MissingPrimary`, Task 3's `CompileFromOptionsAsync`'s implicit use of `SortPhase.Valued` (hard-coded in Task 3's sketch — this task must make it a real, driven loop, not a hard-coded constant), Task 4's `countPhaseScoped` mechanism (used to disambiguate the `MissingPrimary` phase's offset when `Valued` returns zero rows).
- Produces: `SearchStreamAsync` correctly returns a full page even when the requested offset straddles the Valued/MissingPrimary boundary.

- [ ] **Step 1: Re-read Task 8's current `CompileAsync`/`ExecuteAndMaterializeAsync` and the design doc's exact corrected formula**

**The design doc's prose (§3's "Fix:" paragraph) reads, taken literally, as if `MissingPrimary` never runs when `Valued` returns at least one row ("no further work needed") — but the SAME paragraph also says "Either way, `MissingPrimary`'s fetch limit is `Limit - (rows already returned by Valued)`", which only makes sense if `MissingPrimary` DOES still run in that case, just with a reduced limit. This plan resolves the apparent contradiction the way the "either way" clause requires — do not transcribe the "no further work needed" sentence as "skip `MissingPrimary` whenever `Valued` returned any rows"; that reading was tried in an earlier draft of this task and is wrong (see the straddling-page test in Step 2, which it fails).** The correct algorithm:

1. Run the `Valued` phase at the requested offset, with the requested limit.
2. If `Valued` alone returned **the full requested count** (the page is already full), stop — genuinely no further work needed, `MissingPrimary` never runs.
3. If `Valued` returned **at least one row but fewer than the requested count** (a short, non-empty page), the phase boundary is unambiguously *inside* this page — run `MissingPrimary` at offset `0`, limit `requestedCount - valuedCount`, to fill out the rest of the page.
4. If `Valued` returned **zero rows**, the offset landed at-or-past the `Valued` total and the boundary's exact location is ambiguous without asking — run a `CountOnly` (`countPhaseScoped: true`, Task 4) compile of the `Valued` phase to learn the exact `Valued` total, then run `MissingPrimary` at offset `max(0, requestedOffset - valuedTotal)`, limit `requestedCount` (all of it, since `Valued` contributed nothing).

This loop only applies when `options.Sort` is non-empty (this loop has no meaning for keyset paging, which the compiler already handles correctly in one compile via its own boundary mechanism — the adapter never uses keyset paging at all, per the design doc's decision to bridge exclusively via offset-mode paging, so this distinction doesn't need a runtime check). **Corrected from an earlier draft: this loop must run on EVERY sorted search, including the first page with no continuation token at all — offset `0` is not a special case, it is just `OffsetSpec(0, Limit)`.** A token-less sorted search that skipped this loop would compile Valued-only and silently omit every missing-value resource from page 1.

- [ ] **Step 2: Write the failing test for a page straddling the phase boundary**

**Both tests below encode `count: 5` via `ContinuationToken.Encode`, but the loop's own "+1 for hasMore" convention (Step 4b/4c — the same convention `CompileAsync` already applies for a token-driven request) means the ACTUAL requested count the algorithm works with is `6`, not `5`. Both tests' assertions and inline comments are written against that real `6`, not the `5` in the token — do not "fix" the algorithm to make these come out to `5`; `5` would silently break the hasMore convention Task 8 already established for the non-sorted path, and Task 13's differential paging test would eventually catch that regression two tasks later, the hard way.**

```csharp
[Fact]
public async Task GivenAPageStraddlingTheValuedMissingPrimaryBoundary_WhenSearchStreamAsyncCalled_ThenReturnsExactlyThePageWithNoDuplicatesOrGaps()
{
    // Arrange -- create 10 Patients with a sortable String parameter set (Valued), then 5 more Patients
    // WITHOUT that parameter set (MissingPrimary). Sort ascending by that parameter, page size 5,
    // request offset=8. The token encodes count=5, but the +1-for-hasMore convention makes the real
    // requestedCount 6: Valued has only 2 rows left from offset 8 (rows 8-9 of its 10), so Valued
    // returns 2 and MissingPrimary fills the remaining 6-2=4 at its own offset 0 (rows 0-3 of its 5) --
    // 2 + 4 = 6 rows total, straddling the phase boundary with no duplicate and no gap.
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

    // Assert -- exactly 6 rows (2 from the tail of Valued, 4 from the head of MissingPrimary, per the
    // +1-for-hasMore arithmetic above), no duplicates against an adjacent page, no gap.
    results.Count.ShouldBe(6);
    results.Select(r => r.ResourceId).Distinct().Count().ShouldBe(6);
}

[Fact]
public async Task GivenAPageEntirelyWithinMissingPrimary_WhenSearchStreamAsyncCalled_ThenComputesTheCorrectMissingPrimaryOffset()
{
    // Arrange -- same 10 Valued + 5 MissingPrimary setup. Request offset=12, encoded count=5 (real
    // requestedCount, after +1, is 6) -- entirely past the Valued phase's 10 rows (Valued returns 0),
    // so a countPhaseScoped CountOnly compile reports the Valued total (10), giving MissingPrimary
    // offset max(0, 12-10)=2, limit 6-0=6. MissingPrimary only has 5 total rows, 3 of which remain
    // from its own offset 2 (rows 2, 3, 4) -- so min(6, 3) = 3 rows returned. The +1 convention doesn't
    // change this test's answer (data runs out at 3 either way, whether the limit is 5 or 6), but the
    // comment states the real 6 so a future reader isn't misled the way an earlier draft of this test
    // was.
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

    // Assert -- exactly 3 rows (rows 12, 13, 14 of the combined 15).
    results.Count.ShouldBe(3);
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~StraddlingTheValued|EntirelyWithinMissingPrimary"
```

Expected: FAIL — today's implementation hard-codes `SortPhase.Valued` and never runs a `MissingPrimary` phase at all, so the straddling test returns only 2 rows (Valued's tail) instead of 6, and the entirely-past test returns 0 rows instead of 3.

- [ ] **Step 4: Extend `CompileFromOptionsAsync` and `CompileAsync` with `sortPhase`/`countPhaseScoped`, add an explicit-offset override, then implement the two-phase loop**

**4a. Extend `SearchCompiler.CompileFromOptionsAsync` (Task 3) with two more trailing parameters:**

```csharp
public static async Task<SearchTrace> CompileFromOptionsAsync(
    SearchOptions options,
    string? resourceType,
    ISymbolResolver resolver,
    ICompartmentDefinitionManager? compartmentDefinitionManager,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager,
    TimeProvider? timeProvider,
    OffsetSpec? offsetPage = null,
    (long Start, long End)? surrogateIdRange = null,
    bool countOnly = false,
    int includeLimit = 0,
    bool countPhaseScoped = false,
    SortPhase sortPhase = SortPhase.Valued,
    CancellationToken cancellationToken = default)
```

Thread `countPhaseScoped`/`sortPhase` to the internal `Lower.Run(...)` call's own `countPhaseScoped:`/`sortPhase:` arguments — Task 3's original sketch hard-coded `SortPhase.Valued` directly in that call; replace the hard-coded literal with the new `sortPhase` parameter.

**4b. Extend Task 8's private `CompileAsync` helper with the same two parameters, plus an explicit-offset override that bypasses token decoding entirely** (needed because this loop must drive `Valued`/`MissingPrimary` with phase-specific offsets/limits that have no correct representation as a real `Ignixa.Search.Models.ContinuationToken` — round-tripping through `Encode`/`Decode` for an internal, adapter-only value is indirection with no purpose):

```csharp
private async Task<SearchTrace> CompileAsync(
    SearchOptions options,
    CancellationToken cancellationToken,
    bool countOnly = false,
    bool countPhaseScoped = false,
    SortPhase sortPhase = SortPhase.Valued,
    OffsetSpec? offsetPageOverride = null)
{
    var resourceType = options.ResourceType;

    OffsetSpec? offsetPage = offsetPageOverride;
    if (offsetPage is null && !countOnly)
    {
        // Same +1-for-hasMore convention as before -- see the comment in the original version of this
        // method (Task 8, Step 5) for the full rationale; unchanged by this task except for gaining the
        // offsetPageOverride bypass above it.
        if (!string.IsNullOrWhiteSpace(options.ContinuationToken)
            && Ignixa.Search.Models.ContinuationToken.TryDecode(options.ContinuationToken, out var tokenOffset, out var tokenCount))
        {
            offsetPage = new OffsetSpec(tokenOffset, tokenCount + 1);
        }
        else
        {
            offsetPage = new OffsetSpec(0, options.MaxItemCount);
        }
    }

    (long Start, long End)? surrogateIdRange = options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue
        ? (options.StartSurrogateId.Value, options.EndSurrogateId.Value)
        : null;

    var includeLimit = options.IncludesMaxItemCount ?? options.MaxItemCount;

    return await SearchCompiler.CompileFromOptionsAsync(
        options,
        resourceType,
        _symbolResolver,
        _compartmentDefinitionManager,
        _searchParameterDefinitionManager,
        timeProvider: null,
        offsetPage,
        surrogateIdRange,
        countOnly,
        includeLimit,
        countPhaseScoped,
        sortPhase,
        cancellationToken);
}
```

**Go back and update Task 8's own `CompileAsync` (written in that task's Step 5) to this exact shape** — this is the same method, extended, not a duplicate.

**4c. Implement the two-phase loop**, replacing `SearchStreamAsync`'s single Valued-only compile-and-execute call (`CountAsync` is untouched — it never pages, so it has no phase-boundary concern at all):

```csharp
private async IAsyncEnumerable<SearchEntryResult> SearchStreamWithPhaseHandlingAsync(
    SearchOptions options,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    if (options.Sort.Count == 0)
    {
        var trace = await CompileAsync(options, cancellationToken);
        if (trace.Sql is not { } sql)
        {
            throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
        }

        await foreach (var result in ExecuteAndMaterializeAsync(sql, trace.CompiledPlan!, cancellationToken))
        {
            yield return result;
        }

        yield break;
    }

    // Sort is active -- the two-phase loop applies to EVERY sorted search, including a token-less first
    // page (offset 0 is just OffsetSpec(0, Limit), not a special case that can skip this loop).
    int requestedOffset;
    int requestedCount;
    if (!string.IsNullOrWhiteSpace(options.ContinuationToken)
        && Ignixa.Search.Models.ContinuationToken.TryDecode(options.ContinuationToken, out var tokenOffset, out var tokenCount))
    {
        requestedOffset = tokenOffset;
        requestedCount = tokenCount + 1; // same +1-for-hasMore convention CompileAsync itself uses
    }
    else
    {
        requestedOffset = 0;
        requestedCount = options.MaxItemCount;
    }

    var valuedTrace = await CompileAsync(
        options, cancellationToken, sortPhase: SortPhase.Valued,
        offsetPageOverride: new OffsetSpec(requestedOffset, requestedCount));
    if (valuedTrace.Sql is not { } valuedSql)
    {
        throw new RequestNotValidException(valuedTrace.Failure?.Message ?? "The search could not be compiled.");
    }

    // Only count Match-mode rows toward the phase-boundary arithmetic below. An includes-bearing plan's
    // match-page CTE yields Match rows AND separately-unioned Include rows through the same reader --
    // the OFFSET/FETCH paging and every offset/limit computed in this method govern the MATCH set only.
    // Counting Include rows here would prematurely satisfy/shrink the page math on any sorted search
    // combined with _include/_revinclude, silently dropping MissingPrimary match rows that should have
    // been returned.
    var valuedCount = 0;
    await foreach (var result in ExecuteAndMaterializeAsync(valuedSql, valuedTrace.CompiledPlan!, cancellationToken))
    {
        if (result.SearchMode == SearchEntryMode.Match)
        {
            valuedCount++;
        }

        yield return result;
    }

    if (valuedCount >= requestedCount)
    {
        yield break; // Valued alone filled the whole page -- no room left for MissingPrimary rows.
    }

    int missingPrimaryOffset;
    if (valuedCount > 0)
    {
        // A short, non-empty Valued page: the phase boundary is unambiguously inside this page.
        missingPrimaryOffset = 0;
    }
    else
    {
        // Valued returned ZERO rows: the offset landed at-or-past the Valued total, and the boundary's
        // exact location is ambiguous without asking -- learn it via a countPhaseScoped CountOnly compile
        // (Task 4/§3's mechanism, purpose-built for exactly this disambiguation).
        var valuedCountTrace = await CompileAsync(
            options, cancellationToken, countOnly: true, countPhaseScoped: true, sortPhase: SortPhase.Valued);
        if (valuedCountTrace.Sql is not { } valuedCountSql)
        {
            throw new RequestNotValidException(valuedCountTrace.Failure?.Message ?? "The search could not be compiled.");
        }

        using var countCommand = new SqlCommand(valuedCountSql.Sql);
        BindParameters(countCommand, valuedCountSql.Parameters);
        var countRows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId, countCommand, reader => reader.GetInt64(0), cancellationToken);
        var valuedTotal = checked((int)(countRows.Count > 0 ? countRows[0] : 0L));

        missingPrimaryOffset = Math.Max(0, requestedOffset - valuedTotal);
    }

    var missingPrimaryLimit = requestedCount - valuedCount;
    var missingTrace = await CompileAsync(
        options, cancellationToken, sortPhase: SortPhase.MissingPrimary,
        offsetPageOverride: new OffsetSpec(missingPrimaryOffset, missingPrimaryLimit));
    if (missingTrace.Sql is not { } missingSql)
    {
        throw new RequestNotValidException(missingTrace.Failure?.Message ?? "The search could not be compiled.");
    }

    await foreach (var result in ExecuteAndMaterializeAsync(missingSql, missingTrace.CompiledPlan!, cancellationToken))
    {
        yield return result;
    }
}
```

`SearchStreamAsync` (Task 8) should now call `SearchStreamWithPhaseHandlingAsync` instead of its own inline compile-and-execute logic. Note this method never constructs a `SearchOptions` copy with a different `ContinuationToken` — every phase-specific offset/limit goes through `offsetPageOverride` instead, avoiding the `SearchOptions with { ... }` pattern entirely (`SearchOptions` is a mutable class, not a record — `with` expressions do not compile against it; an earlier draft of this task used them and would not have built).

**Re-verify the `results.Count.ShouldBe(6)`/`ShouldBe(3)` assertions in Step 2's two tests by hand-tracing the algorithm above against each test's exact Arrange data, INCLUDING the `tokenCount + 1` convention** — both were hand-traced against this corrected algorithm and both check out: straddling — encoded `count: 5` becomes `requestedCount = 6`; Valued returns 2 of the requested 6 at offset 8 (only 2 rows remain in Valued past offset 8); `MissingPrimary` fills the remaining `6 - 2 = 4` at its own offset 0 = `2 + 4 = 6` total. Entirely-past — encoded `count: 5` becomes `requestedCount = 6`; Valued returns 0 at offset 12; `CountOnly` reports the Valued total as 10; `MissingPrimary` runs at offset `max(0, 12 - 10) = 2` with limit `6 - 0 = 6` against `MissingPrimary`'s 5 total rows, of which only 3 remain from offset 2 = `min(6, 3) = 3` total. **If a real implementation's numbers don't match these, that is an algorithm bug to investigate — not a signal to adjust the test's Arrange/Assert to whatever the implementation happens to produce.** Re-verify this hand-trace once more against whatever Step 2's test data actually ends up being if it changes.

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
    var options = new SearchOptions
    {
        ResourceType = "Patient",
        Sort = [/* the shared sort parameter, ascending */],
    };

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
