# Sub-project 1: Compiler Feature-Parity — Design

**Branch:** `worktree-ignixa-datalayer-sqlserver` (worktree `.claude/worktrees/ignixa-datalayer-sqlserver`). No new branch. Touches `Ignixa.Search.Sql` / `Ignixa.Search.Sql.Generators` only — no `Ignixa.DataLayer.SqlServer` or `Ignixa.DataLayer.SqlEntityFramework` changes here.

## Context

This is sub-project 1 of 3 replacing the original, too-large "Phase E" design (`docs/superpowers/specs/2026-07-22-datalayer-sqlserver-phase-e-search-adapter-design.md` — superseded, do not plan from it). A Fable review of that design found the proposed `SqlServerCompiledSearchService` adapter's execution model unworkable, and separately confirmed real feature gaps that a no-flag hard cutover would turn into live regressions. This sub-project closes those gaps at the compiler level, before any adapter work begins (sub-project 3).

**Not in scope here** (confirmed separately): the `ct`→`cancellationToken` rename, the `SearchCompartmentHandler` nested-`And` bug, the `SqlServerRepositoryFactory` composition-root move, and the `SqlServerFhirRepository.cs` cleanup are sub-project 2. The adapter itself, the corrected `Resolve`→`Lower`→`SqlBuilder` execution model, the differential harness, and the cutover are sub-project 3.

**Unrelated, non-overlapping work in flight**: `origin/brendankowitz-implement-sql-search-gaps` (unfinished, no PR) is a separate track closing predicate/leaf-value gaps (system-qualified token/quantity matching, URI `:above`/`:below`, external reference identity, string overflow) — explicitly scoped as "wiring into production remains out of scope" in its own design doc. Zero file/scope overlap with this sub-project; proceeds independently.

## Scope

1. Token/Number/Quantity/Reference/Uri sort-parameter-type support (`_sort`).
2. The 3-sort-key cap: **stays at 3** (explicit user decision — not raised, not removed, despite legacy having none).
3. A new, purpose-built continuation-token format for keyset paging — no legacy-token compatibility (explicit user decision: a hard cutover means in-flight legacy tokens simply go stale; client restarts from page 1).
4. System-level (cross-resource-type) search.
5. `$everything` (`GET /[type]/[id]/$everything`).

## 1. Generalized sort-type support

**Finding**: legacy's Token/Number/Quantity/Reference/Uri sort (`SqlEntityFrameworkSearchService.cs:776-854`) is one mechanism repeated five times — `LEFT JOIN` to the type's search-index table (filtered by `ResourceSurrogateId` + `SearchParamId`) → project one column → `MIN()`/`MAX()` per ASC/DESC. Only the table+column differ (`TokenSearchParam.Code`, `NumberSearchParam.LowValue`, `QuantitySearchParam.LowValue`, `ReferenceSearchParam.ReferenceResourceId`, `UriSearchParam.Uri`). Quantity needs no System/unit-normalization special-casing — plain `LowValue`, identical shape to Number.

**Design**: add one new `SortKeyKind` member (not five) — call it `SortKeyKind.Aggregated` — carrying a table+column reference resolved via `SqlCatalog` (the same catalog-driven table/column resolution leaf lowering already uses per `SearchParamType`), plus the ASC/DESC-driven `MIN`/`MAX` choice. `String`/`Date`/`_lastUpdated` keep their existing `SortKeyKind` members and `IsMin`/`IsMax`-flagged-row join (a write-path optimization avoiding aggregation entirely) — **do not** force the five new types onto that mechanism. Verify at plan time whether `IsMin`/`IsMax` columns exist on `TokenSearchParam`/`NumberSearchParam`/`QuantitySearchParam`/`ReferenceSearchParam`/`UriSearchParam` (if they don't, which is the expected case given legacy's plain-aggregation approach, `Aggregated` is the only sound mechanism — this is a verification step, not an open design question).

`BuildSortKey` (`Lower.cs:376-384`) gains one new dispatch arm instead of five; `Emit`'s `SortValueExpr` helper (the shared NULL-sentinel wrapper from the original sort increment) extends to cover the `MIN`/`MAX`-aggregated case alongside the existing flagged-row case.

## 2. Sort-key cap

Stays at 3. No code change beyond whatever the new sort-type support naturally requires at the existing cap boundary.

## 3. Continuation-token format

**Finding**: the compiler's `PageSpec` (`SortSpec.cs:49-52`) is a keyset boundary — `IReadOnlyList<SqlParameterRef> Boundary` (one value per active sort key, already sentinel-substituted per `Emit`'s NULL-handling rules) + `BoundaryResourceTypeId` + `BoundarySurrogateId`. This is structurally incompatible with legacy's offset+count token (`ContinuationToken.cs:22-71`) — not a format difference to bridge, a different pagination model entirely (keyset vs. offset).

**Design**: a new type (e.g. `KeysetContinuationToken`) encoding `{BoundaryValues: string[], BoundaryResourceTypeId: int, BoundarySurrogateId: long}` as base64 JSON, matching legacy's *encoding style* (base64-wrapped JSON) but a wholly different payload shape. `Encode`/`TryDecode` static methods mirroring `ContinuationToken`'s existing shape for familiarity. `BoundaryValues` are stored as strings (not typed) since the boundary values' actual CLR types vary per sort key (string/date/numeric/etc.) and round-tripping through a single serialization-friendly representation is simpler than a discriminated payload — the adapter (sub-project 3) is responsible for parsing each value back to the right type per its corresponding `SortKeyKind` when constructing a `PageSpec`. This type lives in `Ignixa.Search.Sql` (not `Ignixa.Search`, where the legacy token lives) since it's coupled to `PageSpec`'s shape, not to `SearchOptions`.

