# Chain and Reverse Chain — Design (Phase 6)

**Builds on:** Phases 1-5 of `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md` (complete, merged to `feature/fhir-to-sql-compiler`). The semantic IR (`Ignixa.Search.Expressions`), `Resolve`, and `Lower`/`Emit`'s CTE-graph (`QueryPlan`/`CteDefinition`/`Predicate`) all exist and are feature-complete for leaf types, composites, `:not`, and resource-column predicates.

**Scope of this document:** forward chain (`Patient?organization.name=Acme`) and reverse chain / `_has` (`Patient?_has:Observation:patient:code=1234-5`), including nested chains and multiary (`And`/`Or`) target expressions, and resource-column predicates (`_id`/`_type`/`_lastUpdated`) inside a chain's target expression. `_include`/`_revinclude`/`:iterate` (Phase 7) and SMART/compartment scope enforcement (Phase 8) are explicitly out of scope — see the appendices for how this design accounts for them without implementing them.

---

## 1. Real binder semantics (ground truth, not assumed)

`ChainedExpression` (`src/Core/Ignixa.Search/Expressions/ChainedExpression.cs`):

```csharp
ChainedExpression(
    string[] ResourceTypes,                    // forward: the queried/source type(s). reverse: the referencing type(s)
    SearchParameterInfo ReferenceSearchParameter,
    string[] TargetResourceTypes,               // forward: the resolved target type(s) -- see below. reverse: the referenced type(s)
    bool Reversed,
    Expression Expression)                      // the target expression -- bound against a DIFFERENT resource type than the outer query
```

Traced both binder paths (`SearchKeyBinder.BindForward`/`BindReverse`, `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs`) and the legacy parser (`LegacyExpressionParser.cs`) to establish the real cardinality invariants `Lower` must honor:

- **Forward chains always resolve to exactly one target type before reaching `Lower`.** `BindForward` either finds a single unambiguous candidate, or (when multiple candidates each independently bind) throws `ChainedParameterSpecifyType` demanding an explicit `:Type` modifier (`SearchKeyBinder.cs:152-162`). There is no binder path that produces a forward `ChainedExpression` with a multi-element `TargetResourceTypes`. **`chainedExpression.Expression` is bound against this one target type** (`BindSingleForwardCandidate`, line 165-175).
- **Reverse chains can legitimately produce a multi-element `TargetResourceTypes`.** `BindReverse` intersects the reference parameter's declared targets with the current search context's resource types (`SearchKeyBinder.cs:187-203`) — this can be plural when the search context itself spans multiple resource types. `chainedExpression.Expression` is bound against the single **referencing** type (`syntax.SourceResourceType`), never against the plural target side.
- **The invariant that matters for `ChainJoin`'s shape:** the side `chainedExpression.Expression` is bound against (target for forward, referencing for reverse) is always exactly one type. The *other* side (source for forward, target for reverse) may legitimately be plural.

## 2. Real reference-join semantics (ground truth from the live EF processor)

`dbo.ReferenceSearchParam` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql:518-526`):

```sql
CREATE TABLE dbo.ReferenceSearchParam (
    ResourceTypeId           SMALLINT      NOT NULL,  -- the REFERENCING resource's type
    ResourceSurrogateId      BIGINT        NOT NULL,  -- the REFERENCING resource's own surrogate id
    SearchParamId            SMALLINT      NOT NULL,  -- which reference search param (e.g. "organization")
    BaseUri                  VARCHAR(128)  NULL,
    ReferenceResourceTypeId  SMALLINT      NULL,       -- the REFERENCED resource's type
    ReferenceResourceId      VARCHAR(64)   NOT NULL,   -- the REFERENCED resource's NATURAL id (not a surrogate id)
    ReferenceResourceVersion INT           NULL
);
```

`ChainedExpressionProcessor.cs` (296 lines, `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/`) is the live, correct executor for this schema — traced in full. Every `ReferenceSearchParam` row means "the resource `(ResourceTypeId, ResourceSurrogateId)` has a reference of type `SearchParamId` pointing at `(ReferenceResourceTypeId, ReferenceResourceId)`." Since `ReferenceResourceId` is a natural id, translating it into the referenced resource's own surrogate id requires an extra join to `dbo.Resource`.

**Key derivation:** forward and reverse chain are the same join shape, with the `dbo.Resource` translation join applied to the *opposite* side:

- **Forward:** the already-known match set is on the REFERENCED (target) side — it needs the `dbo.Resource` translation to become comparable. The OUTPUT is the REFERENCING (source) side, already a surrogate id (`rsp.ResourceSurrogateId`) — no join needed.
- **Reverse:** the already-known match set is on the REFERENCING side — it correlates directly against `rsp.(ResourceTypeId, ResourceSurrogateId)`, the same `(T1, Sid1)` shape every other CTE in this IR already uses. The OUTPUT is the REFERENCED side — needs the `dbo.Resource` translation join.

This was independently verified against fhir-server's real chain emitter (`SqlQueryGenerator.HandleTableKindChain`, `microsoft/fhir-server` `main`, lines 867-956): the translation join is *always* on the reference-target side (lines 888-891); `Reversed` only flips which physical side is aliased as the plan's own `T1`/`Sid1` (lines 877-887). Same fact, same conclusion, independently confirmed.

## 3. The `ChainJoin` node

```csharp
CteDefinition.ChainJoin(
    CteRef InnerMatch,                          // the already-lowered match set on the "known" side
    short ReferenceSearchParamId,
    short InnerResourceTypeId,                  // the side correlated to InnerMatch -- always exactly one type (§1)
    IReadOnlyList<short> OutputResourceTypeIds, // the projected side -- legitimately plural (§1)
    ChainDirection Direction)                   // Forward | Reverse -- picks which side gets the Resource translation join
