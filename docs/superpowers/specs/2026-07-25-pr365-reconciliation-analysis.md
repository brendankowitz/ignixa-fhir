# PR #365 Reconciliation Analysis

Reconciling two independently-developed change sets to `src/Core/Ignixa.Search.Sql/`:

- **Branch A** — `worktree-ignixa-datalayer-sqlserver`, HEAD `cee3e2a5`, based on current `origin/main` (`c054f8d9`). Compiler changes: ~813 insertions across 29 files, plus a full SqlServer data layer (`src/DataLayer/Ignixa.DataLayer.SqlServer/`), a decomposed-DDL database project, 126 integration tests, and a 620-test E2E suite (588 passing). **Execution evidence: real database.**
- **Branch B** — PR #365, `ignixa-fhir-server-adoption`, head `4d5d4fb5`, based on `3398b06a` (one commit behind current main). Compiler changes: ~1169 insertions across 24 files plus `AccessConstraint`/`SearchOptions` in `Ignixa.Search.Models`. **Execution evidence: none — 672 tests assert on SQL text only (its own scope note).**

Verified facts used throughout (not assumptions):

- `git merge-tree --write-tree origin/main pr365` → **CONFLICTS in 4 files**: `Lowering/CompartmentLoweringRule.cs`, `Lowering/LeafContext.cs`, `test/.../Corpus/CorpusCompiler.cs`, `test/.../Lowering/ReferenceLoweringRuleTests.cs`. `gh pr view 365` currently reports `mergeable: MERGEABLE` — that field is stale/unreliable (main's `c054f8d9` landed 2026-07-24); trust merge-tree.
- Main's `c054f8d9` ("Fix defects found reviewing #353") already added `LeafContext.UnmatchableResourceType`, the `ReferenceKind` switch in `ReferenceColumnEquality.BuildBaseUriPredicate`, and a `CompartmentLoweringRule` unmatchable-type guard. B's edits to those same regions are what conflict.
- `PatientEverythingExpression`, `Predicate.False`, `SortPhase`, `SearchOptions.Start/EndSurrogateId` all exist on `origin/main`; neither branch invented them.
- A did **not** modify `ResourceColumnLoweringRule` (the prompt's list was wrong on that one file); only B did (`ToInclusiveLowerSurrogateId`).

---

## Part 1 — Duplicated features, decision per duplicate

### 1.1 Multi-type / system-wide search — **synthesis; A's leaf mechanism is mandatory, B's base-set node is better**

**A's implementation** (`Lower.cs`, `StructuralContext.cs`, all leaf/composite rules, `CteDefinition.cs`, `SqlBuilder.cs`):
- `systemLevelSearch: bool` parameter on `Lower.Run`; `targetResourceType` may be null.
- `ParamSource.ResourceTypeId` and `ResourceSource.ResourceTypeId` widened to `short?`; a null type emits **no** `ResourceTypeId` filter, so every leaf/composite rule works cross-type. `EmitResourceSource` conditionally binds the type parameter; `PlanExplainer` ordinal accounting fixed to match.
- `_type=A,B` handled *inside* the compiler: `TryExtractOrOfResourceColumnEquals` lifts a comma-list `_type` into an `OuterPredicate` Or of `ResourceTypeId` equalities (via `ResourceColumnLoweringRule`, which already lowers `_type`).
- Explicit choke-point guards for what system-level does **not** support: chain, `:not`/`:missing=true` (`LowerNot`), `_not-referenced`, `:text`, includes, sort.

**B's implementation** (`CteDefinition.MultiTypeResourceSource`, `LowerOptions.ResourceTypes`, `LowerBaseSet`, `EmitMultiTypeResourceSource`, `LeafContext.ResourceTypeIdOrSentinel`, `SymbolTable.TryGetResourceTypeId`):
- A new CTE node with guarded factories: `AllTypes()` (explicit whole-database scan) vs `ForTypes(nonEmptyList)` (throws on empty — an all-unknown list collapsing to empty would silently *widen* to a full scan). Unresolvable types kept as sentinel `-1` (`IN (-1)` matches nothing). Types emitted as an `IN` literal list in the base CTE.
- **Only reachable when the remaining expression is null** (bare `GET /`, `GET /?_type=A,B` with no other parameters, resource-column-only). The `_ when targetResourceType is null => throw` arm for typed leaves is **unchanged** in B: B cannot express `GET /?_type=A,B&name=foo` or any cross-type search-parameter predicate at all.

**Decision.** Neither subsumes the other; they solved different halves of the same feature.

- **Keep A's nullable-`ResourceTypeId` threading wholesale.** It is the only implementation of cross-type leaf/composite predicates, it has execution evidence, and B has no counterpart. Also keep A's choke-point guards and its `_type`-Or extraction (executed; it is how Ignixa's `Build` output reaches the compiler — the `_type` filter arrives as an expression, not as a caller-supplied type list).
- **Adopt B's `MultiTypeResourceSource` (+ `ResourceTypeIdOrSentinel`, `TryGetResourceTypeId`) for the no-expression base set**, replacing A's `ResourceSource(null)` in `LowerBaseSet` only. Under default visibility, `MultiTypeResourceSource.AllTypes()` emits byte-identical SQL to A's `ResourceSource(null)` (`FROM dbo.Resource WHERE IsHistory = 0 AND IsDeleted = 0`), so A's E2E behavior for bare system search is preserved; `ForTypes` gives an `IN`-list narrowed base CTE that A's outer-predicate approach only approximates after the fact, and its fail-safe factories (empty list ≠ all types) are genuinely better design. `LowerOptions.ResourceTypes` stays as the API for callers (MS FHIR Server) that resolve `_type` before compiling; A's expression-level extraction stays for callers that don't.
- A's `ResourceSource(short?)` nullability can then technically revert to `short`, but **keep it nullable** — reverting churns A's executed plans (chain-target scopes, `$everything` patient-itself arm are unaffected either way) for zero capability gain, and PlanExplainer already handles it.

**What breaks / must be re-validated:** system-level plans' `Explain()` output changes kind (`resourceSource` → `multiTypeResourceSource`) — A's trace fixtures and B's explain tests re-baseline. A's system-level E2E subset must be re-run (expected: identical rows). B's `LowerBaseSet` tests survive as-is.

### 1.2 Surrogate-id range — **B's shape, validated by A's tests**

**A** (`Lower.Run(surrogateIdRange: (long Start, long End)?)`): splices `Resource.ResourceSurrogateId >= @start AND <= @end` into `OuterPredicate` at lower time. Consequences: forces the `INNER JOIN dbo.Resource r` in every shape (the outer predicate join), range is invisible in the plan (it's just more predicate), bounds are parameterized. Executed by the `$export` path (adapter maps `options.Start/EndSurrogateId`).

**B** (`SurrogateIdRange(SqlParameterRef Start, End)` on `QueryPlan`/`LowerOptions`, `AppendSurrogateRangeClauses`): a first-class plan input emitted as `m.Sid1 >= @p AND m.Sid1 <= @p` — no `dbo.Resource` join needed (`Sid1` *is* `ResourceSurrogateId`), explicitly scoped to the match arm only in the includes shape (documented: partition windows must not drop legitimately-included resources outside the window), and applied to the CountOnly shape.

**Decision: adopt B's.** It is better on every axis that matters — no forced join, self-describing plan, correct includes-arm semantics made explicit rather than incidental, and Explain-able. A's version has execution evidence but the semantic equivalence is trivial (`m.Sid1` ≡ `r.ResourceSurrogateId` under the identity join). Because this is the one place we drop an executed implementation for an unexecuted one, **the gate is A's `$export` integration tests passing against the new emission before the old path is deleted.** What A's version does better that must be preserved: nothing in emission; the adapter-side "both bounds or neither" mapping stays in `SqlServerCompiledSearchService.CompileAsync`. What breaks in A: `Lower.Run`'s tuple parameter and the outer-predicate splice are deleted; `CompileFromOptionsAsync` forwards a `SurrogateIdRange` instead.

### 1.3 `$everything` — **A's traversal; B's constraint wiring and empty-compartment handling around it** (the biggest duplicate; the prompt did not name it)

Both branches lowered `PatientEverythingExpression`. They are not equivalent:

| Aspect | A (`StructuralContext.LowerPatientEverything` + 3 new CTE kinds) | B (`EverythingLoweringRule`) |
|---|---|---|
| Patient-itself arm | `LowerResourceSourceWithPredicate("Patient", Or of _id equals)` | `LowerResourceSourceForId` per id (same idea) |
| Compartment arm | existing `LowerCompartment` per patient, unioned | `LowerCompartmentCore` with optional predicate |
| Referenced resources (Practitioner/Organization/Location/Medication) | **Yes** — `ReferencedTypeExpansion` CTE seeded from the *filtered* compartment set | **No** — self-documented gap |
| Clinical date filter (start/end) | **Yes** — `TableExistsPredicate` over DateTimeSearchParam: `(compartment ∩ matching-date) ∪ (compartment − has-any-date)` | **No** — self-documented gap |
| `_since` | `VisibleSinceFilter`: join `dbo.Transactions` on `VisibleDate >= @since`, intersected with the compartment branch only | surrogate-id lower bound (`lastUpdated >= since`) ANDed into `CompartmentSource` |
| Validation | E2E against real DB, matching legacy `PatientEverythingQueryGenerator` sequencing | SQL-text tests + corpus shape comparison |

**Decision: keep A's traversal** — it is a superset, and it is the implementation whose row-level output has been compared against the legacy engine. B's PR description itself lists its `$everything` as "semantically incomplete." Delete B's `EverythingLoweringRule`, `LowerResourceSourceForId`, `LowerPatientCompartment`, `CompartmentLoweringRule.additionalPredicate`, and `ResourceColumnLoweringRule.ToInclusiveLowerSurrogateId` (its only consumer dies).

**What B does better that must be preserved:**
1. **AccessConstraint wiring**: B's `Lower.Run` applies `ApplyToTypes` (not single-type `Apply`) to a `$everything` match — with the explicit rationale that a single-type intersect would drop every compartment member *and* skip member-type constraints (an authorization bypass). A's `LowerPatientEverything` output must be routed through exactly that arm in the unified `Lower.Run`.
2. **Empty-compartment graceful degrade** (`a17004d4`): B replaces main's `groups.Count == 0` throw with `LowerResourceSourceWithPredicate(compartmentType, Predicate.False(reason))` — an empty match instead of a user-input-reachable 500, consistent with the resolver's "not found is data" convention. Adopt it; A retains main's throw today and its callers short-circuit upstream, so A is unaffected and strictly safer with it.
3. B's decision doc on `_since` asymmetry (patient row exempt from `_since`/`_type`) matches A's design; no action.

**Semantic divergence to record in the design doc:** A's `_since` = transaction `VisibleDate`; B's `_since` = `meta.lastUpdated` via surrogate-id floor. These return different rows around ingestion-visibility boundaries. A's matches the legacy engine (E2E-proven); keep A's and document why B's simpler bound was rejected. If the MS FHIR Server schema lacks Ignixa's `Transactions.VisibleDate` usage pattern, this is a known portability seam — the `VisibleSinceFilter` emitter hardcodes `dbo.Transactions`, which exists in both schemas (Ignixa's schema is derived from fhir-server's).

**What breaks in B:** its `$everything` corpus verdicts (`DivergenceBaseline`) re-baseline — the divergence B recorded ("compiler's inbound traversal differs from legacy's outbound expansion") partially *closes* because A implements the outbound referenced-type expansion B lacked. Its `EverythingLoweringRuleTests` are replaced by A's equivalents.

### 1.4 `SymbolCollectingVisitor.VisitPatientEverything` — **A's** (superset)

Both added the override. Identical for Patient + compartment registration; A additionally collects the four referenced resource types when `IncludeReferencedResources` is set (required by its expansion). Take A's; B's is a strict subset.

### 1.5 Compiler entry points — **keep both; not actually the same problem**

- A: `SearchCompiler.CompileFromOptionsAsync(SearchOptions, …)` — skips the Build stage for callers holding a pre-built `SearchOptions` (any production `ISearchService`), normalizes empty→null resource type once, exposes `CompiledPlan` on the trace.
- B: `operationExpression` parameter on `CompileAsync`/`CompileWithTimeProviderAsync` — swaps in an operation expression (e.g. `$everything`) after Build, for the query-string-driven path and the corpus harness.

They serve different callers and coexist. One hard requirement: **`CompileFromOptionsAsync` must forward `options.AccessConstraints` into `LowerOptions`** (see Part 4.1).

### 1.6 Same refactor done twice: CountOnly WHERE assembly in `SqlBuilder`

Both branches independently converted the CountOnly shape's single-`OuterPredicate` emission into a `countWhereClauses` list (A: to add the `countPhaseScoped` sort-join + MissingPrimary filter; B: to add hash/surrogate clauses and `NeedsResourceJoin`). Merge on **B's skeleton** (`NeedsResourceJoin`, `WriteAndJoinedClauses`, clause lists — it centralizes the "which features force the `dbo.Resource` join" decision, which A's version leaves implicit) and add A's two contributions as clauses/joins within it.

### 1.7 Minor overlaps (compose, don't choose)

- `ReferenceLoweringRule` / `ReferenceTokenLoweringRule`: A widened `resourceTypeId` to `short?`; B threaded `parameter` into `ReferenceColumnEquality.Build` for declared-target narrowing. Orthogonal; merged signature takes both.
- `PlanExplainer`: A fixed null-`ResourceSource` ordinal accounting and added three new node prints; B added `MultiTypeResourceSource` print. All four land.
- Both reworded the "no includes under null target type" guard; trivial.

---

## Part 2 — The signature refactor (`Lower.Run` → `LowerOptions`)

B's refactor is correct and should be the unified shape — its motivating defect (positional `IReadOnlyList<string> ResourceTypes` adjacent to `IReadOnlyList<AccessConstraint>`, silently swappable) is real, and an authorization input must be name-only. A's additions all fit `LowerOptions` cleanly; none argues for a different shape:

| A's parameter | Lands as | Notes |
|---|---|---|
| `countOnly` | `LowerOptions.CountOnly` | already in B |
| `top` | `LowerOptions.Top` | already in B |
| `approximationReferenceTime` | `LowerOptions.ApproximationReferenceTime` | already in B |
| `systemLevelSearch` | **`LowerOptions.SystemLevelSearch`** (new) | keep as an explicit opt-in rather than inferring from `targetResourceType == null` — null-type already means "wildcard compartment" on main, and B's ResourceTypes list is orthogonal (it shapes the *base set*; the flag gates *typed-leaf* cross-type lowering). Interaction: `SystemLevelSearch` + `ResourceTypes` is legal (multi-type search with leaf params — the `GET /?_type=A,B&name=foo` case; leaves stay type-unfiltered, base/outer narrowing does the typing). |
| `offsetPage` | **`LowerOptions.OffsetPage`** (`OffsetSpec?`, new) | its guard ("offset cannot combine with keyset `page` or `top`", T-SQL error 10741) crosses record/positional boundaries (`page` stays positional) — keep the guard in `Lower.Run`, not in the record. |
| `countPhaseScoped` | **`LowerOptions.CountPhaseScoped`** (new) | guard (`requires CountOnly && sort.Count > 0`) likewise crosses into positional `sort`; keep in `Run`. |
| `surrogateIdRange` `(long,long)?` | **subsumed by B's `LowerOptions.SurrogateRange` (`SurrogateIdRange`)** | per decision 1.2; A's tuple is deleted, callers construct the typed record. |

Resulting `LowerOptions`: `CountOnly, Top, ApproximationReferenceTime, Visibility, SurrogateRange, SearchParameterHash, ResourceTypes, AccessConstraints, IncludesOnly, SystemLevelSearch, OffsetPage, CountPhaseScoped` — 12 optionals, all init-only, all name-forced. That is a lot, but they are genuinely orthogonal plan inputs; the record is the right container. The cross-field guards concentrated at the top of `Lower.Run` become the single validation choke point (offset×page×top, countPhaseScoped×countOnly×sort, includesOnly×countOnly, includesOnly×sort).

One licensing/style note: B's new files carry Microsoft copyright headers (`LowerOptions.cs`, `AccessConstraintApplier.cs`, `AccessConstraint.cs`); existing `Ignixa.Search.Sql` files carry none. Normalize during reconciliation.

---

## Part 3 — Inventory: what travels to the unified compiler vs stays in A

**Travels (compiler foundation; plausibly serves MS FHIR Server too):**

| Item | Verdict | Reasoning |
|---|---|---|
| `OffsetSpec` + OFFSET/FETCH emission (both shapes) | **Travel** | It is an *emit* feature — it cannot live in an adapter. Motivated by Ignixa's legacy offset ContinuationToken, but offset paging is a generic capability (and the MissingPrimary phase-fill algorithm needs it regardless of token format). Mutually-exclusive-with-keyset guard travels with it. |
| `countPhaseScoped` | **Travel** | Compiler-side half of the two-phase sort executor: "count this sort phase's own join output." Any adopter implementing FHIR `_sort` with missing-value semantics over this compiler needs it; it is meaningless to express outside Emit. |
| Two-phase sort executor loop (`SearchStreamWithPhaseHandlingAsync`, `MergeCrossPhaseResults`) | **Stays in A** | Pure orchestration over compile+execute; the MS FHIR Server would write its own (or a shared executor library later — not this branch's problem). |
| `SortKeyKind.ResourceId` (`_sort=_id`) | **Travel** | Real FHIR capability; joins `dbo.Resource` directly. Includes the `BuildSortSpec` MissingPrimary rejection for it. |
| `SortKeyKind.Aggregated` (Token/Number/Quantity/Reference/Uri sort) + `SentinelFor` + catalog-driven `SortKey.Table/Column` | **Travel** | Removes the "String/Date/_lastUpdated only" sort restriction — squarely a compiler-completeness item the MS FHIR Server needs (it supports these sorts today via its own codegen). |
| `EmitOrderBy` Msg-145 dedup (LastUpdated key duplicating the `m.Sid1` tiebreak) | **Travel** | Execution-discovered bug fix; B structurally cannot have found it and does not have it. |
| A's `$everything` machinery (`TableExistsPredicate`, `VisibleSinceFilter`, `ReferencedTypeExpansion` + emitters + explainer rows) | **Travel** | Per decision 1.3. Thread B's `ResourceVisibility` through A's emitters when porting (they hardcode `IsHistory = 0 AND IsDeleted = 0` — see 4.2). |
| `KeysetContinuationToken` | **Travel** | Encodes the compiler's own `PageSpec` boundary shape; belongs beside it. Its doc already disclaims compatibility with Ignixa's legacy token — that's the right layering. |
| `SearchTrace.CompiledPlan`, `EmittedSqlTrace.Parameters`, `SearchTrace.ResourceType: string?` | **Travel** | Any executing caller needs the real plan (to pick row shape — `QueryPlanTrace` is display-only and can diverge from `options.Include` when a degenerate stage is dropped) and the bound parameters. |
| `CompileFromOptionsAsync` | **Travel** | Entry point for any pre-built-`SearchOptions` adopter; the MS FHIR Server's `ISearchService` equivalent is exactly this shape. Must gain `AccessConstraints` forwarding (4.1). |
| Nullable-type system-level lowering + `_type` Or-extraction + choke-point guards | **Travel** | Per decision 1.1. |
| `InternalsVisibleTo Ignixa.Search.Sql.Tests` | Travel | Trivial. |
| csproj `AdditionalFiles` switch (97.sql → decomposed `Ignixa.DataLayer.SqlServer.Database/Tables/*.sql`) + multi-file `SqlCatalogGenerator` | **Stays in A** (for now) | Hard dependency on A's new Database project, which does not exist on main or B. The unified compiler branch must keep the 97.sql catalog source so it builds against main; the compiler features are catalog-source-agnostic (every table/column they read — TokenSearchParam.Code, NumberSearchParam.LowValue, etc. — exists in 97.sql). A re-applies the source switch when it rebases. The generator's multi-file change itself is harmless and could travel, but is inert without the DDL files, so keep it with A to avoid a half-feature. |

**Stays in A (adapter-specific, correctly placed):** `SqlServerCompiledSearchService` (chunked VALUES-join hydration, legacy `ContinuationToken` bridging, `+1-for-hasMore` conventions, cross-phase merge), `SqlServerSymbolResolver`, `GetExportRangesAsync` (raw MIN/MAX/COUNT — deliberately not compiled), history executor, the entire Database project, registration/endpoints changes.

**Travels from B (A lacks it, foundation-worthy):** `AccessConstraint` + `AccessConstraintApplier` + include-stage `IncludeConstraint` guards; `ResourceVisibility` + `ResourceVersionTypes`; `ProjectionSpec`; `SearchParameterHash` gating; `IncludesOnly`; `MultiTypeResourceSource`; untyped-reference declared-target narrowing (`DeclaredTargetResourceTypeIds` + `SymbolCollectingVisitor` target-type collection) — **a correctness fix A does not have** (`/Patient?organization=X` matching `Practitioner/X`; A's E2E evidently never exercised the cross-type id-collision case); empty-compartment `Predicate.False` degrade; the corpus differential harness upgrades.

---

## Part 4 — Interaction risks

### 4.1 SMART / AccessConstraint — A consumes nothing old; the risk is fail-open in A's entry point

The "SMART-scope expression rewriting" B replaced lives in the **Microsoft FHIR Server**, not in Ignixa. Ignixa enforces SMART scopes at the API layer (`FhirAuthorizationFilter` — RBAC/scope checks, compartment-search interaction classification); nothing in branch A consumes any expression-rewriting mechanism, and `AccessConstraint` appears nowhere in A's tree. So **nothing in A breaks** when B's mechanism lands.

The real risk is the inverse, and it is exactly the defect class B's own review caught ("`SearchOptions.AccessConstraints` was connected to nothing"): A's `CompileFromOptionsAsync` predates `AccessConstraints` and does not forward it. Post-merge, a caller setting `AccessConstraints` on a `SearchOptions` routed through A's adapter would get **silent non-enforcement**. The unified branch must wire `options.AccessConstraints → LowerOptions.AccessConstraints` in `CompileFromOptionsAsync` and add the non-vacuous test (B's pattern: stub the guard to `1=1`, assert failures). Longer term this gives Ignixa a structural mechanism for SMART patient-context narrowing — an opportunity, not an obligation, for this branch.

### 4.2 Visibility (`IsHistory`/`IsDeleted` as plan input) vs A's emitter changes — no semantic collision, one porting obligation

A's `EmitParamSource` change relative to main is the **type filter** (nullable `ResourceTypeId`), not the history clause — the catalog-driven `historyClause` is unchanged in A. B gates that same clause on `visibility.IncludeHistory`. Textual conflict, trivial semantic compose: `!visibility.IncludeHistory && p.Table.Columns.Any(IsHistory)`.

The genuine obligation: A's three new emitters were written in the hardcoded-visibility world. `EmitReferencedTypeExpansion` hardcodes `r.IsHistory = 0 AND r.IsDeleted = 0`; `EmitVisibleSinceFilter` and `EmitTableExistsPredicate` emit no visibility filter at all. When ported, thread `ResourceVisibility` through them like B did for `ChainJoin`/`IncludeStage`/`NotReferencedSource` (use B's `ResourceRowFilter` helper). In practice `$everything` never runs with relaxed visibility, but leaving one CTE kind outside the visibility contract is exactly the "hardcoded at N emitter sites" defect B just removed. A's adapter maps nothing to `Visibility` today (history bypasses the compiler via its own executor) — that stays true; `ResourceVersionTypes.Latest` default → `null` visibility → identical SQL, so A's E2E is unaffected.

### 4.3 `ProjectionSpec` — A genuinely unaffected

Null projection preserves the historical shapes exactly: `(T1, Sid1)` no-includes, `(T1, Sid1, IsMatch, IsPartial)` includes — A's `ReadMatchRow` reads ordinals 0–2 and `plan.Includes` drives the shape choice, which matches B's documented contract ("callers pick the shape from `plan.Includes?.Count` and `plan.Projection?.Columns`"). A passes no projection → no change. Future option (not now): A could adopt a `RawResource`-bearing projection to eliminate `FetchResourcesAsync` round trips, but its 100-row chunking exists to bound memory and command size; do not conflate that with this merge.

### 4.4 Other invalidated assumptions

- **`QueryPlan` positional tail collision.** A appended `OffsetPage, CountPhaseScoped` at positions 9–10; B appended `Visibility, Projection, SurrogateRange, SearchParameterHash, IncludesOnly` at 9–13. Any positional construction compiles-but-means-something-else risk is real for `bool`-vs-record slots. Unified order must be fixed once, and every construction site (both branches' tests construct `QueryPlan` directly) converted to named arguments for the tail.
- **Ordinal-stability assumptions in B's text tests.** A's `EmitResourceSource` binds its type parameter only when non-null, and A's offset/count features add parameters; B's grammar/text/explainer tests assert exact `@pN` sequences. Expected, mechanical re-baselining — but it must be done against the *merged* emitter, not file-by-file, or the corpus verdict guards will thrash.
- **`SqlBuilder.Run` is over its own limit.** B's remark says decompose at a sixth optional feature; the merged method carries ~9 (OuterPredicate, Projection, Sort/Page, SurrogateRange, SearchParameterHash, IncludesOnly, OffsetPage, CountPhaseScoped, visibility threading). B's `IncludesOnly`+`_sort` error-207 bug was caused by exactly this shape. **Decompose into per-terminal-shape helpers (CountOnly / no-includes / includes) as part of the merge**, not after — the merge is where the feature×shape matrix physically collides.
- **`IncludesOnly` guard duplication.** B guards includesOnly×countOnly and includesOnly×sort in *both* `Lower.Run` and `SqlBuilder.Run` (public-surface defensiveness). Keep both, matching the existing MissingPrimary guard convention.
- **Corpus vs E2E baselines.** Adopting A's `$everything` changes B's corpus verdict distribution (its README's guard on the distribution will fire — that is the guard working); adopting B's reference narrowing changes A's emitted SQL for untyped reference searches (A's integration tests around `organization=X` style queries must be re-run and are the *validation* that the fix is right at row level — the thing B structurally could not do).
- **`gh pr view` mergeable field is unreliable** (reported CONFLICTING earlier, MERGEABLE now; merge-tree says 4 conflicts). Do not gate anything on it.

---

## Part 5 — Recommended base and sequencing

**Base: #365's head rebased onto current `origin/main` (`c054f8d9`), not #365 as-is.** Reasons: (1) main's `c054f8d9` contains defect fixes (`UnmatchableResourceType`, `ReferenceKind` switch) in the exact files B also edited — reconciling on a base that lacks them means resolving those semantics twice; (2) branch A is already based on `c054f8d9`, so a shared base makes A's diffs directly comparable during porting; (3) the conflict set is small and known (4 files: `CompartmentLoweringRule.cs`, `LeafContext.cs`, `CorpusCompiler.cs`, `ReferenceLoweringRuleTests.cs` — B's `additionalPredicate`/`DeclaredTargetResourceTypeIds` edits vs main's unmatchable-type guards; they compose, different concerns in the same regions).

**Sequencing (each step leaves the branch green):**

1. **Rebase #365 onto `origin/main`; resolve the 4 conflicts.** Semantic rule: main's guards stay, B's parameters/helpers are added alongside. Run B's 672 tests.
2. **Extend `LowerOptions` with A's inputs** (`SystemLevelSearch`, `OffsetPage`, `CountPhaseScoped`; `SurrogateRange` exists) and port `CompileFromOptionsAsync` + trace additions (`CompiledPlan`, `EmittedSqlTrace.Parameters`, nullable `ResourceType`), **forwarding `AccessConstraints`** with a non-vacuous enforcement test. This fixes the signature seam first so every later port has a stable target.
3. **Port A's capabilities as coherent units, dependency-ordered:**
   a. nullable-`ResourceTypeId` leaf/composite threading + choke-point guards + `_type` Or-extraction; route the null-expression base set through B's `LowerBaseSet`/`MultiTypeResourceSource` (decision 1.1);
   b. sort expansion (`ResourceId`, `Aggregated`, `SentinelFor`, Msg-145 fix);
   c. `OffsetPage` + `CountPhaseScoped` emission, merged onto B's clause-list/`NeedsResourceJoin` skeleton (decision 1.6);
   d. replace B's `EverythingLoweringRule` with A's `LowerPatientEverything` + three CTE kinds, visibility-threaded (4.2), constraint-wired via `ApplyToTypes` (1.3), keeping B's empty-compartment `Predicate.False` degrade;
   e. delete A's `surrogateIdRange` outer-predicate splice in favor of B's `SurrogateRange` (decision 1.2).
4. **Decompose `SqlBuilder.Run` into per-shape emitters during step 3c/3d**, then re-baseline all text/explainer/corpus assertions once, against the final emitter.
5. **Execution gate (the step B could never run):** from A's worktree, point `Ignixa.DataLayer.SqlServer` at the unified compiler branch and run the 126 integration tests + 620-test E2E suite. Pass bar: current baseline (588/620) with every delta explained; `$export` partition tests specifically gate decision 1.2, and untyped-reference searches gate B's narrowing fix at row level. Nothing merges to main without this.
6. **A rebases onto the unified branch:** drops its now-duplicated compiler commits, re-applies the adapter deltas (SurrogateRange construction, AccessConstraints pass-through, catalog `AdditionalFiles` switch to the decomposed DDL), re-runs the full suite.

**Lands in unified branch:** everything in Part 3 marked Travel (from both branches).
**Stays in A:** adapter + executor loop, Database project + DDL catalog source switch, history/export executors, API/Application wiring.

---

## One-line decision summary

1. **Multi-type/system-wide:** synthesis — A's nullable-type leaf lowering (only implementation of cross-type predicates; executed) + B's `MultiTypeResourceSource` base set (better IR, guarded factories).
2. **Surrogate-id range:** B's plan-input shape (no forced join, match-arm-only contract) — gated on A's `$export` integration tests passing against it.
3. **`$everything` (unlisted duplicate):** A's traversal (superset: referenced-type expansion, date filter, `_since`; E2E-proven) wrapped in B's `ApplyToTypes` constraint wiring and B's empty-compartment graceful degrade; document the `_since` `VisibleDate`-vs-`lastUpdated` divergence.
4. **`VisitPatientEverything`:** A's (superset).
5. **Entry points:** keep both (`CompileFromOptionsAsync` from A, `operationExpression` from B) — different callers.
6. **CountOnly WHERE assembly:** B's skeleton, A's clauses.
