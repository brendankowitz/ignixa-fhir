# Ignixa.Search.Sql unified foundation — design

**Status:** ready for review
**Date:** 2026-07-25
**Worktree:** `.claude/worktrees/search-sql-unified`, branch `worktree-search-sql-unified`
**Base:** `6605ec20` — PR #365's head with `origin/main` merged in

## Why this exists

Two branches independently extended the same FHIR-to-SQL compiler, and both need to ship.

**PR #365** (`ignixa-fhir-server-adoption`) adds capability so the Microsoft FHIR Server can adopt `Ignixa.Search.Sql` via NuGet. Its own scope note is candid that **nothing executes**: no test of any kind runs emitted SQL against a database, and it names "a class of defect this branch structurally cannot detect."

**`worktree-ignixa-datalayer-sqlserver`** migrated Ignixa's own read path onto the compiler. It is the only thing that has ever run this compiler against a real database — 126 integration tests and a 620-test E2E suite.

Left alone, these diverge: two implementations of the same features, and a compiler foundation that neither consumer fully shares. This branch reconciles them into one foundation, so both consumers build on the same compiler rather than each carrying private extensions.

**The evidence base is `.superpowers/sdd/pr365-reconciliation-analysis.md`** (in the DataLayer worktree) — a file-by-file comparison of both implementations. This design records the decisions; that document carries the reasoning and should be read alongside it.

## Naming

