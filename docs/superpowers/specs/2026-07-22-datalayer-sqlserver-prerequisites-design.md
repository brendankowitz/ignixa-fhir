# Sub-project 2: DataLayer.SqlServer Prerequisites — Design

**Branch:** `worktree-ignixa-datalayer-sqlserver` (worktree `.claude/worktrees/ignixa-datalayer-sqlserver`). No new branch. Continues directly from sub-project 1 (compiler feature-parity, complete, tip `1d0962e1`).

## Context

Second of 3 ordered sub-projects replacing the original, too-large "Phase E" design (superseded). Sub-project 1 (compiler feature-parity) is complete and has zero file overlap with this sub-project — sub-project 1 touched only `Ignixa.Search.Sql`/`Ignixa.Search.Sql.Generators`; this sub-project touches `Ignixa.DataLayer.SqlServer`, its EF sibling's factory, `SearchCompartmentHandler` (Application layer), and one exception message in `Ignixa.Search.Sql`.

All 5 items were previously scoped in earlier sessions (a dedicated Fable structural review of `Ignixa.DataLayer.SqlServer`, and the original Phase E design's Fable review before the 3-way split) and re-verified against the current, post-sub-project-1 code before this design was written — none required re-deriving from scratch. Item 2 specifically needed real re-investigation given how much sub-project 1's Task 5 touched the exact method (`Lower.cs`'s `ExtractResourceColumnPredicates`) this bug also lives near — confirmed genuinely still present and unrelated to sub-project 1's changes (an orthogonal shape: `Or`-of-same-column-`Equal` for multi-value `_type` vs. a nested `And` from compartment composition).

## 1. `ct` → `cancellationToken` rename

`SqlServerFhirRepository.cs` — 21 occurrences of `CancellationToken ct` across ~15 method signatures (lines 94, 159, 229, 338, 345, 354, 461, 495, 535, 573, 610, 721, 768, 820, 849, 934, 997, 1010, 1038, 1056, 1068), confirmed the only file in the project with this pattern. CLAUDE.md's explicit "CRITICAL VIOLATION": name it `cancellationToken`. Mechanical rename, zero behavior change, no other files affected (no public API exposes this parameter name to callers outside the class).

## 2. `SearchCompartmentHandler` nested-composition fix

**Finding, re-verified against current code, corrected from an earlier over-broad framing**: `SearchCompartmentHandler.cs:84` composes `And(compartment, SearchOptions.Expression)`. When `SearchOptions.Expression` is itself an `And` (2+ ordinary params), the result is a nested `And(compartment, And(paramA, paramB))`. Contrary to an earlier assumption, this does **not** generally break — `LowerAnd` recursively lowers every child regardless of nesting depth and `Intersect`s the results (associative), so compartment + 2 purely-ordinary nested params already lowers correctly today.

The real, narrower trigger: `Lower.cs`'s `ExtractResourceColumnPredicates` (lines 222-251) only scans the **top-level** `And`'s **direct** children for resource-column codes (`_id`/`_type`/`_lastUpdated`) to pull into `OuterPredicate`. A resource-column predicate nested one level deeper (inside the inner `And`) is invisible to it, falls through to ordinary leaf dispatch, and `StructuralContext.Lower`'s `RejectResourceColumnCode` guard (line 45) throws — confirmed via direct trace for `GET /Patient/123/Observation?_id=X&category=lab`: compartment and `category` both lower fine, `_id` specifically throws.

**Design**: fix in `SearchCompartmentHandler`, not `Lower.cs` — this is a composition defect in the Application-layer caller, not a gap in what the compiler should express. `SearchOptions.Expression` is a `MultiaryExpression` (N-ary, not binary) when it's an `And`; instead of unconditionally wrapping a new `And(compartment, existing)`, `SearchCompartmentHandler` should check whether `existing` is already an `And` and splice `compartment` into that same flat child list (`And([compartment, ...existingChildren])`), falling back to the current 2-child wrap only when `existing` isn't already an `And`. This keeps the compiler's input contract simple (a caller is expected to hand it a flat top-level `And`) rather than making `Lower.cs` more permissive of malformed/nested input from any caller — a nested `And`-inside-`And` from a DIFFERENT, future caller would still (correctly) fail loudly rather than being silently tolerated.

## 3. Composition-root relocation

**Finding, re-verified**: `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs`, confirmed unchanged, still spans ~lines 271-488) directly `new`s every SqlServer type inline: `SqlServerSearchIndexReferenceDataCache` (~360), `SqlServerPostMergeExtensionUpdater` (~384), `SqlServerMergeRepository` (~389), `SqlServerFhirRepository` (~397), `GzipResourceCompressor` (~409).

**Design**: a new `SqlServerRepositoryFactory` class in `Ignixa.DataLayer.SqlServer` takes over this construction — same tenant-scoped inputs (`ISqlExecutionService`, `tenantId`, `ILoggerFactory`, `RecyclableMemoryStreamManager`), same objects built, pure relocation, not a redesign. `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` calls into it instead of constructing directly. Behavior-preserving — verified by the existing test suite (`test/Ignixa.DataLayer.SqlServer.IntegrationTests/`) staying green, not by new tests asserting new behavior.

## 4. `SqlServerFhirRepository.cs` cleanup

**Finding, re-verified**: file confirmed still 1094 lines. Class doc (lines 17-53, ~37 lines) is a Phase-D task-by-task changelog narrating Tasks 6-9, largely redundant with `docs/superpowers/plans/2026-07-20-ignixa-datalayer-sqlserver-phase-d.md`. `DeleteAsync` (line 225) carries an extended legacy-divergence essay explaining a semantic difference from the EF original, redundant with its own pinning test. `BatchWriteAsync` (line 351) has an inline wrapper-building/validation loop. History cluster confirmed at lines 606-712: `ExecuteHistoryQueryAsync`/`BuildHistorySql`/`AddSharedHistoryParameters`/`TryMapHistoryRow`/`ReadHistoryRow`/`HistoryRow` record — a complete, self-contained sub-engine with low coupling to the rest of the class (only shares `_compressor`/`_sqlExecutionService`/`_tenantId`/`_logger`).

**Design** (per the earlier structural review's Option A + C, both together — user's standing decision):
- **A**: condense the class doc to a few lines pointing at the phase plan doc; trim `DeleteAsync`'s essay to a few lines pointing at its pinning test; extract a `BuildResourceWrappers` helper method from `BatchWriteAsync`'s inline loop.
- **C**: extract the entire history cluster into a new `SqlServerHistoryQueryExecutor` collaborator class, constructed with the same `(ISqlExecutionService, int tenantId, GzipResourceCompressor, ILogger)` shape already established by `SqlServerMergeRepository`/`SqlServerPostMergeExtensionUpdater` (both of which `SqlServerFhirRepository` already delegates to). The 3 `IFhirRepository` history methods (`GetResourceHistoryAsync`/`GetTypeHistoryAsync`/`GetSystemHistoryAsync`) become thin delegators: resolve the resource-type ID (unchanged, stays on the repository), then delegate to the executor.

## 5. Diagnostic message improvement (folded in during design review)

**Finding**: `StructuralContext.Lower`'s `RejectResourceColumnCode` guard (line 45) already throws a reasonably diagnostic `NotSupportedException` when item 2's bug fires — it names the exact offending code and explains the guard's structural purpose ("only Lower.Run's top-level extraction pass handles these... throwing rather than routing a resource column into an unrelated table"). It does not name the likely *root cause* a caller should look for.

**Design**: a small, surgical addition to that existing message — when the guard fires, append a sentence naming the likely cause: a resource-column predicate arrived nested inside an `And`/`Or` that wasn't flattened before reaching `Lower.Run` (exactly item 2's failure mode), pointing the next developer who hits this at the actual fix (flatten the composed expression before calling `Lower`) instead of just the symptom. This is not a new structural-nesting detector — the existing guard already catches the real failure correctly; this only improves what it says when it does.

## Testing

- Item 1: no new tests needed (rename only); full suite must stay green.
- Item 2: a new test proving `GET /Patient/123/Observation?_id=X&category=lab`-shaped compartment search (compartment + 2+ ordinary params including one resource-column predicate) now lowers successfully instead of throwing — matching this session's established pattern of proving a fix via the exact previously-failing scenario, not a loosened one.
- Item 3: existing `Ignixa.DataLayer.SqlServer.IntegrationTests` suite must stay green unmodified in behavior — this is the regression proof for a pure relocation.
- Item 4: existing `SqlServerFhirRepository`-related tests must stay green; new tests for `SqlServerHistoryQueryExecutor` matching the existing history-method test coverage, now exercised through the extracted class directly as well as through the repository's thin delegators.
- Item 5: a test confirming the improved message text when the guard fires (exact string/substring assertion, not just "still throws").

## Process

Same rigor as sub-project 1: design → Fable adversarial review → writing-plans → Fable review of the resulting plan → subagent-driven-development with per-task review → final whole-branch review.