```

`InnerResourceTypeId` is a hard requirement, not redundancy: the clustered index `IXC_ReferenceSearchParam` keys `(ResourceTypeId, ResourceSurrogateId, SearchParamId)` (`97.sql:530-532`) and the covering index `IXU_ReferenceResourceId_...` keys `(ReferenceResourceId, ReferenceResourceTypeId, SearchParamId, BaseUri, ...)` (`97.sql:534-536`) — the explicit type-equality filter on the inner side is a second-key-column seek predicate against these indexes, not a redundant check.

### Emitted SQL

**Forward** (`InnerMatch` = the target-side match set):
```sql
cteN = SELECT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1
       FROM dbo.ReferenceSearchParam rsp
       INNER JOIN dbo.Resource r
           ON r.ResourceTypeId = rsp.ReferenceResourceTypeId
          AND r.ResourceId = rsp.ReferenceResourceId
          AND r.IsHistory = 0 AND r.IsDeleted = 0
       INNER JOIN cteInnerMatch m
           ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId
       WHERE rsp.SearchParamId = @refParamId
         AND (rsp.ResourceTypeId = @p1 OR rsp.ResourceTypeId = @p2 OR ...)   -- see note below
         AND rsp.BaseUri IS NULL
```

**Reverse** (`InnerMatch` = the referencing-side match set):
```sql
cteN = SELECT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1
       FROM dbo.ReferenceSearchParam rsp
       INNER JOIN cteInnerMatch m
           ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId
       INNER JOIN dbo.Resource r
           ON r.ResourceTypeId = rsp.ReferenceResourceTypeId
          AND r.ResourceId = rsp.ReferenceResourceId
          AND r.IsHistory = 0 AND r.IsDeleted = 0
       WHERE rsp.SearchParamId = @refParamId
         AND (rsp.ReferenceResourceTypeId = @p1 OR rsp.ReferenceResourceTypeId = @p2 OR ...)   -- see note below
         AND rsp.BaseUri IS NULL
