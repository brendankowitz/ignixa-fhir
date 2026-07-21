# FHIR-to-SQL Compiler: Phase 9 (Compiler Completeness) Design

**Builds on:** Phases 1-8 of `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md` (complete, merged, pushed to `feature/fhir-to-sql-compiler`). Checkpoint 1.5 (the roadmap's explicit go/no-go gate) has been reviewed and closed — all findings from that review are fixed and merged.

**Sequencing:** this becomes **Phase 9** in the roadmap. The DataLayer-wiring work previously planned as Phase 9 (`Ignixa.DataLayer.SqlServer`, wiring `Ignixa.Search.Sql` into real search execution) is renumbered to **Phase 10**. This keeps each phase's diff independently reviewable, and means Phase 10's differential-test suite starts from a more feature-complete compiler.

**Scope of this document:** two new compiler-level features, chosen from a broader gap-analysis of `Ignixa.Search.Sql`'s current coverage (see §1):

1. `_summary=count` and `_total=accurate` (a count-only compiled query shape).
2. The `:missing` search modifier, across all 7 leaf types and all 6 composite types.

Both are pure `Ignixa.Search.Sql` changes — no schema migration, no `Ignixa.DataLayer.*` changes, no new `ISymbolResolver` surface. Explicitly out of scope, and why, in §5.

---

## 1. Ground truth: gap analysis

A direct code audit of `Ignixa.Search.Sql` (not the roadmap's own prose, which doesn't track these) found:

- **`_summary`/`_total`/`SummaryType`/`TotalType`: zero references anywhere in the compiler.** `QueryPlan`'s only `COUNT` usage is an internal `COUNT_BIG(*) OVER()` for `_include`/`_revinclude` truncation detection (`Emit.cs`) — unrelated to a user-facing total.
- **`:missing`: zero references anywhere in the compiler.** No leaf type, no structural layer, no dispatcher handles it.
- **`NumberLoweringRule`/`QuantityLoweringRule`/`DateTimeLoweringRule`/`ReferenceLoweringRule`: zero `.Modifier` checks.** Only `StringLoweringRule` (`:exact`/`:contains`) and `UriLoweringRule` (rejects `:above`/`:below` with a loud throw) look at `predicate.Modifier` at all. A modifier applied to the other four types is silently ignored rather than honored or rejected — the exact "constraint silently vanishes" failure class this project's retrospectives repeatedly call out as its worst recurring bug pattern (see the roadmap's seventh/eighth/tenth increment write-ups).
- **Confirmed NOT gaps**, checked directly rather than assumed:
  - Comma-separated OR values (`status=active,draft`) are expanded into `Or`-composed expressions upstream, in `Ignixa.Search`'s own parsing layer (`SearchValueSyntaxParser.cs`, `SearchOptionsBuilder.cs:82`) — before the compiler ever sees them. Already handled generically by `Predicate.Or`.
  - `_has` (reverse chain as a filter) is fully implemented — Phase 6 (`2026-07-17-fhir-to-sql-compiler-chain.md`).

**Real fhir-server's actual `_total`/`_summary=count` mechanism** (`Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/SqlQueryGenerator.cs`, verified against the real local checkout, not assumed): `searchOptions.CountOnly` swaps the terminal `SELECT` between `SELECT count_big(DISTINCT Sid1)` (when search-param table expressions exist — reuses the same CTE match graph, no join to the Resource table needed) and `SELECT count_big(*)` (an unconditional type search, counted directly off the Resource table). Simple, and directly portable onto Ignixa's own `QueryPlan`/`Emit` shape.

**Real fhir-server's `_total=estimate` (`TotalType.Estimate`): defined as an enum value in `Microsoft.Health.Fhir.Core`, but consumed nowhere** — zero references anywhere in `Microsoft.Health.Fhir.Core` or `Microsoft.Health.Fhir.SqlServer` outside tests. fhir-server accepts the value at the API surface but does not implement it distinctly from `accurate`. There is no real mechanism to port.

**`ReferenceSearchParam` schema has no identifier-search columns** (`97.sql:518-526`: `ResourceTypeId`, `ResourceSurrogateId`, `SearchParamId`, `BaseUri`, `ReferenceResourceTypeId`, `ReferenceResourceId`, `ReferenceResourceVersion` — nothing for a logical/identifier-based reference), and `ReferenceSearchParameterRowGenerator.cs` has zero identifier-related logic. The `:identifier` reference modifier is not a compiler gap — it is a missing schema+write-path feature, out of place in a compiler-only phase (§5).

## 2. `_summary=count` / `_total=accurate`

