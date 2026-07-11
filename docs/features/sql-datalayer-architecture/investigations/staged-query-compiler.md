# Investigation: Staged Adoption of a Compiler-Shaped SQL Pipeline

**Feature**: sql-datalayer-architecture
**Status**: Phase 0-1 Complete (PR #328), Phase 2-3 Recommended (not yet scoped)
**Created**: 2026-07-11
**Updated**: 2026-07-11 (PR review pass)

> Phase 0 (test baseline + mechanical dedup) and Phase 1 (visitor adoption) below have shipped — see `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md` for the executed plan, its ledger, and its Post-Plan section for findings and prerequisites carried forward into Phase 2/3.

## Approach

Adapt the ms-fhir-server "SQL conversions as a compiler" model (`docs/superpowers/plans/2026-07-10-fhir-search-semantic-expression-foundation.md`) to Ignixa's situation, but scoped down and reordered to match what the [audit](current-state-audit.md) actually found broken. Ignixa differs from ms-fhir-server in two ways that change the plan:

1. Ignixa's `Expression`/`IExpressionVisitor` tree in Core is **already shared** across SQL, CosmosDB, and file-based backends — ms-fhir-server built this sharing as Plan 1-2's first deliverable; Ignixa gets to start from it.
2. Ignixa's pain is concentrated in **dispatch organization and catalog duplication**, not in query correctness or missing optimizer sophistication — so the highest-leverage, lowest-risk work (Phases 0-1 below) isn't in ms-fhir-server's plan at all, because ms-fhir-server's SQL backend didn't have this problem to begin with.

Four phases, each independently shippable and reversible. Stop after any phase if the next one isn't earning its cost — this is explicitly not a commitment to build all four.

### Phase 0 — Mechanical dedup (low risk, do first)

Extract the nine duplicated `BinaryOperator → EF predicate` switches in `SearchParameterQueryGenerator.cs` (audit finding 2) into one shared helper — e.g. a static `ComparisonPredicates` class with a method per field type (`DateTime`, `decimal`, etc.) taking `(BinaryOperator, TValue)` and returning the compiled predicate, or an `Expression<Func<T,bool>>` factory keyed by operator. Extract the token "empty-system-means-NULL" and "128-char code overflow" conventions (audit finding 3b) into one named helper (e.g. `TokenCodeStorage.Split(code)` / `TokenCodeStorage.IsExplicitNoSystem(system)`) referenced by both `RowGenerators/TokenSearchParameterRowGenerator.cs` and `Search/CompositeSearchParameterQueryGenerator.cs`.

No behavior change, no schema change, no new abstraction — pure Extract Method. This is the fastest way to shrink `SearchParameterQueryGenerator.cs` from 2113 lines and eliminate the most obvious duplication risk (a bug fixed in one of the nine copies and missed in the other eight).

### Phase 1 — Adopt the existing visitor contract (medium risk, highest leverage)

Make `SearchExpressionQueryBuilder` implement Core's `IExpressionVisitor<TContext, Task<IQueryable<ResourceEntity>>>` (or a purpose-built async visitor interface alongside it) instead of the `expression switch` type-pattern dispatch at `SearchExpressionQueryBuilder.cs:80-92`. Give `ChainedExpressionProcessor`, `CompartmentSearchQueryGenerator`, `PatientEverythingQueryGenerator`, `RevIncludeProcessor` etc. a common signature (they already all do "subtree → matching resource ids"; today they just don't share an interface for it).

This is the direct analog of ms-fhir-server Plan 1 — but note the difference: ms-fhir-server Plan 1 is about *introducing* a semantic leaf into an existing visitor pipeline. Ignixa Plan 1 is about *starting to use the visitor pipeline that already exists*. Payoff: a new `Expression` subtype added to Core becomes a compile error in the SQL visitor instead of a runtime `NotSupportedException` (audit finding 1); `SearchParameterQueryGenerator`'s reflection-based `InExpression<T>` handling (audit finding 3a) can be replaced by a generic visitor method resolved at compile time.

**Prerequisite**: per the audit's test-coverage finding, add focused unit tests around the *current* behavior of `SearchParameterQueryGenerator`'s resource-level-parameter handling (`_id`, `_lastUpdated`, `_ttl`, `_type`) and `CompositeSearchParameterQueryGenerator`'s five composite shapes before restructuring the dispatch, so a regression during Phase 1 fails a unit test, not an E2E suite.

### Phase 2 — Preserve composite structure through a semantic leaf (medium-high risk, targeted)

This is where Ignixa's version of ms-fhir-server's core idea (a semantic predicate expression, lowered by exactly one compatibility boundary) earns its cost — but scoped to the one place structure loss is actually expensive today: **composite search parameters** (audit finding 3, `ExtractComponentExpressions` and the `IsReferenceExpression`/`IsTokenExpression` sniffing in `GenerateReferenceTokenQueryAsync`).

