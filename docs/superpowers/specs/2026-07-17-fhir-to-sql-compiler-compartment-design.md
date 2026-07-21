# Compartment Search — Design (Phase 8, part 1)

**Builds on:** Phases 1-7 of `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md` (complete, merged to `feature/fhir-to-sql-compiler`). The CTE-graph IR (`QueryPlan`/`CteDefinition`/`Predicate`), `Resolve`'s batched-I/O symbol table, `Lower`'s structural tier, and the `ChainJoin`/`IncludeStage` precedent for "a dedicated node for a dedicated SQL shape" all exist.

**Scope of this document:** compiling `CompartmentSearchExpression` (`GET /Patient/123/Observation`, `GET /Patient/123/*`) into the IR. `_sort` and continuation tokens are a separate, later increment (Phase 8, part 2) — not covered here. Instance-level SMART/compartment-boundary *enforcement* is explicitly out of scope, matching the precedent Phase 6 (Appendix B) and Phase 7 (§6) already set: this document names and confirms non-foreclosure of the seat where that enforcement would eventually plug in, without building it.

---

## 1. Ground truth (verified, not assumed)

### 1.1 Compartment search's performance case is closed — this phase is not a performance fix

`docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md` (the roadmap's own "Step 0" proving increment, completed before Phase 1 began) already measured this: production `CompartmentSearchQueryGenerator.cs`, unmodified, does not reproduce the original motivating bug (SQL Server Error 8623, a plan-compilation failure, not a 180-second timeout) at realistic scale. A one-line fix — literalizing `SearchParamId` via `EF.Constant`, the same treatment `ResourceTypeId` already gets two lines below it — closes the small residual gap to hand-written SQL. That finding's own explicit recommendation: ship the one-line fix independently, and do not use compartment search as this compiler's headline motivator. This document does not relitigate that conclusion. Compiling compartment search into the IR is justified here on two grounds instead: IR completeness (every `Expression` kind the real binder produces should be compilable, not just the ones that happened to motivate the project), and it is the load-bearing prerequisite for the SMART/compartment-boundary enforcement work Phase 6 and Phase 7 both explicitly deferred to "Phase 8's business."

### 1.2 The live production shape: group by `SearchParamId`, not by resource type

`CompartmentSearchQueryGenerator.cs:93-206` (the code every real compartment search runs today, confirmed unconditional in `SearchExpressionQueryBuilder.cs:85`) does the following, for a given `(compartmentType, compartmentId, filteredResourceTypes)`:

1. Ask `ICompartmentDefinitionManager.TryGetResourceTypes(compartmentType)` for every resource type the compartment covers at all (e.g., Patient compartment includes Observation, Condition, Encounter, ...), then intersect with `filteredResourceTypes` if the caller supplied a non-wildcard set.
2. For each resource type, ask `TryGetSearchParams(resourceType, compartmentType)` for the search-parameter codes that establish compartment membership for that type — always Reference-type parameters (e.g., Observation's membership parameters are `subject`/`performer`; a resource can be a compartment member via more than one reference).
3. Resolve each `(resourceType, code)` pair's `SearchParameterInfo`, then its `SearchParamId`.
4. **Group by `SearchParamId`** (not by resource type) into `(SearchParamId, HashSet<ResourceTypeId>)` — because one parameter URL (e.g. "subject") is shared verbatim across many resource types, this collapses what would otherwise be ~90 per-type queries down to ~23 per-parameter queries for a full Patient wildcard compartment (Step 0's own measured count).
5. For each group, emit one query: `WHERE SearchParamId = <literal> AND ReferenceResourceId = compartmentId AND ResourceTypeId IN (<inlined list>)`, then `UNION` all groups.

This grouping is not incidental. The original motivating failure (Error 8623, "too many tables/partitions to produce a query plan") occurred on a *pre-grouping* code path (the older, per-branch `SearchParameterQueryGenerator`, confirmed by Step 0's own artifact analysis: `CompartmentSearchProblem.txt` shows ~422 nested table references from 166 ungrouped branches for the same 23 distinct `SearchParamId`s this benchmark resolved). The grouped shape is the only structural fix on record for that failure. **A compilation strategy that re-expands the grouped shape back into one CTE per `(resourceType, code)` pair would be moving toward the only documented production failure in this feature's history**, not away from it — this rules out a "reuse `ParamSource` only, zero new AST" approach, since `ParamSource` is one-resource-type-per-CTE by design (the Phase 6 fix; see §2).

### 1.3 fhir-server independently converges on the same grouped shape

fhir-server has two generations of compartment SQL. The old one is a materialized `dbo.CompartmentAssignment` table with a dedicated `CompartmentQueryGenerator` (`CompartmentTypeId = @p AND ReferenceResourceId = @p`) — vestigial: nothing on fhir-server's current write path populates that table. The live strategy is `SqlCompartmentSearchRewriter`, run before the root SQL rewriter: it groups by parameter URL, attaches a `_type IN (types)` filter per group, and ORs the groups under one `ReferenceResourceType`/`ReferenceResourceId` conjunction — structurally the same per-parameter-group-with-type-list shape as Ignixa's production code, arrived at independently. Two codebases that agree on almost nothing else structurally agree here — the same "two architectures independently discovered it" signal the original design doc used to justify a tier-3 CTE-graph IR in the first place.

One fhir-server trap worth recording, not reproducing: when its rewriter finds *no* membership parameters for a request, it passes the raw compartment expression through unrewritten, which then routes to the vestigial `CompartmentQueryGenerator` and queries the never-populated `CompartmentAssignment` table — a structurally-guaranteed silent empty result through dead code. §2's degenerate-case handling below is written specifically to not reproduce this.

### 1.4 A real, live bug in production code this phase should not reproduce

`CompartmentSearchQueryGenerator.cs:181-185`'s query filters `ReferenceResourceId = compartmentId` but never `ReferenceResourceTypeId` — meaning a natural resource id that collides across resource types (e.g. an `Observation.subject` accidentally pointing at a `Group` with the same natural id as the target `Patient`) could leak into the wrong compartment. fhir-server's live rewriter does filter `ReferenceResourceType` (matching what `ReferenceLoweringRule.cs:28-37` already does for ordinary reference searches in this compiler). This is the same class of documented, deliberate improvement `ChainJoin`'s `BaseUri IS NULL` filter already represents (chain design §3) — not reproduced here, named for Phase 9's differential-test suite.

### 1.5 `Resolve`/`SymbolCollectingVisitor` never touch `CompartmentSearchExpression` today

No `VisitCompartment` override exists in `SymbolCollectingVisitor.cs` — its own `<remarks>` block already names this as an open item ("Compartment target-type resolution remains Phase 8's job"). `LowerNode` has no `CompartmentSearchExpression` arm either — it falls to the generic "Lower does not support X yet" throw. Both gaps close in this phase, following the exact precedent Phase 6 set for `ChainedExpression` (`VisitChained`) and Phase 7 set for `IncludeExpression` (`CollectInclude`).

### 1.6 `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` are already Core-tier — no new cross-tier abstraction needed

Both interfaces live in `src/Core/Ignixa.Search/Definition/`, the same tier `Ignixa.Search.Sql` already references (`Ignixa.Search.Sql.csproj`'s only project reference). Unlike `SearchParamId`/`ResourceTypeId` resolution — which needed the `ISymbolResolver` abstraction specifically because the underlying data (`dbo.SearchParam`/`dbo.ResourceType`) is DataLayer-tier — both compartment-definition APIs are synchronous, in-memory, already-Core-tier lookups. No new resolver interface is needed; `Resolve.RunAsync` can accept both managers directly as parameters.

### 1.7 A dead-code sibling: `CompartmentSearchRewriter.cs` — a worked example, not a precedent to build on

`src/Core/Ignixa.Search/Expressions/CompartmentSearchRewriter.cs` (255 lines) already implements a *different* strategy: rewriting `CompartmentSearchExpression` into a plain `Or`/`And`/`Union`/`In` expression tree, entirely at the semantic-expression-tree layer, before any SQL-specific compiler sees it. Confirmed via full-repo grep: this class is referenced nowhere else — it is orphaned, dead, never wired into any pipeline (the live path bypasses the expression tree entirely via `CompartmentSearchQueryGenerator`). Its *expansion logic* (which resource types, which parameters, URL-keyed grouping into `IN`-list branches) is a correct, useful worked example and matches this document's own grouping strategy. Its *output AST* is not reusable: it targets the old field-level `Expression.StringEquals(FieldName.ReferenceResourceId, ...)`/`Expression.In(FieldName.TokenCode, ...)` shape, not the typed `ISearchValue`-based `SearchParameterPredicateExpression` shape `Lower` actually consumes, and its grouped-type branches rely on a `_type IN (...)` predicate — which this compiler's `StructuralContext.RejectResourceColumnCode` (`StructuralContext.cs:51-64`) deliberately throws on at leaf/composite dispatch. A pre-`Resolve` semantic rewrite following this file's strategy would need to solve that same problem anyway, plus add a whole new rewrite stage — no simpler than compiling `CompartmentSearchExpression` directly.

## 2. The `CompartmentSource` node

```csharp
public sealed record CompartmentSource(
    IReadOnlyList<short> ResourceTypeIds,   // non-empty; the grouped membership types for this one SearchParamId
    short SearchParamId,
    Predicate Predicate)                    // ReferenceResourceTypeId = @p AND ReferenceResourceId = @p
    : CteDefinition;
```

Not `ParamSource` extended to accept a list of resource types — `ParamSource.ResourceTypeId` being a single `short` is a deliberate Phase 6 invariant (the bug that phase fixed: a shared `SearchParamId` returning wrong-type rows), and loosening it back to a list would reopen that exact class of bug for every one of the 13 existing leaf/composite lowering rules that construct a `ParamSource`. `CompartmentSource` is the "dedicated node for a dedicated SQL shape" `ChainJoin`/`IncludeStage` already established as this project's answer when an existing node's own invariant would have to bend to fit a new shape. No `TableDescriptor` field — compartment membership is by definition established only via `dbo.ReferenceSearchParam` (confirmed by every source consulted: production code, the dead rewriter, fhir-server), so `Emit` hardcodes the table name the same way `EmitChainJoin` already hardcodes `dbo.ReferenceSearchParam`/`dbo.Resource` rather than threading a `TableDescriptor` through for a single-purpose node.

`ResourceTypeIds` and `SearchParamId` render as literals (matching `ParamSource`/`ChainJoin`'s established precedent — Step 0's own finding is that `SearchParamId` literalization is the load-bearing fix, so this is not optional). `Predicate` is built through the exact same construction `ReferenceLoweringRule.cs:28-37` already uses for ordinary reference searches (`ReferenceResourceTypeId = @p AND ReferenceResourceId = @p`, both bound parameters) — `ReferenceResourceTypeId` filters to the compartment's own type (e.g. `Patient`'s `ResourceTypeId`), closing §1.4's gap; `ReferenceResourceId` filters to the compartment id.

### Emitted SQL

```sql
cteN = SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
       FROM dbo.ReferenceSearchParam
       WHERE SearchParamId = 55                              -- literal
         AND (ResourceTypeId = 104 OR ResourceTypeId = 106)   -- literal Or-chain; bare Equal when count = 1
         AND ReferenceResourceTypeId = @p0                    -- bound: the compartment's own ResourceTypeId
         AND ReferenceResourceId = @p1                        -- bound: the compartment id
```

The resource-type filter renders as a literal `Or`-chain exactly the way `ChainJoin.OutputResourceTypeIds` already does (chain design §3's note on why a real `Predicate.Equal`/`Or` would wrongly force a bound parameter here — same reasoning, same precedent, reused verbatim). One `CompartmentSource` CTE is built per grouped `SearchParamId`; `StructuralContext.LowerCompartment` builds the group map (mirroring `CompartmentSearchQueryGenerator`'s own grouping loop, §1.2), constructs one `CompartmentSource` per group via a new tier-1 `CompartmentLoweringRule` (~30 lines, sharing `ReferenceLoweringRule`'s predicate-construction pattern), and `Union`s all of them into a single `CteRef` — reusing the existing `Union` node with zero changes to it.

**Degenerate case, matching §1.3's fhir-server trap deliberately NOT reproduced:** if a compartment's resource-type/filter intersection produces zero groups (e.g. `GET /Patient/123/NotInCompartment`, or a `FilteredResourceTypes` set that shares no resource type with the compartment definition), `Lower` throws a named `NotSupportedException` rather than silently querying a table that could never match (there is no `CompartmentAssignment`-equivalent table in this schema for it to silently mis-route to, but the same class of "silent empty result nobody can distinguish from a real empty result" risk applies to any ad hoc empty-`CteDefinition` invention). Phase 9's wiring layer should short-circuit this case before calling `Lower` at all, matching what `CompartmentSearchQueryGenerator.cs:85-89` already does today (`_logger.LogWarning(...); return Enumerable.Empty<long>().AsQueryable();`) — this document records the expectation, Phase 9 owns the actual short-circuit.

## 3. `Resolve`/`SymbolCollectingVisitor` widening

`SymbolCollectingVisitor` gains `VisitCompartment`: adds `expression.CompartmentType` to `ResourceTypes` (needed for `CompartmentSource.Predicate`'s `ReferenceResourceTypeId` filter) and records `(CompartmentType, FilteredResourceTypes)` into a new `Compartments` collection — the visitor's own remarks already flag this as its Phase-8 IOU.

`Resolve.RunAsync` gains two additional parameters, `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` (nullable, defaulted `null` — most callers never search a compartment and shouldn't have to supply either). If `Compartments` is non-empty and either manager is null, `Resolve` throws loudly rather than silently producing a `SymbolTable` `Lower` cannot use. When both are present, `Resolve` expands each collected compartment type the same way `CompartmentSearchQueryGenerator` does (§1.2 steps 1-4): `TryGetResourceTypes` → per type `TryGetSearchParams` → per code `TryGetSearchParameter` (skip non-Reference-type parameters, matching every source's precedent) → group by parameter URL — then feeds every resulting `SearchParameterInfo`/resource-type name into the existing `Parameters`/`ResourceTypes` resolution loops (no new I/O-batching mechanism needed, this reuses `Resolve`'s existing two loops as-is).

`SymbolTable` gains one new structural accessor: `CompartmentMembership(string compartmentType) → IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>` — the full compartment map by *names*, not resolved ids (`Lower` resolves ids through the existing `SearchParamId`/`ResourceTypeId` lookups already on `SymbolTable`, avoiding a duplicate id-resolution path). This stores the *full* map for the compartment type, not filtered to any one request's `FilteredResourceTypes` — `Lower` applies that intersection itself as plain set logic when building `CompartmentSource` nodes, which keeps `SymbolTable`'s stored shape canonical per compartment type (reusable if, hypothetically, two compartment expressions referencing the same type ever appeared in one query) and matches `CollectInclude`'s already-established "resolving a superset is safe" precedent (Phase 7) — over-resolving costs a modest number of extra warm-cache lookups for a filtered request, never a correctness risk.

## 4. Non-wildcard case and compartment + ordinary predicates

`GET /Patient/123/Observation` (one specific resource type, not `/Patient/123/*`) does **not** collapse to a simpler mechanism — it collapses each `CompartmentSource`'s *type list* to one element (Observation's own membership parameters, e.g. `subject`/`performer`, remain distinct `SearchParamId`s, so still a `Union` of N single-type-list `CompartmentSource` CTEs, each degenerating its `Or`-chain to a bare `Equal`). No IR distinction is needed between the wildcard and non-wildcard cases; `FilteredResourceTypes` is purely an input to the same grouping logic.

Compartment search combined with an ordinary query string (`GET /Patient/123/Observation?category=laboratory`) is confirmed already-handled by existing machinery with zero new mechanism: `SearchCompartmentHandler.cs:83-85` ANDs the `CompartmentSearchExpression` together with any ordinary `SearchOptions.Expression` before either reaches the data layer, so `Lower` receives `MultiaryExpression(And, [compartment, category])`; `LowerAnd`'s existing recursion produces `Intersect(compartmentUnion, categoryCte)` exactly the way it already composes any two ordinary predicates today.

**One real gap this document must decide, not silently inherit: the wildcard case has no single target resource type.** `Lower.Run`'s `targetResourceType` parameter is mandatory (a Phase 6 invariant); `SearchCompartmentHandler` nulls `SearchOptions.ResourceType` for a wildcard compartment search. Decision: `Lower.Run`'s `targetResourceType` becomes nullable, valid to omit only when the top-level expression tree actually contains a compartment expansion (`Lower` throws if `targetResourceType` is null and no `CompartmentSearchExpression` is present anywhere in the tree — the existing invariant holds for every non-compartment query, unchanged). A null `targetResourceType` with resource-column predicates (`_id`/`_type`/`_lastUpdated`) still works unchanged, since `OuterPredicate`'s join is already type-agnostic (`(T1, Sid1)` joined to `dbo.Resource` with no type filter of its own). A null `targetResourceType` combined with an *ordinary typed* search parameter (e.g. `GET /Patient/123/*?name=Smith`, which has no single resource type to scope `name` against) throws a named `NotSupportedException` in this phase — a typed leaf rule fundamentally needs a single-type scope to build its `ParamSource`, and FHIR's own spec already restricts cross-type wildcard compartment searches to parameters common across the involved types (a case this phase does not attempt to solve; recorded for whichever future phase adds cross-type common-parameter support, if ever needed).

## 5. SMART/compartment boundary — seats confirmed, nothing built

Matching Phase 6 (Appendix B) and Phase 7 (§6)'s established practice exactly: two seats are named and confirmed fillable, neither is built now.

**Match-graph scoping (the seat this phase's own compilation IS):** a future enforcement layer, given `FhirAuthorizationFilter.PatientFilter` (a SMART-scoped patient id — `FhirAuthorizationFilter.cs:19`), synthesizes `CompartmentSearchExpression("Patient", patientFilter, {requestedType})` and `And`s it into the semantic tree exactly the way `SearchCompartmentHandler` already does by hand for a real `/Patient/{id}/...` URL (§4) — it then flows through `LowerAnd → Intersect` with zero IR changes beyond what this document already builds. No new field, no new node.

**Include scoping (Phase 7's own named IOU):** `IncludeStage.OutputTypeIds`'s future companion, `OutputScopeFilter: CteRef?` (include design §6), explicitly awaits "a compartment-membership CTE... Phase 8's business to construct." `StructuralContext.LowerCompartment`'s returned `CteRef` — the `Union` of `CompartmentSource` nodes for a given compartment type — is precisely that CTE. This phase makes that seat fillable by existing; it adds no reserved field to `CompartmentSource` or `IncludeStage` itself.

The actual wiring — reading `HttpContext.Items["FhirAuthorizationFilter"]` (currently computed, currently unread by anything downstream, per `FhirAuthorizationFilter.cs:90-98`) and constructing the synthetic compartment expression — lives entirely in `Ignixa.Api`/`Ignixa.Application`, outside this Core-tier project's layer boundary, and is explicitly out of scope for this phase and this whole compiler project's Phases 1-8 (it is Phase 9-or-later DataLayer/API wiring work, per this roadmap's own tier boundaries).

## 6. Explicitly in scope / explicitly deferred

**In scope for this increment:**
- `CompartmentSearchExpression` (wildcard and non-wildcard `FilteredResourceTypes`), compiling to a `Union` of `CompartmentSource` CTEs grouped by `SearchParamId`, matching production's own grouping shape
- The `ReferenceResourceTypeId` filter fix (§1.4, a documented, deliberate improvement over production's current `ReferenceResourceId`-only filter)
- `Lower.Run`'s `targetResourceType` becoming nullable for the wildcard-compartment case only, with an explicit throw for the incompatible typed-leaf-with-no-scope case
- `Resolve`/`SymbolCollectingVisitor` widening to resolve compartment membership, reusing `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` directly (no new resolver abstraction)

**Explicitly deferred, named so Phase 9 inherits them as known requirements, not surprises:**
- Instance-level SMART/compartment-boundary enforcement (§5) — the wiring that reads `FhirAuthorizationFilter`/`HttpContext.Items` and actually narrows a query, a live, real, currently-unclosed gap this document names but does not fix
- The empty-compartment-membership short-circuit (§2's degenerate case) — this phase throws loudly at `Lower` time; Phase 9's wiring layer owns short-circuiting before calling `Lower` at all, matching production's current behavior
- Cross-type wildcard compartment search combined with a typed (non-resource-column) search parameter (§4) — throws `NotSupportedException`, not implemented
