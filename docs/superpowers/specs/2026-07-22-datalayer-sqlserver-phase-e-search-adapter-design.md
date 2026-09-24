# Phase E: SqlServer-Native Search Adapter — Design

**Branch:** `worktree-ignixa-datalayer-sqlserver` (worktree `.claude/worktrees/ignixa-datalayer-sqlserver`), continuing directly from Phase D + today's 3 fix plans (tip `f3c62de3`, rebased onto `origin/main`). No new branch.

## Context

Phases A-D cut the FHIR **write** path over to `Ignixa.DataLayer.SqlServer` (`SqlServerFhirRepository`/`SqlServerMergeRepository`), unconditionally, no feature flag, for every SqlServer-storage tenant. **Search/read never moved** — it is still served entirely by `SqlEntityFrameworkSearchService` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/`), backed by a separate `SearchIndexReferenceDataCache` instance from the SqlServer write-side cache. This was a deliberate Phase D scope boundary (`SqlEntityFrameworkRepositoryFactory.cs:373-377`: "Reads... are untouched and remain on the EF-based search path") and is flagged in every final review since as "Phase E territory."

Separately, an independent initiative on `feature/fhir-to-sql-compiler` (which this branch forked from) built `Ignixa.Search.Sql` / `Ignixa.Search.Sql.Generators` — a FHIR-search-to-SQL compiler (Build → Resolve → Lower → Emit, `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`) purpose-built to become the eventual search implementation. Its own roadmap already names this next step "Phase 10" — "wire into `Ignixa.DataLayer.SqlEntityFramework` behind `UseCompiledSearch`; differential suite against the legacy path" — gated on an explicit go/no-go review of Phases 1-8 ("Checkpoint 1.5") that was never performed.

**This design retargets that work**: instead of wiring the compiler into the EF project behind a flag, it becomes a new `ISearchService` implementation living in `Ignixa.DataLayer.SqlServer`, hard-cut-over the same way the write path was — no flag. `Ignixa.DataLayer.SqlEntityFramework` is left untouched as a reference implementation. Checkpoint 1.5's formal review is explicitly skipped per user decision: the cumulative record of per-increment Fable adversarial reviews (Phases 1-9, each already `GO`/`GO WITH MINOR FOLLOW-UPS`) is treated as sufficient.

**Compiler-quality stance**: if the differential harness (below) surfaces a real bug in `Ignixa.Search.Sql`, it gets fixed in the compiler — never routed around in the adapter. This is a direct instruction, not a preference.

## Prerequisite work (before the adapter)

Three items, independently diagnosed, done first because each is either a correctness bug the adapter would otherwise inherit, or an architectural seam the adapter would otherwise be wedged into:

1. **`ct` → `cancellationToken` rename** in `SqlServerFhirRepository.cs`. The only file in `Ignixa.DataLayer.SqlServer` using the abbreviated name — a CLAUDE.md "CRITICAL VIOLATION." Mechanical.
2. **`IncludeStage.Direction` dual-source-of-truth** (`Ignixa.Search.Sql`, roadmap line 93/107): `Lower.BuildIncludeStages` derives `Direction` from *which parameter list* an `IncludeExpression` arrived through (`includes:` → Forward, `revIncludes:` → Reverse), while `Requires`/`Produces` derive from the expression's own `Reversed` flag — two independent sources with no consistency guard. Fix: derive `Direction` from `expression.Reversed` directly (single source of truth), or assert the two agree at Lower time and throw if not (matches this compiler's stated "fail at Lower time" principle). Today's invariant holds only because `SearchOptionsBuilder` always keeps the two in sync — `Lower.Run` is public API and must not depend on that external discipline.
3. **`SearchCompartmentHandler` nested-`And` gap** (roadmap line 101): `SearchCompartmentHandler` composes `And(compartment, SearchOptions.Expression)`, and `SearchOptionsBuilder` itself produces a *nested* `And` (not flattened) whenever 2+ ordinary search parameters are present. `Lower.cs`'s `ExtractResourceColumnPredicates` only inspects the top-level `And`'s direct children, so `GET /Patient/123/Observation?_id=X&category=lab` throws today even though `_id` alone or `category` alone both work. Fix: either flatten the handler's composed `And` before calling `Lower`, or make `ExtractResourceColumnPredicates` recurse into nested same-operator `And`s.
4. **Composition-root move**: today, `Ignixa.DataLayer.SqlEntityFramework`'s `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` directly `new`s every SqlServer type (`SqlServerSearchIndexReferenceDataCache`, `GzipResourceCompressor`, `SqlServerPostMergeExtensionUpdater`, `SqlServerMergeRepository`, `SqlServerFhirRepository`) — the older sibling project is the composition root for the newer one. A new `SqlServerRepositoryFactory` class in `Ignixa.DataLayer.SqlServer` takes over that construction (same tenant-scoped inputs, same objects built, pure relocation — not a redesign), exposing the equivalent of today's inline `createRepository` closure (`CreateServiceFactory.cs:380-404`) as a method. `CreateServiceFactory` calls into it instead of constructing directly. The upcoming `SqlServerCompiledSearchService` construction (below) is added to this same new factory, not wedged into the EF project's `createSearchService` closure. Behavior-preserving; gets the same differential/regression verification as any other refactor in this initiative.

## Architecture

### New component: `SqlServerCompiledSearchService : ISearchService`

Lives in `Ignixa.DataLayer.SqlServer/Search/` (new folder, mirrors the EF sibling's `Search/` folder — confirmed a natural fit by the structural review).

**`SearchStreamAsync` / `CountAsync`** — both drive the compiler via `SearchCompiler.CompileAsync` (`Ignixa.Search.Sql.Tracing`), not a re-implementation of the Build→Resolve→Lower→Emit sequence: the compiler's own doc comment states it "exists so that future production wiring runs the same sequence." `CompileAsync` returns a `SearchTrace` whose `Failure` field is non-null (never thrown) for an unresolved parameter or a `NotSupportedException`/`KeyNotFoundException` from Lower/Emit. The adapter checks `Failure` first and translates it to `Ignixa.Domain.Exceptions.RequestNotValidException` (an existing `FhirException` subtype, matching how other invalid-search-input cases already surface through this codebase) before touching the database — no query executes on a failed compile.

On success, `SearchTrace.SqlTrace!.Sql` is the T-SQL text (parameterized per `Emit`'s design — literal catalog IDs, parameterized user values, matching the plan-shaping rationale already established in the compiler's own docs). Execution:

1. Run the compiled SQL via `ISqlExecutionService.ExecuteReaderAsync`, tenant-scoped like every other SqlServer write-side query. The result is the match page: `(ResourceTypeId, SurrogateId, IsMatch, IsPartial, SortValue0..N)` rows (`CountOnly` plans return a single scalar instead — `CountAsync` uses this path and returns early, skipping steps 2-3 below).
2. Batch-fetch the corresponding `dbo.Resource` rows by `SurrogateId` (chunked, matching the `Chunk(100)` pattern `SqlServerPostMergeExtensionUpdater` already uses) — `RawResource`, `Version`, `LastUpdated`, `IsDeleted`.
3. Decompress via the existing `GzipResourceCompressor` (already shared with the write path — no new compression code) and materialize `SearchEntryResult` per row, preserving match-page order, mapping `IsPartial` → `SearchEntryMode.Include` / else `Match`.

**`GetExportRangesAsync`** does not touch the compiler — a direct `MIN`/`MAX`-based range query over `dbo.Resource.ResourceSurrogateId` via `ISqlExecutionService`, the same shape as any other SqlServer-native query in this project.

### Differential harness

New test project or new test class (decided at plan time) comparing `SqlServerCompiledSearchService` against `SqlEntityFrameworkSearchService` for the same queries, across a representative set spanning every leaf/composite search-parameter type, chain, include/revinclude (+`:iterate`), compartment, sort, `:missing`, count — mirroring Phase D's `DifferentialTestHarness` pattern (`SnapshotLegacyAsync`/`SnapshotNewAsync`, but comparing search *results* rather than written rows).

**Known, expected divergences** — the harness must assert these diverge in the documented direction, not flag them as failures:
- `CompartmentSearchQueryGenerator` never filters `ReferenceResourceTypeId` (only `ReferenceResourceId`) — a natural-id-collision risk across resource types that the compiler's `CompartmentSource` correctly closes. Compiler is right; legacy is wrong.
- `_include`'s `SearchParamId` filter: the live `IncludeProcessor` never filters by it at all (silently over-includes any reference parameter sharing a target type); the compiler filters correctly.
- Composite `:missing`: `SearchExpressionQueryBuilder.ApplyMissingSearchParameterExpressionAsync` has no `Composite` arm, logs a warning, and returns empty; the compiler returns real results.
- `:iterate` beyond one hop: the compiler supports one Kahn-sorted hop per expression by design (matches `microsoft/fhir-server`'s own open limitation, issue #1310); the live `IterateProcessor`'s runtime fixpoint goes further. Queries needing recursion beyond one hop are out of scope — not exercised by the harness's query set.

**Any other divergence is a real bug** — fixed in `Ignixa.Search.Sql`, harness re-run, not special-cased in the adapter or the harness.

### Cutover

`SqlServerRepositoryFactory` (the new composition root from prerequisite item 4) constructs `SqlServerCompiledSearchService` unconditionally for SqlServer-storage tenants — same storage-type gate Phase D's write cutover already uses, no feature flag. `Ignixa.DataLayer.SqlEntityFramework`'s `createSearchService` closure and `SqlEntityFrameworkSearchService` are left in place, untouched, as the reference implementation for FileSystem/other non-SqlServer storage types (if any) and as a rollback lever.

## Explicitly out of scope

Carried forward from the compiler's own roadmap, not silently dropped:
- **SQL plan-shaping "cache-breaking" investigation** (roadmap §"Open investigation"): whether `Emit`'s determinism needs an opt-in escape hatch for fhir-server's cache-breaking trick. Flagged as needing its own dedicated investigation pass; not blocking this phase.
- **SMART/compartment instance-level scope enforcement** (`OutputScopeFilter`): the compiler's `OutputTypeIds` field reserves the seat but nothing enforces it yet.
- **True multi-level `:iterate` recursion** beyond one hop.

## Testing

- Prerequisite fixes (1-4 above): each gets its own unit/integration test proving the specific bug is closed, per this initiative's existing per-task pattern.
- `SqlServerCompiledSearchService`: unit tests per search-parameter-type/feature area (leaf types, composites, chain, include/revinclude, compartment, sort, `:missing`, count), matching the compiler's own existing test granularity.
- Differential harness: the primary acceptance gate for cutover, per the Verification Strategy decision below.
- Full E2E suite re-run post-cutover (matching this session's established practice for anything touching the live search path).

## Verification strategy (decided)

Differential harness first, proven clean, **then hard cutover** — no feature flag, no shadow/dual-run period. Matches Phase D's exact pattern (`SnapshotLegacyAsync`/`SnapshotNewAsync` differential proof before the unconditional write-path swap).