Branch A = `worktree-ignixa-datalayer-sqlserver` (Ignixa's data layer + compiler extensions).
Branch B = PR #365 (FHIR Server adoption capability). This branch's base.

## Reconciliation decisions

Six places both branches solved the same problem. Where capability is equivalent, the tiebreak is execution evidence — but design quality overrides it, and one decision goes B's way on exactly that basis.

**1. Multi-type / system-wide search — synthesis.** Keep A's nullable-`ResourceTypeId` threading through every leaf and composite rule: it is the only implementation that can express `GET /?_type=A,B&name=foo`, since B's typed-leaf path still throws when the target type is null. Adopt B's `MultiTypeResourceSource` for the no-expression base set — guarded `AllTypes`/`ForTypes` factories, IN-list narrowing, sentinel handling — whose `AllTypes()` emits byte-identical SQL to A's `ResourceSource(null)`.

**2. Surrogate-id range — B's shape.** The one decision where the unexecuted implementation wins. B's `SurrogateIdRange` plan input emits against `m.Sid1` with no forced `dbo.Resource` join, states the match-arm-only contract explicitly, and is visible in the plan. A's outer-predicate splice is deleted. **Gated on A's `$export` partition integration tests passing against it** — that is the validation B could not perform.

**3. `$everything` — A's traversal, B's wiring.** The largest duplicate, and one neither branch's authors flagged. A's is a strict superset (referenced-type expansion, conditional clinical-date filter, `_since`) and E2E-proven; B's self-declares "semantically incomplete." Keep A's `LowerPatientEverything` and its three CTE kinds, wrapped in B's `ApplyToTypes` constraint wiring and B's empty-compartment `Predicate.False` degrade.

**A behavioural divergence must be recorded, not silently resolved:** A's `_since` filters on `Transactions.VisibleDate`; B's on a `lastUpdated` surrogate floor. **These return different rows.** A's matches the legacy engine, so A's is kept — but the choice is deliberate and belongs in the code comment, because a future reader will otherwise assume the two were equivalent.

**4. `VisitPatientEverything` — A's** (superset).

**5. Compiler entry points — keep both.** A's `CompileFromOptionsAsync` (pre-built `SearchOptions`) and B's `operationExpression` serve different callers; this was never actually one problem.

**6. CountOnly WHERE assembly — B's skeleton, A's clauses.** Refactored twice; B's `NeedsResourceJoin`/clause-list structure with A's clause set.

## The signature seam

B's `Lower.Run` → `LowerOptions` refactor is the unified shape. Its motivating defect is real: a positional `IReadOnlyList<string> ResourceTypes` sat adjacent to a same-typed `IReadOnlyList<AccessConstraint>`, silently swappable — an authorization input must be name-only.

A's additions all fit as init properties: `SystemLevelSearch`, `OffsetPage` (`OffsetSpec?`), `CountPhaseScoped`. A's `(long, long)` surrogate tuple is subsumed by B's typed `SurrogateRange`.

`SystemLevelSearch` stays an explicit opt-in rather than being inferred from `targetResourceType == null` — null already means "wildcard compartment" on main, and B's `ResourceTypes` is orthogonal (it shapes the base set; the flag gates typed-leaf cross-type lowering). `SystemLevelSearch` + `ResourceTypes` together is legal and is exactly the `GET /?_type=A,B&name=foo` case.

**Cross-field guards stay in `Lower.Run`, not the record** — offset×page×top, countPhaseScoped×countOnly×sort, includesOnly×countOnly, includesOnly×sort — because each crosses into parameters that remain positional. `Lower.Run`'s head becomes the single validation choke point.

Fixing this seam first gives every later port a stable target.

## What travels, what stays

**Travels from A:** `OffsetSpec` and OFFSET/FETCH emission (an emit feature — it cannot live in an adapter); `countPhaseScoped`; `SortKeyKind.ResourceId` and `SortKeyKind.Aggregated` with `SentinelFor` and catalog-driven sort keys (removing the String/Date/`_lastUpdated`-only restriction the FHIR Server also needs lifted); the `EmitOrderBy` Msg-145 dedup fix (execution-discovered — B structurally could not have found it); A's `$everything` machinery; `KeysetContinuationToken`; `SearchTrace.CompiledPlan`, `EmittedSqlTrace.Parameters`, nullable `SearchTrace.ResourceType`; `CompileFromOptionsAsync`; nullable-type system-level lowering.

**Travels from B:** `AccessConstraint` + `AccessConstraintApplier` + include-stage guards; `ResourceVisibility`; `ProjectionSpec`; `SearchParameterHash` gating; `IncludesOnly`; `MultiTypeResourceSource`; and **untyped-reference declared-target narrowing — a correctness fix A does not have**, where `/Patient?organization=X` also matched `Practitioner/X`. A's E2E evidently never exercised the cross-type id-collision case.

**Stays in A:** the two-phase sort executor loop (`SearchStreamWithPhaseHandlingAsync`, `MergeCrossPhaseResults`) — pure orchestration over compile-and-execute; `SqlServerCompiledSearchService` and its chunked hydration, legacy `ContinuationToken` bridging, and `+1-for-hasMore` conventions; `SqlServerSymbolResolver`; `GetExportRangesAsync`; the history executor; the Database project.

**Notably staying in A: the csproj catalog-source switch** from `97.sql` to decomposed DDL. It depends on A's Database project, which exists on neither main nor B. The unified branch keeps the `97.sql` catalog source so it builds against main; every table and column the ported features read already exists there. A re-applies the switch when it rebases.

## Two obligations that are not merges

**Wire `AccessConstraints` through `CompileFromOptionsAsync`.** A's entry point predates the parameter and does not forward it. Post-merge, a caller setting `AccessConstraints` on a `SearchOptions` routed through A's adapter would get **silent non-enforcement** — precisely the fail-open defect B's own review caught ("`SearchOptions.AccessConstraints` was connected to nothing"). Not live today, since Ignixa enforces scopes at the API filter, but it is a trapdoor left open. Needs the wiring and a **non-vacuous** test: B's own access-constraint tests were vacuous until stubbing the guard to `1 = 1` was shown to fail something.

**Thread `ResourceVisibility` through A's three new `$everything` emitters.** They were written in the hardcoded-visibility world: `EmitReferencedTypeExpansion` hardcodes `r.IsHistory = 0 AND r.IsDeleted = 0`; `EmitVisibleSinceFilter` and `EmitTableExistsPredicate` emit no visibility filter at all. `$everything` never runs with relaxed visibility in practice, but leaving one CTE kind outside the visibility contract reinstates the "hardcoded at six emitter sites" defect B just removed.

## Decompose `SqlBuilder.Run` during the merge

B's own comment warns that a sixth optional feature should prompt decomposition. The merged method carries roughly nine: `OuterPredicate`, `Projection`, `Sort`/`Page`, `SurrogateRange`, `SearchParameterHash`, `IncludesOnly`, `OffsetPage`, `CountPhaseScoped`, visibility threading.

This is not a tidiness point. B's `IncludesOnly` + `_sort` bug — `ORDER BY` bound to a nonexistent column, grammatically valid, failing at execution with error 207 — was caused by exactly this shape: three sites having to agree on a column contract. Decompose into per-terminal-shape emitters (CountOnly / no-includes / includes) **as part of the merge**, because the merge is where the feature × shape matrix physically collides. Doing it afterwards means colliding twice.

## Sequencing

1. **Extend `LowerOptions`** with A's inputs; port `CompileFromOptionsAsync` and the trace additions, forwarding `AccessConstraints` with its non-vacuous test.
2. **Port A's capabilities in dependency order:** nullable-type leaf threading and `_type` Or-extraction (routing the null-expression base set through B's `MultiTypeResourceSource`) → sort expansion → `OffsetPage` and `CountPhaseScoped` onto B's clause-list skeleton → `$everything` replacement, visibility-threaded and constraint-wired → surrogate-range swap to B's shape.
3. **Decompose `SqlBuilder.Run`**, then re-baseline all text, explainer, and corpus assertions **once, against the final emitter** — not file-by-file, or the corpus verdict guards thrash.
4. **Execution gate.** From A's worktree, point `Ignixa.DataLayer.SqlServer` at this branch and run the 126 integration tests and the 620-test E2E suite. **Nothing merges to main without this.**
5. **A rebases onto this branch**, dropping its now-duplicated compiler commits and re-applying its adapter deltas.