## 4. System-level search

**Finding**: `resourceType` is a required, non-nullable parameter threaded through the entire `Lower` dispatch tree (`Lower.cs:96-98` and every leaf/composite/`And`/chain dispatch site), and `ParamSource.ResourceTypeId` renders as a SQL literal per lowering pass (the established plan-shaping technique this compiler uses throughout). This is not a shallow guard — relaxing it to a nullable/parameterized value would give up that literal-ID optimization across the board. The existing wildcard-compartment mechanism (`Lower.cs:62-89`) is not reusable: it resolves compartment *membership* itself across a bounded, known type set and never calls `RequireResourceType`-guarded dispatch at all, whereas system-level search needs the *same arbitrary expression tree* evaluated with no type constraint.

**Design**: run the existing `LowerNode` dispatch once per candidate resource type (the full catalog, or the `_type`-supplied list when present — matching legacy's `IN (...)` list behavior at `SqlEntityFrameworkSearchService.cs:280-307`), and `Union` the resulting `CteRef`s via the already-existing `context.Union(refs)` primitive (the same mechanism OR-expressions and composite alternatives already use). This preserves per-branch literal `ResourceTypeId` plan-shaping — each branch still compiles with its own literal type ID — while composing at the union level, not the predicate level. New orchestration at the `Run`/top-level-dispatch layer; no new AST node kinds.

**Composition limits, explicit and deliberate** (matching this project's established "throw loudly, document the gap" pattern rather than solving everything in one pass): system-level search does **not** combine with `_include`/`_revinclude` in this sub-project — the same limitation the wildcard-compartment increment already accepted for itself (roadmap: "two new `NotSupportedException` guards... a wildcard compartment search alongside `_include`/`_revinclude`"). Sort composes normally (each unioned branch's rows carry the same sort-key columns, `Emit`'s existing `UNION ALL`-then-`ORDER BY` shape from the includes/sort work already handles this pattern).

## 5. `$everything`

**Finding**: legacy's `PatientEverythingQueryGenerator.cs` composes five things, not just compartment search: (1) the Patient resource itself, `UNION` (2) compartment search (already supported), (3) a **conditional date predicate** — keep a resource if it has a matching clinical date, OR if it has no date search parameter at all (so Patient/RelatedPerson/Device, which carry no date param, always survive) — not an ordinary predicate, (4) an optional `_since` filter via `Transaction.VisibleDate` (an incremental-update mechanism, distinct from ordinary `_lastUpdated` search), and (5) an optional **referenced-type expansion** — a separate query pulling in Practitioner/Organization/Location/Medication resources referenced *from* the compartment set, unioned in last. None of this has existing AST representation. Confirmed zero interaction with sort/paging: `$everything` has neither today, even in the legacy path (`PatientEverythingHandler.cs` explicitly disables both) — this item carries no regression risk from items 1-3 above, in either direction.

**Design**: new `EverythingExpression`/lowering support composing: the existing `CompartmentSource`/`LowerCompartment` mechanism for (1)+(2); a new conditional-predicate AST shape for (3) — `Or(HasDateMatch, Not(HasDateParam))` expressed via the existing `:missing`-style "any row exists for this parameter" mechanism (`LowerParameterPresence`, added in Phase 9) composed with an ordinary date range predicate, so this may require **zero new AST node kinds**, only a new top-level composition rule; a new `Since` field alongside `PageSpec`/`Top`/`CountOnly` on `QueryPlan`, lowering to a literal-free parameterized filter against `Transactions.CreateDate` (mirroring how `SqlServerFhirRepository`'s own transaction-scoped queries already join `dbo.Transactions`); and a new `Union`-composed referenced-type-expansion CTE, structurally similar to a `_revinclude` stage but seeded from the compartment `Union` rather than the match page. Exact AST shape (new `CteDefinition` kind vs. composition of existing ones) is a plan-time design decision informed by prototyping against `EndToEndCompilationTests.cs`'s existing style — this design fixes the *what*, task-level design fixes the *exact IR shape*.

## Testing

Each of the 5 items gets unit tests matching this compiler's established granularity (lowering-rule tests, `Emit` golden-SQL tests, end-to-end `Resolve→Lower→Emit` combined-proof tests) — no execution against a real database in this sub-project (that proof belongs to sub-project 3's differential harness, the compiler's first real live-DB test regardless of feature area). Sort-type tests read legacy's exact SQL shape as the oracle for correctness (same pattern chain/include/compartment increments used against `fhir-server`). System-level search and `$everything` tests exercise the new `Union`-based composition against hand-traced expected CTE counts/shapes, matching `PlanExplainer`/`Explain()`-based assertions established since the fourth increment specifically to catch "something silently vanishes" bugs.

## Process

Same rigor as every prior increment in this compiler's roadmap: design → Fable adversarial review of this doc → writing-plans → Fable review of the resulting plan → subagent-driven-development with per-task review → final whole-branch review.