**Design:** `QueryPlan` gains one new field: `bool CountOnly = false` (purely additive, default `false`, matching every prior phase's "new optional field, zero call-site edits" precedent). `Emit.Run` checks this flag before choosing its terminal `SELECT` shape:

- `CountOnly == false` (today's behavior, unchanged): the existing row-returning `SELECT {top}T1, Sid1 ...` / `SELECT {top}m.T1, m.Sid1 ... ORDER BY ...` shapes, exactly as built through Phase 8.
- `CountOnly == true`: `SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte{Match} m [INNER JOIN dbo.Resource r ON ... WHERE {OuterPredicate}]` — reusing `plan.Ctes`/`plan.Match`/`plan.OuterPredicate` completely unchanged. `DISTINCT` is required (not optional): a `Union`-rooted match (compartment search, wildcard compartment search) can legitimately produce duplicate `Sid1` values across its branches, and a plain `COUNT_BIG(*)` would over-count them.

**No new `CteDefinition` node.** The match graph that determines *which resources match* is identical whether the caller wants rows or a count — only the terminal statement differs. This is why `CountOnly` composes for free with everything built so far:

- **Sort is irrelevant to a count** — `EmitOrderBy`/`EmitSeekPredicate`/`plan.Sort`/`plan.Page` are simply not consulted when `CountOnly` is true. No interaction bug to hunt; there is no interaction.
- **Includes are irrelevant to a count** — FHIR's `_total` counts only the *match* set, never included resources. `Emit.Run`'s `cteMatchPage`/`UNION ALL` includes branch is not reached at all when `CountOnly` is true; the plain no-includes branch's `CountOnly` handling is the only code path involved, regardless of whether `plan.Includes` is populated.
- **`Top` is irrelevant to a count** — a count query has no `TOP`, so `plan.Top` is ignored when `CountOnly` is true (matches fhir-server: `count_big(...)` never has a `TOP`).

**`Lower.Run`'s signature** gains a `bool countOnly = false` parameter, threaded straight onto `QueryPlan.CountOnly` — no lowering-tier logic needed, since the match graph itself is unaffected. This is a pure pass-through, matching `SortPhase`'s own "caller input, not computed" precedent from Phase 8 part 2.

**`_summary=count` vs. `_total=accurate`**: both compile to the identical `CountOnly` plan. The difference between them is a response-shaping decision (does the Bundle also include `entry`s, or only `total`) that belongs to the eventual Phase 10 executor, not this compiler — `Ignixa.Search.Sql` only needs to answer "how many," not "what does the response body look like."

## 3. `_total=estimate`

Throws `NotSupportedException`, at the same call site/tier as every other deliberately-deferred feature (`:ap`, Quantity System/Code matching, wildcard-compartment+sort), citing that real fhir-server does not implement this distinctly from `accurate` either — a documented, intentional scope boundary, not a silent gap. Exact message: `"_total=estimate is not supported -- real fhir-server does not implement this distinctly from _total=accurate either (TotalType.Estimate exists as an enum value but is never consumed in Microsoft.Health.Fhir.Core or Microsoft.Health.Fhir.SqlServer). Use _total=accurate."`

## 4. `:missing` modifier

**Design:** reuses the existing `:not` machinery verbatim — no new `CteDefinition` node, no new `Predicate` case beyond one small addition (below).

Ground truth for how `:not` already works (`Lower.cs`'s `LowerSearchParameter`, tier-2, the single point through which every leaf *and* composite predicate passes before per-type dispatch):

```csharp
if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
{
    var positiveMatch = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value);
    return context.LowerNot(context.Lower(positiveMatch, resourceType), resourceType);
}
```

`context.LowerNot(innerMatch, resourceType)` builds `CteDefinition.Except(ResourceSource(resourceType), innerMatch)` — "every resource of this type, minus the ones matching the inner predicate."

**`:missing` is structurally the same shape, at the same tier**, added as a new branch in `LowerSearchParameter` before the existing dispatch:

```csharp
if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Missing } missingPredicate)
{
    var presenceMatch = context.LowerParameterPresence(missingPredicate.Parameter, resourceType);
    return IsMissingTrue(missingPredicate.Value)
        ? context.LowerNot(presenceMatch, resourceType)
        : presenceMatch;
}
```

(`IsMissingTrue` reads the boolean carried by `:missing=true`/`:missing=false` — the exact FHIR value-parsing detail lives in `Ignixa.Search`'s existing boolean-value parsing, not new work here.)

**`context.LowerParameterPresence(parameter, resourceType)` is the one new piece of machinery**, a `StructuralContext` method analogous to `context.Lower(predicate, resourceType)` but building a `ParamSource` with **no value predicate** — "any row exists for this `(ResourceTypeId, SearchParamId)`," scoped only by table/type/param-id, exactly the shape `EmitMissingPrimaryFilter` (Phase 8 part 2's sort feature) already independently arrived at for the same underlying question ("does a value exist"), just expressed as a `NOT EXISTS` SQL fragment there instead of a full CTE here (that method builds a WHERE-clause fragment for a different purpose — decorating an existing match set's sort join — while `:missing` needs a full match-graph node, since it changes *which resources match*, not just how they're ordered).

**This requires one small, purely-additive `Predicate` case**: `ParamSource(Table, ResourceTypeId, SearchParamId, Predicate)` currently requires a non-null `Predicate` (no "no value filter" variant exists). Add `Predicate.True : Predicate` — a zero-argument sentinel case. `Emit`'s `EmitParamSource`-equivalent rendering treats it specially: when the predicate is `Predicate.True`, the CTE's `WHERE` clause is `WHERE ResourceTypeId = @rt AND SearchParamId = @sp` (no third `AND` term at all — never emit `AND 1=1` clutter). Every existing `ParamSource`-consuming call site is unaffected; `Predicate.True` is a new option, not a changed contract.

**Which table does `LowerParameterPresence` scope against?** Exactly what `SqlCatalog` already tells every leaf/composite rule for its own type — `LowerParameterPresence` needs the parameter's `SqlCatalog`-resolved table, the same lookup every existing leaf rule already performs. No new `ISymbolResolver`/`SymbolTable` surface.

**Composite coverage, for free:** since `LowerSearchParameter` is the single tier both leaf *and* composite predicates pass through (composite detection, `TryGetCompositeComponents`, happens later in the same method — after this new `:missing` branch, so `:missing` is checked first regardless of whether the underlying parameter turns out to be a leaf or composite type), `LowerParameterPresence` only needs to resolve "what table backs this `SearchParameterInfo`" — which `SqlCatalog` already answers uniformly for both leaf and composite parameter kinds. One mechanism, all 13 search-parameter-table types (7 leaf + 6 composite), zero per-type code.

**Resource-column parameters (`_id`/`_type`/`_lastUpdated`) already correctly reject `:missing`** via `ResourceColumnLoweringRule`'s existing, unconditional "any modifier throws" guard (`predicate.Modifier is not null` → `NotSupportedException`) — these parameters are never absent (every resource always has an id/type/lastUpdated), so `:missing` on them is a client error, not a compiler gap. No new code needed here; confirm via a test that this remains true after the new `:missing` branch is added (the new branch must not intercept resource-column parameters before they reach `ResourceColumnLoweringRule`'s existing guard — `LowerSearchParameter`'s dispatch order needs to keep resource-column detection ahead of, or otherwise not shadowed by, the new `:missing` branch).

**Chain-nested `:missing`** (`Patient?organization.name:missing=true` — the referenced Organization's name is absent) composes for free, by construction: `LowerSearchParameter` already runs at every chain-nesting depth (it's the same method chain-target expressions recurse through), so the new `:missing` branch is reachable there with no extra wiring. `Patient?organization:missing=true` (the Patient's own `organization` reference is absent) is not chain-specific at all — it's an ordinary `:missing` on a `Reference`-typed leaf parameter, already covered by the general mechanism above.

## 5. Explicitly out of scope (named, so Phase 10 inherits them as known requirements, not surprises)

- **`_total=estimate`** — throws, per §3. A genuine approximate-count mechanism (the only technically sound approach identified: `SET SHOWPLAN_XML ON`-style query-plan cardinality estimation, since raw statistics histograms don't compose correctly across this compiler's arbitrary CTE-graph shapes — joins, unions, intersects) has no fhir-server precedent to build against and was explicitly deferred by the user after weighing that trade-off.
- **Reference `:identifier` modifier** — needs a schema migration (`ReferenceSearchParam` has no identifier-search columns today) and `ReferenceSearchParameterRowGenerator` write-path changes. Out of place in a compiler-only phase; tracked as a future item.
- **Reference type modifier** (`subject:Patient=X`) — schema-ready (`ReferenceResourceTypeId` already exists, and the ninth increment's compartment fix already established exactly this filtering pattern), but explicitly deferred alongside `:identifier` per the user's own scoping decision, to keep this phase to exactly two features.
- **`:iterate` true multi-level recursion** — separately confirmed in-scope by the user as a priority, but is its own, larger, separate design effort (today's implementation is fhir-server's own documented one-hop-per-expression limitation, issue #1310) — not folded into this document; a future phase.
- **All previously-tracked Phase 8/9 follow-ups** (compartment nested-`And` gap, SMART/compartment instance-level scope enforcement, Token/Number/Quantity/Reference/Uri sort keys, `:ap`, Quantity System/Code matching, `_lastUpdated` partial-precision ranges) — unchanged by this document, still tracked where the roadmap already records them.

## 6. Testing

Matches every prior phase's discipline: golden `Explain()`/`Emit.Run` SQL-shape assertions (not loose substring checks — the roadmap's seventh increment found this loose-assertion pattern once already, missing a silently-dropped predicate), end-to-end `Resolve → Lower → Emit` proof tests composing `CountOnly`/`:missing` with sort, includes, chain, and compartment (proving §2's "composes for free" and §4's "chain-nested for free" claims aren't just asserted in prose), and explicit `NotSupportedException` tests for §3's estimate boundary and §5's deferred items.