Add a semantic composite-component expression that retains, from parse time, each component's resolved `SearchParameterInfo` and position — mirroring `SearchParameterPredicateExpression` from the referenced plan, but only for the composite case, not a system-wide relowering of every expression. Field-level lowering (`FieldName`/`BinaryExpression`/`StringExpression`) stays the default path for CosmosDB and file-based backends and for all non-composite SQL search — unlike ms-fhir-server, Ignixa doesn't need a system-wide semantic-first rewrite to fix its actual problem. `DetermineCompositeType` becomes unnecessary for expressions that already carry typed component identity; `IsReferenceExpression`/`IsTokenExpression` sniffing (workaround for DocumentReference's swapped `relationship` order) becomes unnecessary once component identity is carried explicitly instead of inferred.

### Phase 3 — Data-driven search-parameter catalog (medium risk)

Replace the informal, duplicated catalog described in audit finding 4 (facts spread across ~19 `RowGenerators/*` files and re-encoded in `Search/*QueryGenerator`) with one declarative source per `SearchParamType` (and composite shape): `{ EF entity/table, column mapping, value-normalization rules }`. Both write path (`RowGenerators`) and read path (`Search/*QueryGenerator`) consult it instead of encoding the same facts twice. This is Ignixa's scoped-down answer to ms-fhir-server Plan 4 — no cost-based optimizer needed, since Ignixa's schema is fixed and known ahead of time; this is closer to a lookup table than a planner.

### Phase 4 — Full logical/physical/typed-SQL pipeline (deferred, not recommended now)

Ms-fhir-server Plans 3, 5, 6 (logical relational plan, memo optimizer/costing/plan cache, typed SQL AST + differential execution) solve problems — cost-based plan selection across alternative physical strategies, safe canary rollout of new query shapes — that the audit found no evidence Ignixa currently has. Building this now would be solving a problem the codebase doesn't report having yet, which is the over-engineering CLAUDE.md's YAGNI section warns against. Revisit only if Phases 0-3 don't resolve recurring "special-case per resource type" bugs, or if a genuine multi-strategy query-planning need emerges (e.g. choosing between index-seek and full-scan strategies per parameter combination at scale).

## Tradeoffs

| Pros | Cons |
|---|---|
| Phased, each step independently shippable and revertible (CLAUDE.md reversibility check) | Four phases is slower than a single rewrite; requires sustained follow-through across multiple PRs |
| Phase 0-1 are near-zero risk and immediately shrink the worst file (2113 lines) and close the reflection/duplication gaps | Phase 2 (composite semantic leaf) touches the one part of the `Expression` tree contract shared with Cosmos/file backends — needs care even though scoped to composites only |
| Reuses Core's existing visitor infrastructure instead of building new machinery | Phase 3's catalog is a real new abstraction — risk of it becoming its own source of ad-hoc-ness if not kept strictly data-only (no logic creeping in) |
| Directly addresses the audit's three concrete failure modes (operator-switch duplication, composite structure loss, read/write convention duplication) rather than a generic "rewrite it" | Doesn't (by design) chase ms-fhir-server's later stages (costing, canary) — if Ignixa turns out to need those, this plan doesn't provide them |

## Alignment

- [x] Follows architectural layering rules — all four phases stay inside `Ignixa.DataLayer.SqlEntityFramework`; no Application/Domain changes required.
- [x] Developer Experience — Phase 1's compile-time exhaustiveness check is a direct DX improvement (new Expression type → compiler error, not a 2am `NotSupportedException`).
- [x] Specification compliance — Phase 2 explicitly fixes the DocumentReference `relationship` component-order workaround by carrying real structure instead of sniffing it; no spec-compliance regression risk identified.
- [x] Consistent with existing patterns — Phase 1 adopts a pattern (`IExpressionVisitor`) already used correctly elsewhere in Core; it isn't a new pattern for the codebase, just an unused one in this corner of it.

## Evidence

See [current-state-audit](current-state-audit.md) for all cited findings. Reference plan: `docs/superpowers/plans/2026-07-10-fhir-search-semantic-expression-foundation.md` (ms-fhir-server `fhir-server` repo). Core visitor infrastructure confirmed present at `src/Core/Ignixa.Search/Expressions/{IExpressionVisitor.cs,DefaultExpressionVisitor.cs,ExpressionRewriter.cs}`.

## Alternatives considered (not investigated in depth)

- **Targeted dedup only (Phase 0, stop there)**: lowest cost, fixes the most obviously dangerous duplication (finding 2) but leaves the visitor-bypass and composite structure-loss problems in place. Worth doing regardless of what else is decided — it's a prerequisite for Phase 1 anyway.
- **Big-bang rewrite of the SQL search layer**: rejected outright given CLAUDE.md's reversibility guidance and the audit's finding that this is a live, traffic-serving system with thin unit-test coverage on exactly the files that would be rewritten (audit finding 6) — regressions would be expensive to isolate.

## Verdict

Recommend starting with **Phase 0 immediately** (mechanical, no design review needed) and **Phase 1 next** (needs a short design review since it changes the shape of `SearchExpressionQueryBuilder`, but is still behavior-preserving). Treat Phase 2 and Phase 3 as separate follow-up investigations to open once Phase 1 has landed and its real cost is known — don't commit to them yet. Do not start Phase 4 without new evidence it's needed.