```

**`OutputResourceTypeIds` rendering — no new `Predicate` type needed.** This IR has no IN-list predicate today (`Predicate.cs` has `Equal`/`Like`/`And`/`Or`/the four comparisons, nothing list-shaped). Rather than add one, `ChainJoin`'s output-type filter renders as an `Or`-chain of `Equal` predicates over `OutputResourceTypeIds` — reusing existing, already-tested `Predicate`/`Emit` machinery with zero new predicate shapes. `N=1` (the common case for both directions per §1) degenerates to a single bare `Equal`, no `Or` involved. `InnerResourceTypeId` filters `InnerMatch`'s own side as a single `Equal` (or folded into the `m.T1 =` join condition for reverse) — always exactly one type, per §1, never an `Or`-chain.

**`BaseUri IS NULL`** — a deliberate addition beyond fhir-server's own chain join, which has no equivalent filter. Neither the live EF processor nor fhir-server's `SqlQueryGenerator` filters `BaseUri`; an external reference (`BaseUri` set, pointing outside this database) whose `ReferenceResourceId` happens to collide with a local resource's natural id would silently mis-join in both implementations today. Chains only ever mean to follow *local* references, so this filter is free correctness, not a behavior change for any query that was already working. Documented here as a deliberate, minor improvement over fhir-server, not an oversight.

**History/deleted baked into the translation join** — same precedent as `ResourceSource`; matches the live EF processor's `Resources.Where(r => !r.IsHistory && !r.IsDeleted)` (`ChainedExpressionProcessor.cs:149,232`).

## 4. Scope threading through `Lower`'s recursion

`StructuralContext` currently holds one `_targetResourceType` field for the whole query (`StructuralContext.cs:16`, set once in the constructor). A chain's target expression needs a *different* resource-type scope than the outer query — and nested chains need a different scope at every level.

**Rejected approach:** minting a sibling `StructuralContext` instance per scope, sharing the same underlying `List<CteDefinition>` by reference. This works structurally but sets up the same "context bag" failure mode the original design doc already rejected once (`docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md`, "The tier boundary is a type, not a convention") — two mutable views over one list with per-instance divergent fields is fragile the moment any future feature (e.g. CTE coalescing, already named as a future `Lower` normalization rule in that doc) needs to reason about "the" context.

**Chosen approach:** delete `_targetResourceType` as a field. The resource-type scope becomes an explicit parameter threaded through `LowerNode`'s recursion (and `LowerResourceSource`/`LowerNot`, which currently read the field). One `StructuralContext` instance for the whole plan, always — `CteRef` indices stay trivially globally valid since there's only ever one underlying `Ctes` list, held once.

**Impact on existing code:** this touches `StructuralContext.cs` and `Lower.cs`'s `LowerNode`/`LowerSearchParameter`/`LowerAnd` signatures, all of which were built and tested in prior increments (Tasks 5-9 of the `:not`/resource-column-predicates increment). Every call site gains a scope parameter; behavior at the top level is unchanged (the top-level scope is just the first value threaded in, exactly what `_targetResourceType` held before).

## 5. Resource-column predicates inside a chain's target expression

`Patient?organization._id=X` and `Patient?_has:Observation:patient:_id=X` are both legal, common FHIR that must not silently mis-lower. Today, `Lower.Run`'s `ExtractResourceColumnPredicates` only runs at the true top level, routing hits into `QueryPlan.OuterPredicate` — a mechanism that only makes sense once, at the outermost query (no "outer" WHERE is available for a sub-CTE that itself becomes another node's `InnerMatch`).

**Design:** the same extraction logic runs at every scope entry (top-level query, chain target expression, nested chain target expression, at any depth). What it does with an extracted predicate depends on whether this is the outermost scope:

- **Outermost scope** (the real `Lower.Run` entry): extracted predicate → `QueryPlan.OuterPredicate`, exactly as today. Unchanged.
- **Any nested scope** (a chain's target expression, at any depth): extracted predicate → `CteDefinition.ResourceSource` gains a `Predicate? Predicate = null` field (resurrecting the pre-outer-WHERE sketch from the original design doc, `docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md`, `ResourceSource(short? ResourceTypeId, Predicate? p)`), used as `ResourceSource(scopeTypeId, thatPredicate)`, `Intersect`ed with any ordinary predicates also present in that scope's expression — the same composition every other `And` in this IR already uses.

This is deliberately **direction-agnostic**: the same "lower this expression in this scope" call handles forward's target-side expression and reverse's referencing-side expression identically. Neither needs a per-direction accommodation, and reverse in particular needs none of the extra `dbo.Resource` join machinery fhir-server itself requires for this case (`SqlQueryGenerator.cs:893-904` needs a second Resource join specifically for this) — because in this IR, `InnerMatch` is *already* an ordinary `Intersect`-composed CTE before `ChainJoin` ever sees it, regardless of which side of the reference it represents.

`ResourceSource`'s new `Predicate` field is unused (`null`) at the top level, which keeps the top level's existing outer-WHERE mechanism — a deliberate, user-confirmed choice made for SQL Server TOP+CTE-inlining performance reasons during the `:not`/resource-column-predicates increment — completely unchanged.

## 6. `SymbolCollectingVisitor` — new `VisitChained` override

No override exists today; the base `ExpressionRewriter.VisitChained` (`ExpressionRewriter.cs:28-34`) only recurses into `.Expression`, so `ReferenceSearchParameter`'s `SearchParamId` and both sides' `ResourceTypeId`s never resolve. New override:

```csharp
public override Expression VisitChained(ChainedExpression expression, object? context)
{
    Parameters.Add(expression.ReferenceSearchParameter);
    foreach (var rt in expression.ResourceTypes) ResourceTypes.Add(rt);
    foreach (var rt in expression.TargetResourceTypes) ResourceTypes.Add(rt);
    return base.VisitChained(expression, context);  // recurses into .Expression via ExpressionRewriter, collects the rest
}
```

Closes the visitor's own remarks-block IOU: "Chain/compartment target-type resolution remains Phase 6/8's job" (`SymbolCollectingVisitor.cs`).

## 7. Complexity guard

fhir-server's own history (researched against `microsoft/fhir-server` GitHub, not assumed) surfaces two real, still-open-in-spirit failure classes:

- **#2818**: SQL Server's optimizer gives up ("could not produce a query plan") under a chain combined with many codes, includes, and `:not` CTEs piling up — closed without a structural fix.
- **#2540**: a 7x latency swing purely from query-*string* parameter order, band-aided by a static heuristic CTE reorderer (`SearchParamTableExpressionReorderer`).

This design's `Lower`-time CTE ordering is already deterministic and derived purely from the expression tree's own shape (never from incidental query-string parameter order), which avoids #2540's failure class by construction — worth stating explicitly as a design property, not just an accident.

For #2818's class: add a nested-chain-depth guard of **10 levels** (a `ChainedExpression` whose `.Expression` is itself a `ChainedExpression`, recursively) — a cheap, named `NotSupportedException` thrown at `Lower` time rather than letting SQL Server discover the problem at execution time with an opaque error. 10 is deliberately generous against realistic FHIR chains (2-3 levels covers nearly every real query; fhir-server's own precedent of depth 100 governs its whole rewrite-pass recursion including includes/`:not`/sort, not chain nesting specifically, so it is not a directly comparable number) while still being a real, enforced ceiling rather than an unbounded recursion. This guards nesting depth only — it does not attempt to bound total CTE count from multiary target expressions or `:not` combined with chain, which is a separate, harder-to-bound concern left to future `Lower` normalization work (CTE coalescing, already named as future work in the original design doc) if it turns out to matter in practice.

## 8. Explicitly in scope / explicitly deferred

**In scope for this increment:**
- Forward chain (`organization.name=Acme`)
- Reverse chain / `_has` (`_has:Observation:patient:code=X`)
- Nested chains, up to 10 levels deep (§7's guard) (`organization.partof.name=X`)
- Multiary (`And`/`Or`) target expressions (`organization.name=X&organization.active=true`)
- Resource-column predicates (`_id`/`_type`/`_lastUpdated`) inside a chain's target expression, either direction, any nesting depth
- `BaseUri IS NULL` (documented improvement over fhir-server)

**Explicitly deferred, throws `NotSupportedException`:**
- `_include`/`_revinclude`/`:iterate` — Phase 7. See Appendix A for how this design already accounts for it.
- SMART/compartment scope enforcement for chained targets — Phase 8/SMART phase. See Appendix B for why `ChainJoin` itself needs no changes to support this later, and what Phase 8 actually needs to build.
- Nested chain depth beyond 10 levels (§7's guard) — throws `NotSupportedException` naming the limit, not a SQL Server optimizer failure discovered at execution time.

---

## Appendix A: `_include`/`_revinclude` forward sketch (Phase 7, not implemented now)

Researched fhir-server's real include implementation (`SqlQueryGenerator.HandleTableKindInclude`, lines 958-1245) for forward context. It is **structurally the same join as a chain link** — the same `ReferenceSearchParam ⋈ dbo.Resource` translation (lines 982-985 mirror lines 888-891). The differences are consumption semantics, not join shape: seeded by `EXISTS` against the match CTE (or predecessor include CTEs for `:iterate`) rather than intersected into the match graph; projects an `IsMatch = 0` marker; capped with `TOP(@IncludeCount + 1)` plus an `IsPartial` truncation-signal column (`HandleTableKindIncludeLimit`, lines 1247-1267; default/max 1000, `CoreFeatureConfiguration`).

**Recommendation for Phase 7 (not built here):** reuse the Resource-translation-join logic at the `Emit` level (extract it as a shared private helper used by both `ChainJoin` emission and future include-stage emission), but do **not** reuse `ChainJoin` as an IR node for includes. `docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md` already decided `Includes` sits *outside* `QueryPlan.Ctes` — "includes are not predicates and not CTEs in the match graph, and modelling them as such is what forces include-seeding logic to contaminate it." An `IncludeStage` referencing `CteDefinition.ChainJoin` would drag include stages into the match graph's `CteRef` index space, violating that decision.

Sketch (illustrative, not a commitment):
```csharp
IncludeStage(
    ChainDirection Direction,                  // _include vs _revinclude -- same flip as ChainJoin
    short? ReferenceSearchParamId,             // null = wildcard (*)
    IReadOnlyList<short> SourceTypeIds,
    IReadOnlyList<short>? TargetTypeIds,       // also the future scope-filter seat -- see Appendix B
    bool Iterate,
    int Limit)
