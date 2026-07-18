# Include, Reverse Include, and `:iterate` — Design (Phase 7)

**Builds on:** Phases 1-6 of `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md` (complete, merged to `feature/fhir-to-sql-compiler`). The CTE-graph IR (`QueryPlan`/`CteDefinition`/`Predicate`), `Lower`'s scope-threaded recursion, and `ChainJoin` (Phase 6) all exist. Phase 6's design doc (`2026-07-17-fhir-to-sql-compiler-chain-design.md`) sketched a forward-looking Appendix A for this phase and an Appendix B SMART/compartment analysis naming includes (not chain) as the real cross-resource-type boundary concern — both are starting points here, not commitments; this document critiques and completes them.

**Scope of this document:** `_include`, `_revinclude`, wildcard includes, and `:iterate`/`:recurse` (single-hop-per-expression model, matching fhir-server — see §4), plus truncation/limit. `_sort`/continuation interactions and instance-level SMART/compartment filtering are explicitly out of scope — see §7.

---

## 1. Ground truth (verified, not assumed)

### 1.1 The real semantic IR already carries what a dependency graph needs

`IncludeExpression` (`src/Core/Ignixa.Search/Expressions/IncludeExpression.cs`) computes `Requires`/`Produces` from `Reversed`/`TargetResourceType`/`ReferenceSearchParameter.TargetResourceTypes`/`WildCard`/`ReferencedTypes` (lines 125-161) — a direct port of fhir-server's own `IncludeExpression`, and sufficient edge data for a topological sort. Two real gaps found while tracing it:

- **Includes never reach `SymbolCollectingVisitor`.** They live in `SearchOptions.Include`/`RevInclude`, not `options.Expression` — no `VisitInclude` override exists or would ever fire. `Resolve.RunAsync` must be widened to accept the include lists directly and resolve each one's `ReferenceSearchParameter`, `SourceResourceType`, `TargetResourceType`, and `ReferencedTypes` into the `SymbolTable`.
- `SearchKeyBinder.BindInclude` defaults a reverse **non-iterate** include's null `TargetResourceType` to the search context's own type; reverse **iterate** leaves it null so `Requires` falls back to the reference parameter's declared target types — both cases already produce correct `Requires`/`Produces`, just via different paths worth knowing about if a future bug report doesn't match expectations.

### 1.2 The join shape matches chain's — with inverted direction polarity

`IncludeProcessor.cs`/`RevIncludeProcessor.cs` (the live, correct executors — same role `ChainedExpressionProcessor.cs` played for chain) confirm the identical `dbo.ReferenceSearchParam ⋈ dbo.Resource` translation join. But: **forward `_include`'s known set is the referencing side** (the resource already in the result set); **forward `ChainJoin`'s known set is the referenced side**. This means a forward `_include` emits the same join shape `ChainJoin.Reverse` already emits, and `_revinclude` emits `ChainJoin.Forward`'s shape — the opposite of what an initial reading of "same mechanism as chain" would suggest. This is the single easiest place this phase could ship a silently-swapped emission, matching exactly the class of bug the Phase 6 final review specifically hunted for in reverse chain's field mapping. §2 addresses this with a dedicated `IncludeDirection` enum (not `ChainDirection` reused) and a side-by-side golden test.

Wildcard forward include emits no `SearchParamId` filter at all (`IncludeProcessor.cs:141-151`) — only the `dbo.Resource` join itself constrains it. Ignixa also supports a revinclude wildcard-source form (`*:*` — no source-type filter *and* no reference-param filter, `RevIncludeProcessor.cs:121-125`), which any design must be able to express (both `SeedTypeIds` and `ReferenceSearchParamId` nullable).

### 1.3 `:iterate` — the live executor and fhir-server disagree, and this design must pick one

`IterateProcessor.cs`'s live behavior is a **runtime fixpoint**: newly discovered resources are re-fed through both processors until nothing new appears or a depth-10 cap is hit, deduping by `(ResourceType, ResourceId)`. This is genuinely stronger than what a one-shot SQL compile can express.