Each step leaves the branch green.

## The execution gate is the point

This is the step B could never run, and the reason this reconciliation is worth doing rather than merging both branches and fixing the collision later.

**Pass bar:** A's current baseline — 588 of 620 E2E passing, 126 integration passing — with **every delta explained**. Two specific gates:

- **`$export` partition tests gate decision 2.** Adopting B's surrogate-range shape over A's executed one is only defensible if A's tests pass against it.
- **Untyped-reference searches gate B's narrowing fix at row level.** B changed which rows `/Patient?organization=X` returns. Text assertions cannot confirm that is right; A's integration tests can.

Two baselines will legitimately move, and moving is the guard working, not a regression: adopting A's `$everything` shifts B's corpus verdict distribution, and adopting B's reference narrowing changes A's emitted SQL for untyped reference searches.

## Risks

- **`QueryPlan` positional-tail collision.** A appended two slots at positions 9–10; B appended five at 9–13. `bool`-versus-record slots make a compiles-but-means-something-else error real. Fix the order once and convert every construction site — both branches' tests build `QueryPlan` directly — to named arguments for the tail.
- **The `$everything` `_since` divergence** returns different rows. Recorded above; must land as a code comment, not tribal knowledge.
- **`gh pr view`'s `mergeable` field is unreliable** — it reported CONFLICTING and MERGEABLE for the same tree within one session. Gate nothing on it; use `git merge-tree`.
- **Ordinal re-baselining is broad.** B's grammar and explainer tests assert exact `@pN` sequences, and A's features add parameters. Mechanical, but it must happen once against the merged emitter.
- **The gap-closure work designed against branch A is now partly stale.** Several of its 30 targeted E2E failures live in files this branch rewrites, and some may already be fixed here. Re-measure against the unified foundation before planning that work.
- **B's new files carry Microsoft copyright headers** that existing `Ignixa.Search.Sql` files do not. Normalize during reconciliation.