```

`:iterate` needs a topological ordering of the stage list (fhir-server's `IncludeRewriter` uses Kahn's algorithm over produces/requires edges, rejecting cycles, since an iterate CTE must textually follow whichever CTE produces its seed type) — in this IR that's a pure, deterministic sort over `Includes`, testable via `Explain()` with no SQL involved. Not designed further here; this is context for Phase 7's own brainstorming pass, not a spec.

## Appendix B: SMART/compartment authorization boundary — analysis and recommendation

**Threat model examined:** does a `ChainJoin` (or, later, an include) risk returning data outside an authorized compartment/SMART scope once the query crosses into a different resource type via a reference?

**Answer: `ChainJoin` cannot leak data across a scope boundary.** Its output is always shaped as the *outer* (queried) resource type's `(T1, Sid1)` — never the target type's rows. The only residual risk is an **inference channel**: a patient-scoped client could filter by properties of an out-of-compartment resource (e.g. `Observation?performer.name=X` under a scope that shouldn't reveal performer identities) without that resource's data ever appearing in the result set. Researched fhir-server's handling of this: it ships the identical inference channel — no GitHub issue or advisory found describing it as addressed — so this is not a chain-specific gap introduced by this design, it is a property of allowing chained search under restricted scopes at all, already accepted by the reference implementation.

**Includes are the real boundary-crossing concern** (Phase 7's problem, not this one's) — they return the referenced resources directly. Researched fhir-server's actual behavior: included resources are scope-filtered by resource *type* only (`ReferenceResourceTypeId IN (allowedTypes)` filters plus a bind-time 400 via `ExpressionAccessControl.CheckAndRaiseAccessExceptions`), never by compartment instance, except in the SMART v2 granular-scope path. A real shipped bug confirms this was the deliberate baseline: wildcard `_include`/`_revinclude` under SMART scopes returned out-of-scope resource *types* until a 2025 fix (Azure Health Data Services release notes) — the fix was type-level, not instance-level.

**Recommendation: `ChainJoin` gets no authorization hook or reserved predicate seat.** A reserved-but-unused extension point encoding a policy with no current owner is exactly the `OverflowConvention` mistake this project's own design doc already rejected once. Enforcement composes upstream with zero `ChainJoin` changes:
1. A bind-time type check (fhir-server's `ExpressionAccessControl` model) that rejects a chain into an unauthorized target type before `Lower` ever runs — the right place to close the inference channel, if/when that's decided to be worth closing.
2. A semantic-tree rewriter that ANDs scope predicates into expressions, including into a chain's *target* expression — which then flows through the ordinary `Lower` machinery (§5's mechanism) as just another predicate, no special-casing needed.
3. For Phase 7: `IncludeStage.TargetTypeIds` (Appendix A) is the one place a reserved seat is actually justified, because includes are not predicates and cannot be handled by tree rewriting the way chains and ordinary queries can.

**Roadmap action (not part of this increment):** name this explicitly as a Phase 8/SMART-phase requirement — "scope enforcement for chained targets (bind-time type check) and for includes (type-level filter minimum, instance-level optional-stricter, matching fhir-server's SMART v2 precedent)" — so it's inherited as a known, named requirement rather than rediscovered as a surprise. Also note: porting fhir-server's `SmartCompartmentSearchExpression` semantics at all (including its deliberately wider "universal resources" list — `Location`, `Organization`, `Practitioner`, `Medication`, `Device`) is the design doc's own already-named, currently-unowned prerequisite (`docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md`, *Risks and prerequisites*) — this analysis doesn't change that, just confirms it's still true and still needed before Phase 8 can meaningfully enforce anything.

**Separate finding, filed independently, not part of this design:** `src/Application/Ignixa.Api/Filters/FhirAuthorizationFilter.cs:90-98` computes a per-patient SMART authorization filter and stashes it in `HttpContext.Items["FhirAuthorizationFilter"]` with a comment saying it's "for query layer (patient compartment filtering)" — nothing in the repo ever reads that key. Patient-scoped SMART tokens currently narrow *nothing* about search results; only resource-type/interaction-level 403s apply. This is a live gap in Ignixa's current, already-shipped authorization enforcement, entirely independent of this compiler project. Should be tracked as its own defect.