fhir-server compiles each `:iterate`-tagged include *expression* to exactly **one hop**, with multi-level behavior coming from multiple `_include:iterate=...` query parameters chained via topological order — true recursive iteration is an open, unimplemented fhir-server issue (#1310). Its `IncludeRewriter` (169 lines, confirmed Kahn's algorithm, self-documented as such) partitions non-iterate includes (kept in original query order) from the iterate subset (topologically sorted by `Requires`/`Produces` edges, self-edges excluded, cross-expression cycles rejected via `SearchOperationNotSupportedException` since PR #1391).

**Decision (user-confirmed): adopt fhir-server's single-hop-per-expression model.** This is what a deterministic, one-shot `QueryPlan` compile can actually express, and matches the reference implementation's shipped behavior. It is a deliberate, documented divergence from Ignixa's current live executor — recorded here explicitly so Phase 9's differential-test suite catches and confirms it rather than silently inheriting a behavior change.

### 1.4 No bind-time ordering exists — this is `Lower`'s job

`SearchOptionsBuilder.ParseIncludeParameters` parses each `_include`/`_revinclude` independently and appends in query-string order; no ordering, dedup, or cycle rejection happens at bind time, in Ignixa or in fhir-server (whose `IncludeRewriter` is a SQL-layer pass, not a binder pass). Topological ordering belongs in `Lower`.

### 1.5 No SQL-level truncation mechanism exists today

There is no `TOP(@N+1)`/`IsPartial` analogue anywhere in Ignixa's current include path. Today: `SearchOptionsBuilder` parses `_includesCount` (capped at 1000) but **defaults to unbounded** if absent; `StreamingBundleSerializer` truncates at the *serializer*, not SQL; `IncludesResourceHandler`'s `$includes` operation re-executes the whole search with an offset-based continuation token (not surrogate-id-range-pinned like fhir-server's). None of this is a correct local precedent to preserve at the SQL layer — §5 adopts fhir-server's SQL-level shape instead, as a deliberate improvement.

## 2. The `IncludeStage` node

```csharp
IncludeStage(
    IncludeDirection Direction,               // Forward | Reverse -- a DISTINCT enum from ChainDirection (see §1.2 rationale)
    short? ReferenceSearchParamId,            // null = wildcard, no SearchParamId filter emitted
    IReadOnlyList<short>? SeedTypeIds,        // type filter on the seeded (known) side; null = unconstrained (revinclude *:*)
    IReadOnlyList<short>? OutputTypeIds,      // type filter on the produced side; null = wildcard -- the SMART scope seat (§6)
    IReadOnlyList<int> SeedStages,            // indices into QueryPlan.Includes this stage seeds from -- may only reference lower indices
    bool SeedFromMatch,                       // true if this stage ALSO seeds from cteMatchPage directly
    bool Iterate,
    int Limit)
```

`QueryPlan` gains `IReadOnlyList<IncludeStage> Includes = []`.

**`SeedStages` is the piece Appendix A's original sketch omitted entirely**, and it's the load-bearing one. Without it, "which CTE does an iterate hop's `EXISTS` seed from" has nowhere to live except emitter-mutable state — exactly fhir-server's own `_includeLimitCtesByResourceType`, a type-keyed dictionary built up *during* emission (`SqlQueryGenerator.cs`'s `AddIncludeLimitCte`). That mutable-state-during-rendering pattern is precisely what this whole project exists to avoid (see the original design doc's tier-boundary argument). Reifying the dependency as data — plain integer indices into `QueryPlan.Includes`, exactly analogous to how `CteRef` indices work for `QueryPlan.Ctes` — means `Lower` computes every edge once, during the Kahn sort, and `Emit` becomes a dumb renderer with no registry to maintain.

A non-iterate stage always has `SeedStages = []`, `SeedFromMatch = true`. An iterate stage seeds from every predecessor stage whose `Produces` intersects its own `Requires` (populating `SeedStages`), plus the match page directly (`SeedFromMatch = true`) when its `Requires` intersects the match's own resource type(s) — this reproduces both fhir-server's `_cteMainSelect` fallback for iterate stages with no in-graph predecessor and its "matches + first-level includes" combined seed for the second hop.

### Emitted SQL

**Forward** (`Direction = Forward` — known set is the referencing side, e.g. `_include`):
```sql
inc0 = SELECT DISTINCT TOP (@Limit+1) rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1
       FROM dbo.ReferenceSearchParam rsp
       INNER JOIN dbo.Resource r
           ON r.ResourceTypeId = rsp.ReferenceResourceTypeId
          AND r.ResourceId = rsp.ReferenceResourceId
          AND r.IsHistory = 0 AND r.IsDeleted = 0
       WHERE rsp.SearchParamId = 55                 -- omitted entirely for wildcard (ReferenceSearchParamId is null)
         AND rsp.ResourceTypeId = 103                -- SeedTypeIds filter on rsp, when present
         AND r.ResourceTypeId = 105                  -- OutputTypeIds filter on r, when present
         AND rsp.BaseUri IS NULL
         AND EXISTS (
             SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId
             -- UNION ALL one clause per SeedStages entry, same shape against incNlim
         )
```

**Reverse** (`Direction = Reverse` — known set is the referenced side, e.g. `_revinclude`): the mirror, structurally identical to `ChainJoin.Forward`'s shape — known-side correlates directly on `rsp`, output side translated through `dbo.Resource`.

`BaseUri IS NULL` carried over from `ChainJoin`'s own precedent (§3 of the chain design doc) — neither the live processors nor fhir-server filter it; same class of free, documented correctness improvement.

A dedicated `EmitTests` case asserts both directions' full SQL text side by side, specifically to catch a future accidental swap (the exact bug class §1.2 flagged as the easiest way this phase could regress).

## 3. Match-page CTE (only when `Includes` is non-empty)

Both the live executor and fhir-server agree: include stages seed from the *page* of matches (post-`TOP`), not the unpaged match set — Ignixa's own executor comment states this is a FHIR-spec requirement. Today, `Emit` applies `Top`/`OuterPredicate` in the outer `SELECT`, not a CTE — a deliberate choice from the `:not`/resource-column-predicates increment specifically to avoid SQL Server's TOP+CTE-inlining pushdown risk.

**Decision (user-confirmed): materialize `cteMatchPage` as its own CTE, but only when `plan.Includes` is non-empty.** Plain queries (the overwhelming majority) keep today's outer-WHERE shape completely unchanged — zero risk, zero behavior change, verified by a zero-diff regression requirement on every existing golden string, matching this project's established practice for scope-limited changes (see Task 5 of the prior increment's "zero golden-string changes" acceptance criterion). Only queries that request includes pay the CTE-inlining cost, and they already need N additional joins for the includes themselves, so the marginal risk is small relative to the query's own shape.

```sql
;WITH <existing Ctes...>,
cteMatchPage AS (
    SELECT TOP (@Top) m.T1, m.Sid1
    FROM cte{Match.Index} m
    [INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1
     WHERE <OuterPredicate>]                          -- only emitted if OuterPredicate is set
),
inc0 AS ( ... ),
inc0lim AS (
    SELECT TOP (@Limit) T1, Sid1,
           CASE WHEN COUNT_BIG(*) OVER() > @Limit THEN 1 ELSE 0 END AS IsPartial
    FROM inc0
),
...
SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage
UNION ALL
SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i
WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)
-- one more UNION ALL block per additional stage's ...lim CTE
ORDER BY IsMatch DESC
```

**Result shape changes from `(T1, Sid1)` to `(T1, Sid1, IsMatch, IsPartial)` whenever `Includes` is non-empty.** `EmittedSql`'s XML doc states this explicitly as a contract — callers key off `plan.Includes.Count > 0` to know which shape to expect, not by inspecting column count at runtime.

**Dedup across stages: `UNION ALL` + executor-side key dedup, not SQL-side `DISTINCT`.** With `IsPartial` in the projection, `SELECT DISTINCT` is unreliable (the same logical row could appear with different `IsPartial` flags from different stages). fhir-server uses `SELECT DISTINCT` on the *outer* query without `IsPartial` complications because its column shape differs slightly; Ignixa's live executor already deduplicates by `(ResourceType, ResourceId)` key at the application layer (`processedResourceKeys`), so `UNION ALL` here is consistent with an already-established, working pattern rather than introducing a new one.

## 4. Topological ordering (Kahn's algorithm, in `Lower`)

Confirmed against fhir-server: ordering happens in the SQL-generation layer, never at bind time. `Lower`:

1. Partitions `IncludeExpression`s into non-iterate (kept in original query-parameter order) and the `:iterate` subset.
2. Runs Kahn's algorithm over the iterate subset: an edge `x → y` exists iff `x.Produces ∩ y.Requires ≠ ∅` (resolved to ids via the `SymbolTable`), self-edges excluded (a self-referential iterate is not a cycle for this purpose).
3. A genuine cycle between two or more *distinct* iterate expressions throws a named `NotSupportedException` citing the FHIR spec's own ambiguity here and fhir-server's PR #1391 precedent for rejecting it — not a silent wrong answer, not an infinite compile loop.
4. A single self-referential iterate (e.g. `Observation:has-member:iterate` pointing at Observation) is allowed and compiles to exactly one hop, matching §1.3's decision.
5. Kahn's algorithm needs a **deterministic tie-break** among simultaneously-ready nodes (original list index) — without it, `Explain()` golden strings would be nondeterministic across otherwise-identical inputs, breaking this project's whole golden-string testing discipline.

`Lower.Run` and `Resolve.RunAsync`'s signatures widen to accept the include/revinclude lists directly (today they take only the bound `Expression` for ordinary search).

## 5. Truncation

`Limit` is a **required, non-nullable `int`** field on `IncludeStage` (and, upstream, a required parameter on whatever `Lower.Run` overload accepts the include lists) — `Lower` stays pure with no ambient default, matching the precedent `targetResourceType` already set in Phase 6. This phase does **not** touch `SearchOptionsBuilder.cs` (out of scope: it is existing, already-shipped binder code, and today it defaults `IncludesMaxItemCount` to *unbounded* when `_includesCount` is absent — the opposite of fhir-server's 1000). The recommended-1000 default is explicitly **Phase 9's (DataLayer wiring) responsibility**: when Phase 9 translates a `SearchOptions` into a `Lower.Run` call, it computes `searchOptions.IncludesMaxItemCount ?? 1000` itself before passing `limit` in. This phase's own tests always pass an explicit `limit`, never relying on a default that doesn't exist at this layer. Recorded here now specifically so Phase 9 doesn't rediscover the unbounded-vs-1000 divergence as a surprise.

"Was truncated" for a given stage = any row with `IsMatch = 0 AND IsPartial = 1` in that stage's output — no per-stage attribution beyond that is needed; fhir-server doesn't preserve one either.

## 6. SMART/compartment boundary

**`OutputTypeIds` is the one seat needed, and it must be the *produced* side regardless of direction** — the correction to Appendix B's original "`TargetTypeIds`" framing, which was direction-ambiguous the same way Appendix A's `SourceTypeIds`/`TargetTypeIds` naming was before `ChainJoin` settled on `InnerResourceTypeId`/`OutputResourceTypeIds` role-naming instead. fhir-server's own SMART type filter always lands on the produced side (`ReferenceResourceTypeId IN (allowed)` for forward, `ResourceTypeId IN (allowed)` for reverse) — confirming this is the right, direction-independent seat.

**Type-level enforcement needs no new field**: a future Phase 8 layer intersects a stage's `OutputTypeIds` with the caller's allowed set before `Lower` constructs the stage, plus a bind-time 400 upstream (fhir-server's `ExpressionAccessControl` model, matching Phase 6's chain recommendation). **Do not build this now.**

**Instance-level enforcement (fhir-server has an in-flight PR, #5683, not yet merged, moving toward this) stays a documented future addition, not built now**: one nullable field, `CteRef? OutputScopeFilter`, consumed by `Emit` as one additional `AND EXISTS (...)` clause against a compartment-membership CTE (itself an ordinary `ReferenceSearchParam`-shaped `CteDefinition`, Phase 8's business to construct). The check that matters for this phase: confirm the proposed `IncludeStage` shape is strictly additive-compatible with that future field — nothing here forecloses it, the same non-foreclosing verification Phase 6 did for `ChainJoin`.

A real, shipped fhir-server bug (PR #5668) confirms this boundary is not hypothetical: wildcard `_include`/`_revinclude` under SMART scopes returned out-of-scope resource types until fixed — type-level, matching what this phase's `OutputTypeIds` seat is designed to eventually close.

## 7. Explicitly in scope / explicitly deferred

**In scope for this increment:**
- `_include` and `_revinclude`, both directions, specific reference param or wildcard
- `:iterate`/`:recurse`, single-hop-per-expression model (§1.3), multi-expression topological ordering, cycle rejection
- Truncation via `Limit` + the `IsPartial` mechanism (§5)
- `BaseUri IS NULL` (documented improvement, matching `ChainJoin`'s precedent)

**Explicitly deferred, named so Phase 8/9 inherit them as known requirements, not surprises:**
- `_sort`/continuation-token interaction with includes — fhir-server's densest bug cluster for this feature area (PRs #5242, #5297: multiple includes + `_sort` causing dropped includes, 500s, or infinite pagination loops; issues #2950, #2382: `_revinclude` + large `_count`, self-typed iterate ordering edge cases). This phase's `IncludeStage`/`Lower` ordering logic does not attempt to address any of these — Phase 8 (which owns sort) must treat this interaction as a first-class design concern, not an afterthought, given how much of fhir-server's own bug history lives exactly here.
- Instance-level SMART/compartment filtering (§6) — Phase 8/SMART-phase, per the non-foreclosing `OutputScopeFilter` seat above.
- True multi-level `:iterate` recursion beyond what topological ordering of separately-specified `:iterate` parameters expresses — matches fhir-server's own current limitation (issue #1310, still open there too).
