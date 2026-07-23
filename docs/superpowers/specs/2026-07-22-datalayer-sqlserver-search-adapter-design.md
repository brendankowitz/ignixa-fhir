# Sub-project 3: SqlServer-native search adapter — Design

**Branch:** `worktree-ignixa-datalayer-sqlserver` (worktree `.claude/worktrees/ignixa-datalayer-sqlserver`). No new branch.

## Context

This is the third and final sub-project of the original, too-large "Phase E" (read-path/search migration) design (`docs/superpowers/specs/2026-07-22-datalayer-sqlserver-phase-e-search-adapter-design.md`, superseded — that document's real requirements are still valid, only its packaging as a single monolithic phase was wrong). It was split into three ordered sub-projects:

1. **Compiler feature-parity** (`Ignixa.Search.Sql`) — complete, branch tip `1d0962e1`.
2. **`Ignixa.DataLayer.SqlServer` prerequisites** — complete, branch tip `0107b720`, pushed to `origin/worktree-ignixa-datalayer-sqlserver`.
3. **This sub-project** — the actual adapter, now unblocked.

Phases A-D (an earlier, separate DataLayer.SqlServer initiative) cut the FHIR **write** path over to `Ignixa.DataLayer.SqlServer` unconditionally, no feature flag. **Search/read never moved** — it is still served entirely by `SqlEntityFrameworkSearchService`, backed by its own `SearchIndexReferenceDataCache` instance, separate from the SqlServer write-side cache. This sub-project retargets that gap: the compiler becomes a new `ISearchService` implementation living in `Ignixa.DataLayer.SqlServer`, hard-cut-over the same way the write path was — no flag. `Ignixa.DataLayer.SqlEntityFramework` is left untouched as a reference implementation and rollback lever.

**Compiler-quality stance, unchanged from the original design:** if the differential harness surfaces a real bug in `Ignixa.Search.Sql`, it gets fixed in the compiler — never routed around in the adapter.

## Re-verification against current code (this design pass)

The original Phase E doc listed 4 prerequisites. Re-checked against the current branch tip before finalizing this design, since sub-projects 1 and 2 both touched adjacent code since that doc was written:

1. **`ct` → `cancellationToken` in `SqlServerFhirRepository.cs`** — done, sub-project 2 Task 1.
2. **`IncludeStage.Direction` dual-source-of-truth** (`Lower.BuildIncludeStages` deriving direction from which parameter list an include arrived through, vs. `Requires`/`Produces` deriving from `expression.Reversed`) — **already fixed**, independently of both sub-projects 1 and 2. Confirmed via `git log`: commit `fe019141` ("derive IncludeStage.Direction from IncludeExpression.Reversed, not caller's parameter list") predates this sub-project's work. Confirmed in current code: `Lower.ResolveInclude` derives `Direction` purely from `expression.Reversed` for every include regardless of which list it came from — single source of truth, no work needed here.
3. **`SearchCompartmentHandler` nested-`And`** — fixed, sub-project 2 Task 2.
4. **Composition-root move** — done, sub-project 2 Task 3 (`SqlServerRepositoryFactory`).

So of the original 4 prerequisites, 3 are done and 1 was already a non-issue. This sub-project needed to re-derive its own prerequisite list rather than trust the original doc's — see below.

## New prerequisite work (found during this design pass, not in the original doc)

### 1. `ISearchService`'s `ct` → `cancellationToken`

`src/Application/Ignixa.Domain/Abstractions/ISearchService.cs` still declares `CancellationToken ct = default` on all three members — the same CLAUDE.md "CRITICAL VIOLATION" sub-project 2 already fixed on `IFhirRepository`, just on a sibling interface nobody has touched yet. `SqlServerCompiledSearchService` is a new implementation of this interface; CA1725 (parameter names must match the base interface declaration, enforced as a build error via `TreatWarningsAsErrors` + `AnalysisLevel=latest-All`) means writing it correctly from day one requires the interface to already say `cancellationToken`.

**Decision: rename now**, cascading through `ISearchService` + its 2 existing implementations (`SqlEntityFrameworkSearchService`, `FileBasedSearchService`) + call sites, as a small prequel task — same pattern as sub-project 2 Task 1.

### 2. `SearchCompiler` needs a pre-built-`SearchOptions` entry point

The original design doc's architecture section claimed the adapter "drives the compiler via `SearchCompiler.CompileAsync`... not a re-implementation of Build→Resolve→Lower→Emit." Checked against the real signature and the real call shape `ISearchService` requires, and this claim doesn't hold as written:

- `SearchCompiler.CompileAsync(string resourceType, IReadOnlyList<QueryParameter> parameters, ISearchOptionsBuilder optionsBuilder, ISymbolResolver resolver, ...)` does **Build** (`optionsBuilder.Build(...)`) internally, then Resolve → Lower → Emit.
- `ISearchService.SearchStreamAsync<TSearchOptions>(TSearchOptions searchOptions, ...)` receives an **already-built** `Ignixa.Search.Models.SearchOptions` — confirmed by reading `SqlEntityFrameworkSearchService`, which just casts `TSearchOptions` straight to it. Build already happened upstream, by whatever produced that `SearchOptions` (the request pipeline's `SearchOptionsBuilder`, before `ISearchService` is ever called).

Calling `CompileAsync` as-is would mean re-deriving raw `QueryParameter`s from an already-built `SearchOptions` just to redo Build — wasteful and lossy (not everything Build derives is guaranteed reconstructible from its own output). The correct fix, confirmed with the user: add a variant to `SearchCompiler` that accepts an already-built `SearchOptions` directly and skips Build, doing Resolve → Lower → Emit only. This keeps `SearchCompiler`'s own stated purpose true ("production wiring runs the same sequence [as the tracing suite]") and is a small, additive change to `Ignixa.Search.Sql` — same file (`Tracing/SearchCompiler.cs`), same project, not a new architectural component.

**Interaction with PR #353** (`brendankowitz-implement-sql-search-gaps`, open, `MERGEABLE`, expected to merge before or during this sub-project's execution): that PR adds a `CompileWithTimeProviderAsync` overload for `:ap`'s deterministic time capture, but it still takes raw `QueryParameter`s + a builder — it does not add the pre-built-`SearchOptions` entry point this sub-project needs. The new overload this sub-project adds must also thread a `TimeProvider`/`approximationReferenceTime` through, mirroring what `CompileWithTimeProviderAsync` already does, so `:ap` continues to work correctly through the new entry point. Exact signature and naming are a plan-time detail; the shape (`SearchOptions` in, `SearchTrace` out, Resolve-onward only, `TimeProvider` threaded through) is the design-level commitment.

### 3. Residual compiler gaps — explicit disposition, not blockers

The compiler-parity gate ("no fallback dispatch, closing every gap is a prerequisite for cutover") was written against a 5-item list (`docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md`). Checked PR #353's actual diff against that list directly:

| Gap | Status |
|---|---|
| `:ap` comparator | Closed by PR #353 |
| Quantity System/Code matching | Closed by PR #353 |
| `_lastUpdated` partial-precision ranges | **Still open** — PR #353 adds `:ap`'s range handling only; the `if (value.Start != value.End) throw` guard in `ResourceColumnLoweringRule.cs` remains for every other comparator (`eq`/`ne`/`gt`/etc.) |
| Reference `:identifier`/type modifiers | **Still open, and not compiler-only** — `ReferenceSearchParam` has no identifier-search columns at all (schema gap, confirmed via `97.sql:518-526`) and `ReferenceSearchParameterRowGenerator` has zero identifier logic (write-path gap) |
| True multi-level `:iterate` | Not actually a "gap" — a deliberate, permanent scope boundary matching `microsoft/fhir-server`'s own open limitation (issue #1310) |

**Decision, confirmed with the user:** proceed with this sub-project's design and build now rather than waiting on the two still-open items. Both become entries in the differential harness's "known, expected divergences" list (below), and the adapter throws `NotSupportedException` for those query shapes post-cutover, same as multi-level `:iterate` already does. Whether the *hard cutover* itself waits on them closing is a separate decision, made when the adapter and harness are otherwise ready — not decided by this document.

## Architecture

### New component: `SqlServerCompiledSearchService : ISearchService`

Lives in `Ignixa.DataLayer.SqlServer/Search/` (new folder, mirrors the EF sibling's `Search/` folder).

**`SearchStreamAsync<TSearchOptions>` / `CountAsync<TSearchOptions>`** — cast `TSearchOptions` to `Ignixa.Search.Models.SearchOptions` (same pattern `SqlEntityFrameworkSearchService` already uses), then drive the compiler via the new pre-built-`SearchOptions` `SearchCompiler` entry point (item 2 above). The adapter checks the returned `SearchTrace.Failure` first: non-null translates to `Ignixa.Domain.Exceptions.RequestNotValidException` (existing `FhirException` subtype, matching how other invalid-search-input cases already surface through this codebase) before touching the database — no query executes on a failed compile.

On success, `SearchTrace.SqlTrace!.Sql` is parameterized T-SQL (literal catalog IDs, parameterized user values, per `Emit`'s established design). Execution:

1. Run the compiled SQL via `ISqlExecutionService.ExecuteReaderAsync`, tenant-scoped like every other SqlServer-native query. Result is the match page: `(ResourceTypeId, SurrogateId, IsMatch, IsPartial, SortValue0..N)` rows. `CountOnly` plans (used by `CountAsync`) return a single scalar and skip steps 2-3.
2. Batch-fetch the corresponding `dbo.Resource` rows by `SurrogateId`, chunked via `.Chunk(100)` — matching `SqlServerPostMergeExtensionUpdater`'s existing pattern — fetching `RawResource`, `Version`, `LastUpdated`, `IsDeleted`.
3. Decompress via the existing `GzipResourceCompressor` (already shared with the write path — no new compression code) and materialize `SearchEntryResult` per row, preserving match-page order, mapping `IsPartial` → `SearchEntryMode.Include` / else `Match`.

**`GetExportRangesAsync`** does not touch the compiler — a direct `MIN`/`MAX`-based range query over `dbo.Resource.ResourceSurrogateId` via `ISqlExecutionService`, mirroring the EF sibling's single-aggregation-query shape (min/max/count together, not three separate subqueries) but expressed as raw SQL instead of EF LINQ.

### Differential harness

New test class (exact file organization — new test project vs. new class in an existing one — decided at plan time) comparing `SqlServerCompiledSearchService` against `SqlEntityFrameworkSearchService` for the same queries, across a representative set spanning every leaf/composite search-parameter type, chain, include/revinclude (+`:iterate`), compartment, sort, `:missing`, count — mirroring Phase D's `DifferentialTestHarness` pattern (`SnapshotLegacyAsync`/`SnapshotNewAsync`, comparing search *results* rather than written rows).

**Known, expected divergences** — the harness must assert these diverge in the documented direction, not flag them as failures:
- `CompartmentSearchQueryGenerator` never filters `ReferenceResourceTypeId` (only `ReferenceResourceId`) — the compiler's `CompartmentSource` correctly closes this; compiler is right, legacy is wrong.
- `_include`'s `SearchParamId` filter — the live `IncludeProcessor` never filters by it at all; the compiler filters correctly.
- Composite `:missing` — `SearchExpressionQueryBuilder.ApplyMissingSearchParameterExpressionAsync` has no `Composite` arm, logs a warning, returns empty; the compiler returns real results.
- `:iterate` beyond one hop — the compiler supports one Kahn-sorted hop per expression by design; the live `IterateProcessor`'s runtime fixpoint goes further. Not exercised by the harness's query set.
- `_lastUpdated` partial-precision ranges — the compiler throws `NotSupportedException` for every comparator except `:ap`; legacy silently flattens to a single instant. Not exercised by the harness's query set.
- Reference `:identifier`/type modifiers — the compiler has no support for these query shapes (no schema support to build on). Not exercised by the harness's query set.

**Any other divergence is a real bug** — fixed in `Ignixa.Search.Sql`, harness re-run, never special-cased in the adapter or the harness.

### Cutover

`SqlServerRepositoryFactory` (sub-project 2's composition root) constructs `SqlServerCompiledSearchService` unconditionally for SqlServer-storage tenants — same storage-type gate the write-path cutover already uses, no feature flag. `Ignixa.DataLayer.SqlEntityFramework`'s `createSearchService` closure and `SqlEntityFrameworkSearchService` are left in place, untouched, as the reference implementation for any non-SqlServer storage type and as a rollback lever.

**Sequencing gate, within this sub-project's own plan:** differential harness proven clean first, *then* hard cutover — no feature flag, no shadow/dual-run period. Matches Phase D's exact pattern. This is a task-ordering constraint inside this sub-project's plan, not an external decision point.

## Explicitly out of scope

Carried forward from the compiler's own roadmap and this design pass, not silently dropped:
- **SQL plan-shaping "cache-breaking" investigation** — whether `Emit`'s determinism needs an opt-in escape hatch for fhir-server's cache-breaking trick. Needs its own dedicated investigation pass; not blocking this sub-project.
- **SMART/compartment instance-level scope enforcement** (`OutputScopeFilter`) — the compiler's `OutputTypeIds` field reserves the seat but nothing enforces it yet.
- **True multi-level `:iterate` recursion** beyond one hop.
- **`_lastUpdated` partial-precision ranges** — compiler-only gap, deliberately not closed by this sub-project (see above).
- **Reference `:identifier`/type modifiers** — schema+write-path feature, not this sub-project's job (see above).

## Testing

- Prequels (items 1-2 above): each gets its own unit/integration test proving the rename is behavior-preserving and the new `SearchCompiler` entry point is correctly wired (including `:ap`'s `TimeProvider` threading), per this initiative's existing per-task pattern.
- `SqlServerCompiledSearchService`: unit tests per search-parameter-type/feature area (leaf types, composites, chain, include/revinclude, compartment, sort, `:missing`, count), matching the compiler's own existing test granularity.
- Differential harness: the primary acceptance gate for cutover.
- Full E2E suite re-run post-cutover, matching this session's established practice for anything touching the live search path.

## Verification strategy (decided)

Differential harness first, proven clean, **then hard cutover** — no feature flag, no shadow/dual-run period. Matches Phase D's exact pattern (`SnapshotLegacyAsync`/`SnapshotNewAsync` differential proof before the unconditional write-path swap) and the original (superseded) Phase E design's own decision, re-confirmed here.
