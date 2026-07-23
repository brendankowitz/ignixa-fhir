# Sub-project 3: SqlServer-native search adapter — Design

**Branch:** `worktree-ignixa-datalayer-sqlserver` (worktree `.claude/worktrees/ignixa-datalayer-sqlserver`). No new branch.

## Context

This is the third and final sub-project of the original, too-large "Phase E" (read-path/search migration) design (`docs/superpowers/specs/2026-07-22-datalayer-sqlserver-phase-e-search-adapter-design.md`, superseded — that document's real requirements are still valid, only its packaging as a single monolithic phase was wrong). It was split into three ordered sub-projects:

1. **Compiler feature-parity** (`Ignixa.Search.Sql`) — complete, branch tip `1d0962e1`.
2. **`Ignixa.DataLayer.SqlServer` prerequisites** — complete, branch tip `0107b720`, pushed to `origin/worktree-ignixa-datalayer-sqlserver`.
3. **This sub-project** — the actual adapter, now unblocked.

Phases A-D (an earlier, separate DataLayer.SqlServer initiative) cut the FHIR **write** path over to `Ignixa.DataLayer.SqlServer` unconditionally, no feature flag. **Search/read never moved** — it is still served entirely by `SqlEntityFrameworkSearchService`, backed by its own `SearchIndexReferenceDataCache` instance, separate from the SqlServer write-side cache. This sub-project retargets that gap: the compiler becomes a new `ISearchService` implementation living in `Ignixa.DataLayer.SqlServer`, hard-cut-over the same way the write path was — no flag. `Ignixa.DataLayer.SqlEntityFramework` is left untouched as a reference implementation and rollback lever.

**Compiler-quality stance, unchanged from the original design:** if the differential harness surfaces a real bug in `Ignixa.Search.Sql`, it gets fixed in the compiler — never routed around in the adapter.

**Revision note:** this design went through one round of Fable adversarial review, which found two Critical gaps (pagination, export surrogate-ID range filtering) the first draft omitted entirely, plus a cluster of Important mechanical errors carried over unchecked from the superseded doc. Both Critical items required a user decision; both are now resolved and folded in below (§3, §4). This is materially larger than the first draft — see §Sizing at the end.

## Re-verification against current code (this design pass)

The original Phase E doc listed 4 prerequisites. Re-checked against the current branch tip before finalizing this design, since sub-projects 1 and 2 both touched adjacent code since that doc was written:

1. **`ct` → `cancellationToken` in `SqlServerFhirRepository.cs`** — done, sub-project 2 Task 1.
2. **`IncludeStage.Direction` dual-source-of-truth** (`Lower.BuildIncludeStages` deriving direction from which parameter list an include arrived through, vs. `Requires`/`Produces` deriving from `expression.Reversed`) — **already fixed**, independently of both sub-projects 1 and 2. Confirmed via `git log`: commit `fe019141` ("derive IncludeStage.Direction from IncludeExpression.Reversed, not caller's parameter list") predates this sub-project's work. Confirmed in current code: `Lower.ResolveInclude` derives `Direction` purely from `expression.Reversed` for every include regardless of which list it came from — single source of truth, no work needed here. (Re-confirmed against `Lower.cs:462-467` during the Fable review pass.)
3. **`SearchCompartmentHandler` nested-`And`** — fixed, sub-project 2 Task 2.
4. **Composition-root move** — done, sub-project 2 Task 3 (`SqlServerRepositoryFactory`).

So of the original 4 prerequisites, 3 are done and 1 was already a non-issue. This sub-project needed to re-derive its own prerequisite list rather than trust the original doc's — see below.

## Prerequisite / early-task work

### 1. `ISearchService`'s `ct` → `cancellationToken`

`src/Application/Ignixa.Domain/Abstractions/ISearchService.cs` still declares `CancellationToken ct = default` on all three members — the same CLAUDE.md "CRITICAL VIOLATION" sub-project 2 already fixed on `IFhirRepository`, just on a sibling interface nobody has touched yet. `SqlServerCompiledSearchService` is a new implementation of this interface; CA1725 (parameter names must match the base interface declaration, enforced as a build error via `TreatWarningsAsErrors` + `AnalysisLevel=latest-All`) means writing it correctly from day one requires the interface to already say `cancellationToken`.

**Decision: rename now**, cascading through `ISearchService` + its 2 existing implementations (`SqlEntityFrameworkSearchService`, `FileBasedSearchService`) + call sites, as a small early task — same pattern as sub-project 2 Task 1.

### 2. `SearchCompiler` needs a pre-built-`SearchOptions` entry point that also carries bound parameters

The original design doc's architecture section claimed the adapter "drives the compiler via `SearchCompiler.CompileAsync`... not a re-implementation of Build→Resolve→Lower→Emit." Checked against the real signature and the real call shape `ISearchService` requires, and this claim doesn't hold as written:

- `SearchCompiler.CompileAsync(string resourceType, IReadOnlyList<QueryParameter> parameters, ISearchOptionsBuilder optionsBuilder, ISymbolResolver resolver, ...)` does **Build** (`optionsBuilder.Build(...)`) internally, then Resolve → Lower → Emit.
- `ISearchService.SearchStreamAsync<TSearchOptions>(TSearchOptions searchOptions, ...)` receives an **already-built** `Ignixa.Search.Models.SearchOptions` — confirmed by reading `SqlEntityFrameworkSearchService`, which just casts `TSearchOptions` straight to it. Build already happened upstream, by whatever produced that `SearchOptions` (the request pipeline's `SearchOptionsBuilder`, before `ISearchService` is ever called).

Calling `CompileAsync` as-is would mean re-deriving raw `QueryParameter`s from an already-built `SearchOptions` just to redo Build — wasteful and lossy. The fix: add a variant to `SearchCompiler` that accepts an already-built `SearchOptions` directly and skips Build, doing Resolve → Lower → Emit only.

**A second, real gap caught during Fable review: `SearchTrace`'s existing shape cannot execute.** `SqlBuilder.Run` returns `EmittedSql(Sql, Parameters, TextRanges)` (`Builders/EmittedSql.cs:12-15`) — `Parameters` is `IReadOnlyList<EmittedSqlParameter>`, the `@pN` → value bindings the SQL text needs to actually run. But `SearchCompiler.CompileAsync` discards them when building the trace: `EmittedSqlTrace` is `(string Sql, IReadOnlyList<SqlTextRange> Ranges)` only (`Tracing/EmittedSqlTrace.cs:6`), constructed at `SearchCompiler.cs:98` as `new EmittedSqlTrace(emitted.Sql, emitted.TextRanges ?? [])` — `emitted.Parameters` is dropped on the floor. A caller with only `SearchTrace.Sql!.Sql` (note: the record's field is named `Sql`, not `SqlTrace` — corrected from the first draft) has SQL text with `@p0`, `@p1`, ... placeholders and nothing to bind them to.

**Fix, folded into the same prequel task:** extend `EmittedSqlTrace` to also carry `Parameters` (its one construction site already has `emitted.Parameters` in scope — `new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? [])`). This is a small, additive change to an existing record with a single call site; it does not ripple beyond `Ignixa.Search.Sql`'s own tracing test suite (which will need its one construction-site assertion updated, not rewritten).

**Interaction with PR #353** (`brendankowitz-implement-sql-search-gaps`, open, `MERGEABLE`, expected to merge before or during this sub-project's execution): verified via `gh pr diff 353` — that PR adds a `CompileWithTimeProviderAsync` overload for `:ap`'s deterministic time capture, still taking raw `QueryParameter`s + a builder. It does not add the pre-built-`SearchOptions` entry point this sub-project needs. The new overload this sub-project adds must also thread a `TimeProvider`/`approximationReferenceTime` through, mirroring `CompileWithTimeProviderAsync`, so `:ap` continues to work correctly through the new entry point.

## 3. Pagination bridge — decided: extend `Emit` with real OFFSET/FETCH support

**The gap, found during Fable review, not in the first draft:** `Ignixa.Search.Sql` is keyset-only. `PageSpec` (`Ast/SortSpec.cs:62-65`) carries a keyset `Boundary`; `SqlBuilder` never renders `OFFSET`/`FETCH` anywhere. `KeysetContinuationToken`'s own doc comment states it is explicitly "not compatible with, and not intended to bridge to, `Ignixa.Search.Models.ContinuationToken`" — the offset+count token the Application layer already mints and decodes independently of whichever data layer is underneath (`StreamingBundleSerializer.cs`, `SearchResolver.cs`), and which `SqlEntityFrameworkSearchService` already decodes today. Cutting the adapter over without addressing this would mean either breaking pagination for every caller, or quietly changing the Application layer's token contract mid-cutover.

**Decision:** extend `Lower`/`Emit` with a second, offset-based paging mode, so the adapter can request true SQL `OFFSET ... FETCH NEXT` compilation and pass the *existing* offset+count `ContinuationToken` straight through with no Application-layer contract change.

**Shape (architecture-level; exact types are a plan-time detail):**
- `Lower.Run` gains a new optional parameter, mutually exclusive with **both** the existing keyset `page: PageSpec?` **and** `top` (T-SQL forbids `TOP` and `OFFSET` in the same query, SQL Server error 10741) — supplying more than one of the three is a caller error, throw, don't silently prefer one. Represents `(int Offset, int Limit)`.
- `Emit` renders `OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY` **inside the match-page CTE, where the keyset boundary predicate and its `ORDER BY` already live** (`SqlBuilder.cs`'s match-page construction, ~lines 110-132) — not on the plan's outer/final `SELECT`. On an includes-bearing plan the final result is a `UNION ALL` of match-page and include rows; paging the outer query would page over match+include rows combined, which is wrong — the page boundary belongs to the match set alone, same as keyset paging already correctly scopes it. The existing `ORDER BY`-only-when-`Top is not null` gate on the match-page CTE extends to cover offset mode too (an `ORDER BY` inside a CTE is legal alongside `OFFSET`, so this composes cleanly).
- The deterministic `ORDER BY` construction `Emit` already builds for keyset paging (sort keys plus resource-type-id/surrogate-id tiebreakers, required for `OFFSET` to give stable pages at all) is reused unchanged — only the boundary-vs-offset mechanism differs.
- **The two-phase missing-value sort (`SortPhase.Valued`/`MissingPrimary`; per `SortSpec.cs:36-41`, "the executor drives the transition between the two") is genuinely new adapter logic — there is no existing reference implementation to read.** (Corrected from an earlier draft of this section, which incorrectly claimed `SqlEntityFrameworkSearchService` already implements this two-phase shape; it doesn't — the legacy service sorts in one query via Min/Max subqueries relying on SQL Server's default NULL-sorts-first behavior, a materially different approach, not a reference for this adapter's phase-driving loop.)

  The naive version of this loop — "only run `MissingPrimary` if `Valued`'s page came back short" — is wrong under a *global* offset token, because the adapter cannot tell, from an empty `Valued` result at `offset > 0`, whether the correct `MissingPrimary` offset is `0` or `offset - |Valued total|`: it doesn't know `|Valued total|` without asking. Concrete failure: `Valued` has 10 rows, page size 5 — a request at offset 10 needs `MissingPrimary` offset 0, a request at offset 15 needs `MissingPrimary` offset 5, and both look identical ("Valued returned 0 rows") to a loop that only inspects the Valued page itself. As written, this either duplicates or skips rows across the phase boundary.

  **Fix:** when the requested page could plausibly cross the phase boundary (i.e., the `Valued`-phase page comes back short of `Limit`, or would start past a not-yet-known `Valued` total), the adapter first runs a `CountOnly` compile of the `Valued` phase (the compiler already supports `CountOnly` — no new mechanism) to learn the exact `Valued` total, then computes the correct `MissingPrimary` offset as `requestedOffset - valuedTotal` and runs that phase. This fits entirely within the already-decided architecture (offset-mode compilation, existing `CountOnly`); it's a sequencing detail this design doc must state explicitly rather than leave for a plan-writer to discover the hard way.
- `SqlServerCompiledSearchService` decodes the incoming `SearchOptions.ContinuationToken` the same way `SqlEntityFrameworkSearchService` already does (existing decode logic, not new) and translates it to the new offset-mode parameter. **Token minting stays exactly where it already is today: the Application layer (`StreamingBundleSerializer`/`SearchResolver`), not the data-layer service** — the adapter, like its EF sibling, only ever decodes an incoming token; it does not mint outgoing ones. This means phase state cannot be smuggled through the token (it's outside the adapter's control) — the `CountOnly`-based disambiguation above is the only place that problem can be solved.

This is real, additive `Ignixa.Search.Sql` feature work — not a one-line change — and is scoped as early tasks inside this sub-project's own plan (not a separate preceding sub-project), per explicit decision: the adapter is the only consumer of this feature and there's no independent value in shipping it alone.

## 4. Export surrogate-ID range filtering — decided: extend the existing `OuterPredicate` mechanism

**The gap, found during Fable review:** `GetExportRangesAsync` hands out `(StartId, EndId)` surrogate-id ranges for parallel export workers. Each worker is meant to feed its assigned range back in via `SearchOptions.StartSurrogateId`/`EndSurrogateId` (`Models/SearchOptions.cs:88-94`), which `SqlEntityFrameworkSearchService` already applies as a filter today. `Ignixa.Search.Sql` has **zero** support for a surrogate-range predicate — nothing on `Lower.Run`, no AST node for it. Cutting the adapter over as-is would silently break export partitioning: every worker would fetch the entire resource type.

**Decision:** extend the compiler's existing `QueryPlan.OuterPredicate` mechanism — the same mechanism `_id`/`_type`/`_lastUpdated` resource-column predicates already use (an outer `INNER JOIN dbo.Resource` + `WHERE`, not the CTE graph; see `Lowering/ResourceColumnLoweringRule.cs:11` and `Lowering/Lower.cs:222`'s `ExtractResourceColumnPredicates`). A surrogate-id range is structurally the same class of filter — a resource-column-level range check, not a search-expression predicate.

**Shape:** `Lower.Run` gains a new optional parameter, e.g. `surrogateIdRange: (long Start, long End)?`. When supplied, it ANDs an additional `ResourceSurrogateId >= @start AND ResourceSurrogateId <= @end` predicate into `QueryPlan.OuterPredicate`, composed via the same `Predicate.And` machinery `ExtractResourceColumnPredicates` already uses — no new `CteDefinition` kind, reusing the existing outer-WHERE-on-`dbo.Resource` rendering path in `SqlBuilder.cs` (already handles a non-null `OuterPredicate` at 4 call sites: `:40`, `:63`, `:118`, `:134`). `SqlServerCompiledSearchService` reads `StartSurrogateId`/`EndSurrogateId` off the incoming `SearchOptions` (only when both are set, matching the EF sibling's existing "must be set together" contract) and passes them through.

Small, additive, and consistent with how the compiler already models this exact class of filter — scoped as an early task alongside §3, same reasoning (adapter is the only consumer, no independent shipping value).

## 5. `SqlServerSymbolResolver` — a new component, not yet designed in the first draft

Found during Fable review: `Resolve.RunAsync` requires an `ISymbolResolver`; the only implementation today is `SqlEntityFrameworkSymbolResolver` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSymbolResolver.cs`), in the EF project. `Ignixa.DataLayer.SqlServer` has no `Search/` folder and no resolver — this sub-project must build one.

**A real design decision inside this component, not just plumbing:** PR #353 grows `ISymbolResolver` with `GetSystemIdAsync`/`GetQuantityCodeIdAsync` (read-only, "not found is data" — an unresolved system/code lowers to `Predicate.False`, never an error). `SqlServerSearchIndexReferenceDataCache`'s equivalent methods (`SqlServerSearchIndexReferenceDataCache.cs:314,364`) are **get-or-create** — they `INSERT` a new catalog row on a miss, correct for the write path (a resource being indexed may legitimately introduce a system/code the catalog hasn't seen yet) but wrong for the read path (a search filtering on an unknown system/code must not silently create catalog data as a side effect of a query). `SqlServerSymbolResolver` (new class, `Ignixa.DataLayer.SqlServer/Search/`) wraps the same cache but calls **read-only, miss-returns-null** lookup paths — read `SqlEntityFrameworkSymbolResolver`'s own resolution logic first as the reference for how the EF sibling already draws this line, then mirror the read-only/write-only split onto the SqlServer cache (adding new read-only methods to `SqlServerSearchIndexReferenceDataCache` if it doesn't already expose one, rather than reusing the get-or-create methods from a read path).

**Negative-sentinel trap to avoid, not just "mirror the pattern":** the cache's system/quantity-code maps (`_systemCache`/`_quantityCodeCache`) treat every entry as a real resolved ID — they don't participate in the cache's own negative-caching convention (`MissingSentinel`, already used by its *other* two maps for exactly this "not found" case). A naive read-only wrapper that just does a dictionary lookup against these two maps and returns null on a plain miss will work for a genuinely uncached value, but will not correctly distinguish "never looked up" from "confirmed not found" the way the cache's other two maps already do — the new read-only methods must use the same `MissingSentinel` negative-caching convention, not read the get-or-create maps as-is expecting them to already behave that way.

`Resolve.RunAsync` also takes optional `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` — confirm at plan time which existing implementations of these (if any are already tenant/SqlServer-agnostic) can be reused as-is versus need their own SqlServer-side wiring through `SqlServerRepositoryFactory`.

## 6. Residual compiler gaps — explicit disposition, not blockers

The compiler-parity gate ("no fallback dispatch, closing every gap is a prerequisite for cutover") was written against a 5-item list (`docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md`). Checked PR #353's actual diff against that list directly:

| Gap | Status |
|---|---|
| `:ap` comparator | Closed by PR #353 |
| Quantity System/Code matching | Closed by PR #353 |
| `_lastUpdated` partial-precision ranges | **Still open** — PR #353 adds `:ap`'s range handling only; the `if (value.Start != value.End) throw` guard in `ResourceColumnLoweringRule.cs` remains for every other comparator (`eq`/`ne`/`gt`/etc.) |
| Reference `:identifier`/type modifiers | **Still open, and not compiler-only** — `ReferenceSearchParam` has no identifier-search columns at all (schema gap; the live schema is `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/ReferenceSearchParam.sql`, not the retired `97.sql`) and `ReferenceSearchParameterRowGenerator` has zero identifier logic (write-path gap) |
| True multi-level `:iterate` | Not actually a "gap" — a deliberate, permanent scope boundary matching `microsoft/fhir-server`'s own open limitation (issue #1310) |

**Decision, confirmed with the user:** proceed with this sub-project's design and build now rather than waiting on the two still-open items. Whether the *hard cutover* itself waits on them closing is a separate decision, made when the adapter and harness are otherwise ready — not decided by this document.

**Failure-mode correction from Fable review — the two residual gaps do NOT behave the same way, and only one is harness-observable:**
- **`_lastUpdated` partial-precision**: a real, attributable compiler failure. `ResourceColumnLoweringRule.cs:95-101` throws `NotSupportedException` with a specific message; `SearchCompiler` catches it at the Lower/Emit boundary (`SearchCompiler.cs:104`) into `SearchTrace.Failure`. Per §Architecture below, the adapter translates that into `RequestNotValidException` (HTTP 400) — so the caller-observable failure mode is a 400 with the lowering rule's message, not a raw `NotSupportedException`. The differential harness can assert this specific, named 400 to distinguish "known gap" from "new bug."
- **Reference `:identifier`**: **not a compiler failure at all, and not harness-observable.** The parameter never reaches `Ignixa.Search.Sql` — `SearchExpressionBinder.BindAtomic` and `SearchValueExpressionBuilderHelper.Visit(ReferenceSearchValue)` reject any reference modifier except `:type` at parse time, inside `SearchOptionsBuilder`'s own catch, so the parameter is silently dropped before either engine ever sees it. **Both the legacy and compiled paths already behave identically here today** — there is no adapter-vs-legacy divergence to assert, and nothing for the harness to compare. Removed from the differential harness's known-divergence list below; it isn't one.

## Architecture

### New component: `SqlServerCompiledSearchService : ISearchService`

Lives in `Ignixa.DataLayer.SqlServer/Search/` (new folder, mirrors the EF sibling's `Search/` folder), alongside the new `SqlServerSymbolResolver` (§5).

**`SearchStreamAsync<TSearchOptions>` / `CountAsync<TSearchOptions>`** — cast `TSearchOptions` to `Ignixa.Search.Models.SearchOptions` (same pattern `SqlEntityFrameworkSearchService` already uses), then drive the compiler via the new pre-built-`SearchOptions` `SearchCompiler` entry point (§2), passing the pagination (§3) and, when present, surrogate-range (§4) parameters. The adapter checks the returned `SearchTrace.Failure` first: non-null translates to `Ignixa.Domain.Exceptions.RequestNotValidException` before touching the database — no query executes on a failed compile.

On success, `SearchTrace.Sql!.Sql` is the parameterized T-SQL text and `SearchTrace.Sql!.Parameters` (post-§2 fix) is the `@pN` → value binding list — both are needed to execute. Execution:

1. Run the compiled SQL via `ISqlExecutionService.ExecuteReaderAsync`, tenant-scoped like every other SqlServer-native query, binding every entry in `Parameters`. **`ExecuteReaderAsync` fully materializes its result set — there is no server-side-cursor streaming primitive in this codebase** (same precedent `SqlServerHistoryQueryExecutor` already establishes); `SearchStreamAsync`'s `IAsyncEnumerable` yields from that in-memory page, consistent with how the history path already works, not a new streaming guarantee.
2. **The result column shape is not fixed — it depends on the compiled plan's own structure, not a single row format.** Per `EmittedSql.cs:7-10`: `(T1, Sid1)` when the plan has no includes and no sort; `SortValue0..N` columns appear only when sort is active; `IsMatch`/`IsPartial` columns appear only when the plan has includes. The adapter must branch on the compiled `QueryPlan`'s own `Includes`/`Sort` presence to pick the right row-mapping — not sniff the reader's columns — mirroring how `PlanExplainer`/`SqlBuilder` already decide this internally. `CountOnly` plans (used by `CountAsync`) return a single scalar and skip steps 2-3 below entirely; the scalar is `COUNT_BIG(...)` (SQL `bigint`/`long`), while `ISearchService.CountAsync` returns `ValueTask<int>` — the adapter casts with `checked`, so a genuine `int` overflow throws rather than silently truncating (matches this codebase's "fail fast for programmer errors" stance; a >2 billion single-type match count is not an expected real-world case, but truncating it would be a silent correctness bug if it ever happened).
3. **`IsMatch`/`IsPartial` mapping, corrected from the first draft** (which copied the superseded doc's error verbatim): `IsMatch = 1` on a match-page row, `0` on an included row; `IsPartial = 1` only on an included row whose stage's `TOP(@Limit)` truncated further rows. So: `IsMatch == 0` → `SearchEntryMode.Include`; `IsMatch == 1` → `SearchEntryMode.Match`. `IsPartial` is a truncation marker on included rows, not the include/match discriminator itself.
4. Batch-fetch the corresponding `dbo.Resource` rows by `SurrogateId`, chunked via `.Chunk(100)` — matching `SqlServerPostMergeExtensionUpdater`'s existing pattern — fetching `RawResource`, `Version`, `LastUpdated`, `IsDeleted`.
5. Decompress via the existing `GzipResourceCompressor` (already shared with the write path — no new compression code) and materialize `SearchEntryResult` per row, preserving match-page order, applying step 3's mapping.

**`GetExportRangesAsync`** does not touch the compiler — a direct `MIN`/`MAX`-based range query over `dbo.Resource.ResourceSurrogateId` via `ISqlExecutionService`, mirroring the EF sibling's single-aggregation-query shape (min/max/count together, not three separate subqueries) but expressed as raw SQL instead of EF LINQ. Its output feeds the §4 surrogate-range filter on the consuming side, which is what makes export actually partition correctly post-cutover.

### Differential harness

New test class (exact file organization — new test project vs. new class in an existing one — decided at plan time) comparing `SqlServerCompiledSearchService` against `SqlEntityFrameworkSearchService` for the same queries, across a representative set spanning every leaf/composite search-parameter type, chain, include/revinclude (+`:iterate`), compartment, sort (both phases), paging (both directions across a page boundary), `:missing`, count — mirroring Phase D's `DifferentialTestHarness` pattern (`SnapshotLegacyAsync`/`SnapshotNewAsync`, comparing search *results* rather than written rows).

**Known, expected divergences** — the harness must assert these diverge in the documented direction, not flag them as failures:
- `CompartmentSearchQueryGenerator` never filters `ReferenceResourceTypeId` (only `ReferenceResourceId`) — the compiler's `CompartmentSource` correctly closes this; compiler is right, legacy is wrong.
- **`_include`'s `SearchParamId` filter — scope corrected from the first draft.** The unfiltered behavior belongs only to `IncludeProcessor`, which serves the **multi-type buffered search path** (`ResourceType` null/empty). The single-type streaming path uses `BuildIncludeQuery`, which **already filters by `SearchParamId` correctly** today. The harness must scope this divergence assertion to multi-type/wildcard `_include` queries specifically — asserting it against an ordinary single-type `_include` query will find no divergence and the assertion will fail on its own premise.
- Composite `:missing` — `SearchExpressionQueryBuilder.ApplyMissingSearchParameterExpressionAsync` has no `Composite` arm, logs a warning, returns empty; the compiler returns real results.
- `:iterate` beyond one hop — the compiler supports one Kahn-sorted hop per expression by design; the live `IterateProcessor`'s runtime fixpoint goes further. Not exercised by the harness's query set.
- `_lastUpdated` partial-precision ranges — the compiler surfaces a `RequestNotValidException` (400) naming the exact lowering-rule message (§6); legacy silently flattens to a single instant and searches only that. The harness asserts the compiled path's specific 400, not merely "throws something."
- **Missing-value sort ordering** — a real, deliberate engine divergence, not a compiler bug, so it must be named here or the harness's own "any other divergence is a real bug" rule will misfile it. Legacy sorts a NULL key *first* in ascending order (SQL Server's default null-ordering, which `SqlEntityFrameworkSearchService`'s own code comment states explicitly). The compiler's two-phase model (`Valued` then `MissingPrimary`) always places missing-value rows *after* every valued row, regardless of sort direction — matching `microsoft/fhir-server`'s own missing-last model, not SQL Server's default. For an ascending sort, page membership and ordering differ between the two engines. The harness's sort-query set must assert this specific, documented direction rather than treat any ascending-sort-with-nulls result mismatch as a new bug.

**Reference `:identifier`/type modifiers is intentionally absent from this list** — per §6, both engines already behave identically (silent parse-time drop), so there is nothing to assert as a divergence.

**Open item to confirm at plan time, not yet resolved by this design: `_sort=_id`.** Legacy sorts `_id` natively against `ResourceId` on the resource row itself. `Lower.BuildSortKey` has no `_id` case — `_id` is token-typed, so it would fall through to the generic `Aggregated` path over `TokenSearchParam`, a table `_id` is never indexed into, producing an `INNER JOIN` that matches nothing (an empty `Valued` phase, not a thrown error — a silent wrong-result risk, not a loud failure). Whether this is actually reachable depends on `_id`'s `SortStatus` in `SearchOptionsBuilder` (gated on `SortStatus == Enabled`) — not yet chased down in this design pass. Before this sub-project's plan is written, confirm either that `_id` is already gated off as an unsupported sort key upstream (in which case nothing to do here), or add an explicit `_id` case to `BuildSortKey` (sorting by the resource-column surrogate id / `ResourceId`, whichever is correct) before the differential harness can safely include `_sort=_id` in its query set.

**Any other divergence is a real bug** — fixed in `Ignixa.Search.Sql`, harness re-run, never special-cased in the adapter or the harness.

### Cutover

`SqlServerRepositoryFactory` (sub-project 2's composition root) constructs `SqlServerCompiledSearchService` unconditionally for SqlServer-storage tenants — same storage-type gate the write-path cutover already uses, no feature flag. `Ignixa.DataLayer.SqlEntityFramework`'s `createSearchService` closure and `SqlEntityFrameworkSearchService` are left in place, untouched, as the reference implementation for any non-SqlServer storage type and as a rollback lever.

**Sequencing gate, within this sub-project's own plan:** differential harness proven clean first, *then* hard cutover — no feature flag, no shadow/dual-run period. Matches Phase D's exact pattern. This is a task-ordering constraint inside this sub-project's plan, not an external decision point.

## Explicitly out of scope

Carried forward from the compiler's own roadmap and this design pass, not silently dropped:
- **SQL plan-shaping "cache-breaking" investigation** — whether `Emit`'s determinism needs an opt-in escape hatch for fhir-server's cache-breaking trick. Needs its own dedicated investigation pass; not blocking this sub-project.
- **SMART/compartment instance-level scope enforcement** (`OutputScopeFilter`) — the compiler's `OutputTypeIds` field reserves the seat but nothing enforces it yet.
- **True multi-level `:iterate` recursion** beyond one hop.
- **`_lastUpdated` partial-precision ranges** — compiler-only gap, deliberately not closed by this sub-project (see §6).
- **Reference `:identifier`/type modifiers** — schema+write-path feature, not this sub-project's job (see §6).

## Testing

- Prequel/early tasks (§1-§5): each gets its own unit/integration test proving the change is behavior-preserving (rename) or correctly wired (`SearchCompiler` entry point including `:ap`'s `TimeProvider` threading and `Parameters` carrying; offset-mode paging; surrogate-range `OuterPredicate` extension; `SqlServerSymbolResolver`'s read-only-vs-get-or-create split), per this initiative's existing per-task pattern.
- `SqlServerCompiledSearchService`: unit tests per search-parameter-type/feature area (leaf types, composites, chain, include/revinclude, compartment, sort — both phases, paging — both pagination-affecting code paths, `:missing`, count), matching the compiler's own existing test granularity.
- Differential harness: the primary acceptance gate for cutover.
- Full E2E suite re-run post-cutover, matching this session's established practice for anything touching the live search path.

## Verification strategy (decided)

Differential harness first, proven clean, **then hard cutover** — no feature flag, no shadow/dual-run period. Matches Phase D's exact pattern (`SnapshotLegacyAsync`/`SnapshotNewAsync` differential proof before the unconditional write-path swap) and the original (superseded) Phase E design's own decision, re-confirmed here.

## Sizing

This design is materially larger than its first draft, and larger than either prior sub-project (6 and 9 tasks respectively): the `ct` rename, the `SearchCompiler` entry point + `Parameters`-carrying fix, offset/FETCH pagination in `Lower`/`Emit`, `OuterPredicate` surrogate-range extension, a new `SqlServerSymbolResolver` component, the adapter service itself (multiple reader shapes, two-phase sort loop, export ranges), the differential harness, and the cutover task. Expect a plan comparable to or larger than sub-project 1's 9 tasks — this is noted here explicitly rather than left for the plan-writer to discover; if the resulting task count or interdependency graph turns out to be unwieldy for one subagent-driven-development run, that's a call to make at plan-review time, not a surprise mid-execution.
