# Investigation: Ignixa.Search.Sql Structural Decomposition

**Feature**: search
**Status**: Viable
**Created**: 2026-08-11

## Problem Statement

`Ignixa.Search.Sql` compiles bound FHIR search expressions into parameterized T-SQL through a three-stage
pipeline (`Resolve` → `Lower` → `Emit`). The leaf tier of that pipeline is well factored — value-type lowering
lives in `Lowering/Leaf/` and `Lowering/Composite/` as small rule classes behind a dispatcher. The *stage
orchestrators* never received the same treatment.

90 files, ~8,500 lines. Three files carry a third of the project:

| File | Lines | Responsibilities bundled |
|------|-------|--------------------------|
| `Builders/SqlBuilder.cs` | 1711 | plan validation, CTE emission, sort emission, include emission, shape emission, predicate emission |
| `Lowering/Lower.cs` | 930 | input validation, match orchestration, expression dispatch, resource-column extraction, include planning, sort-spec building |
| `Lowering/StructuralContext.cs` | 548 | CTE-graph accumulator + chain rule + compartment orchestration + `$everything` rule + `:missing` table resolution |

Everything else is under 401 lines and mostly under 100.

The consequences are concrete rather than aesthetic:

- **Duplicated invariants.** Eight plan-validity rules are enforced independently in `Lower.Run` and
  `SqlBuilder.RejectUnsupportedCombinations`, with `HasCustomSortKey` copy-pasted into both files. The two copies
  can drift silently.
- **Shotgun surgery on sort keys.** Adding a `SortKeyKind` requires edits in four places across two files.
- **Schema knowledge scattered.** The `SearchParamType → table` mapping is encoded in four locations; the
  composite-shape → table mapping in three.
- **Inconsistent structure.** `LowerNode` is a third dispatcher, structurally identical to the leaf and composite
  dispatchers, but its rules are inline private methods instead of rule classes.

## Constraints

Any change here is bounded by three hard invariants:

1. **Byte-identical SQL.** Golden tests (`Ast/EmitTests.cs` 3114 lines, `EndToEndCompilationTests.cs` 2178,
   `Ast/EmitSqlGrammarTests.cs` 854) pin emitted text. The same plan must always emit the same bytes.
2. **Parameter ordinal ordering is load-bearing.** `EmitCteBlocks` runs before shape emission so CTE values take
   leading `@pN` ordinals, and `PlanExplainer` re-walks the plan to reproduce that numbering. Reordering emission
   breaks the explain format.
3. **`QueryPlan` is a public construction surface.** A plan can be built without going through `Lower`, so the
   emitter cannot assume `Lower` already validated it. This is why the duplication exists and any fix must
   preserve the guarantee, not just delete one copy.

The project is alpha and not yet wired into production (`README.md`), so internal API churn is cheap.

## Approach

Extend the pattern the project already established at the leaf tier — a thin dispatcher over one class per case —
upward into the stage orchestrators. Nothing below is a new idea for this codebase; it is the existing idiom
applied consistently.

### P0 — Extract shared plan invariants

`Lower.Run` and `SqlBuilder.RejectUnsupportedCombinations` both enforce: `Top >= 0`, `OffsetSpec` bounds,
`Enum.IsDefined(sortPhase)`, `Count.CurrentSortPhase` requires sort keys, typeless page requires a custom sort,
typed page forbids a custom sort, boundary count equals `ActiveKeyCount`, and include limits in range.
`HasCustomSortKey` is duplicated verbatim, with an in-code comment acknowledging it.

The reasoning behind the duplication is correct — each layer guards its own construction surface. The
implementation is not: one rule invoked from two sites gives the same guarantee without the drift risk.

**Amended during implementation.** The first design here was a single `QueryPlanInvariants.Validate(plan)`
called from both layers. Reading the code closely showed that to be wrong, for two reasons.

The guards are not all the same guard. Lower checks `options.IncludeLimit` (one input value) where the emitter
checks per-stage `stages[i].Limit` (plan data); Lower checks `sort.Count == 0` on the input `SortExpression`
list where the emitter checks the built `SortSpec`; Lower's `Enum.IsDefined(sortPhase)` always runs where the
emitter's only runs for a non-null `plan.Sort`. Only six rules genuinely share both rule *and* data.

The messages differ deliberately. Lower names options-level concepts ("SearchPaging.Offset requires an
OffsetSpec"); the emitter names plan-level ones ("OffsetPage must skip a non-negative row count"). Each
diagnoses the surface its caller actually used, and the test suite pins both — 311 `Should.Throw` sites, with
`EmitTests` asserting fragments like "silently dropped at the page seam" and `LowerOptionsTests` exercising
the options-level guards. Collapsing to one message would be a diagnostic regression for one audience and
would break tests that are right to exist.

So the shared unit exposes **predicates only**, and each layer keeps its own `throw` and its own wording:

```
KeysetPageInvariants           (rules: what is legal)
  ├── Lower.Run           throws in options vocabulary
  └── SqlBuilder.Run      throws in plan vocabulary
```

This removes the dangerous drift (two copies of a rule disagreeing about what is legal) while keeping the
harmless kind (two messages describing the same illegality to different callers). `HasCustomSortKey` was the
one true verbatim duplicate and became `SortSpec.HasCustomKey`, backed by `SortKey.IsCustom`.

### P1 — Split `SqlBuilder` by cohesion

Purely mechanical; no design decisions required.

| New unit | Contents |
|----------|----------|
| `PlanValidator` | `RejectDanglingReferences`, `RejectDanglingIncludeReferences`, `RejectUnsupportedCombinations`, `RejectUnsupportedPageCombinations`, `RequireIndex` |
| `CteEmitter` | `EmitCte` switch + per-variant emitters |
| `SortEmitter` | `EmitSortJoins`, `SortValueExpr`, `EmitOrderBy`, `EmitSeekPredicate`, `SentinelFor`, `ActiveKeyIndices`, `EmitMissingPrimaryFilter`, `EmitSortSelectColumns` |
| `IncludeEmitter` | `WriteIncludeStageCtes`, `EmitIncludeStage`, `EmitIncludeLimitStage`, `EmitSeedExists`, `EmitConstraintGuard` |
| `ShapeEmitter` | `EmitCountOnlyShape`, `EmitMatchOnlyShape`, `EmitIncludesShape`, `EmitGlobalIncludesPage` |
| `PredicateEmitter` | `EmitPredicate`, `EmitParam`, `EmitCollation`, `EscapeLike` |

Two deliberate non-changes:

- **Keep the `switch` over `CteDefinition`.** An exhaustive switch over a closed AST gives compiler-checked
  completeness. Moving `Emit` onto the records would put T-SQL generation inside a type that is supposed to be a
  pure value, and would couple the AST to one backend.
- **Do not introduce interfaces or DI.** These are static pure functions. The file split delivers the benefit;
  indirection would not.

**Retracted during implementation.** This section previously claimed a dispatch defect: that `Run` selects its
terminal shape from the derived booleans `plan.CountOnly` and `plan.Includes.Count > 0` when
`plan.EffectiveShape` already models that choice as a discriminated union, and should switch on the union.

That claim is wrong. `EffectiveShape` partitions into `Count` / `Matches` / `IncludesPage`, but the emitter
partitions into count / has-include-stages / plain — and `Includes` is an orthogonal field, not a shape. A
`Matches` plan carrying include stages must route to `EmitIncludesShape`, which switching on `EffectiveShape`
alone would not do. The existing two-field test is correct; the union does not subsume it. Left as-is.

### P2 — `SortKeyKind` strategy

`EmitSortJoins` and `SortValueExpr` each switch over the same six-case enum with parallel arms. Adding a sort key
kind means coordinated edits in both.

```csharp
internal abstract class SortKeyEmitter
{
    public abstract string? Join(SortKey key, int index, bool isPrimary);
    public abstract string ValueExpr(SortKey key, int index, bool guaranteedNonNull);
    public static SortKeyEmitter For(SortKeyKind kind) => ...;
}
```

Roughly 100 lines collapse into four classes (LastUpdated/ResourceType share one, as do String/Date), and "resource
columns need no join" becomes a `null` return rather than a `continue` in the caller's loop.

**Scope narrowed during implementation.** Two claims in the original review were wrong:

- `SentinelFor` is not a fourth parallel switch over `SortKeyKind`. It maps a *SQL type string* to a literal and is
  reachable only from the `Aggregated` branch, so it moves wholesale into `AggregatedSortKeyEmitter` as a private
  member rather than becoming a virtual.
- `EmitMissingPrimaryFilter` is not safely polymorphic. Its guard is
  `key.Kind == SortKeyKind.LastUpdated || key.SearchParamId is null`, so a hypothetical `ResourceType`/`ResourceId`
  key carrying a non-null `SearchParamId` falls through to the `DateTimeSearchParam` arm. A `MissingFilter` virtual
  would silently relocate that fall-through. The method keeps its guard and its `Aggregated` special case in
  `SortEmitter`; only the String/Date table name is read back from the registry (via a type test with the original
  `else` branch preserved as the fallback) so table names live in exactly one place.

`SortEmitter` retains all phase and index policy — the `i == 0 && Phase == MissingPrimary` skip, `isPrimary = i == 0`,
and `guaranteedNonNull = index == 0 && Phase == Valued`. The emitters answer only "what does this kind look like",
never "which key am I".

**One deliberate behaviour delta.** The old if-chains ended in a bare `else` that treated *any* unrecognised
`SortKeyKind` as `Date` — emitting a `DateTimeSearchParam` join and a `StartDateTime` comparison. `SortKeyEmitter.For`
throws `NotSupportedException` instead. `SortKeyKind` is a public enum, so `(SortKeyKind)99` is constructible by a
caller building a `QueryPlan` directly, which makes this reachable in principle. It is not reachable from any
in-repo path and no golden test covers it.

Kept rather than reverted, for two reasons. It matches the established convention in this same file set — HEAD
already throws `NotSupportedException` for undefined `ChainDirection` (twice) and `IncludeDirection` values, so
`SortKeyKind`'s silent fallback was the outlier. And the old behaviour silently emitted a *wrong query* with no
signal, which is a worse failure mode than a loud exception. Recorded here rather than buried because it is the one
place this refactor is not strictly byte-identical, and a reviewer should get to disagree with it.

### P3 — Decompose `Lower`

`Lowering/Leaf/` and `Lowering/Composite/` already pair a dispatcher with rule classes. `LowerNode` is the same
shape with its rules inlined. Make the third dispatcher look like the first two:

- `Lowering/Structural/StructuralLoweringDispatcher` + `AndLoweringRule`, `OrLoweringRule`, `UnionLoweringRule`,
  `NegationLoweringRule`
- `IncludeStagePlanner` — `BuildIncludeStages`, `ResolveInclude`, `ResolveTypeIds`, `Overlaps`,
  `TopologicalSort`, `ResolvedInclude` (~180 lines)
- `SortSpecBuilder` — `BuildSortSpec`, `BuildSortKey`
- `ResourceColumnExtractor` — `ExtractResourceColumnPredicates` and its `TryLower*` helpers

`Lower.cs` reduces to roughly 120 lines of orchestration.

### P4 — Separate `StructuralContext`'s accumulator from its rules

`StructuralContext` is both the CTE-graph accumulator and the host for several lowering rules. `$everything`
orchestration alone is ~130 lines and has nothing to do with being "the structural context". Note that
`CompartmentLoweringRule.cs` already exists as its own file while its orchestration lives in `StructuralContext` —
one responsibility split across two files. Tests already treat `$everything` as a unit
(`PatientEverythingLoweringTests.cs`) that the production code does not have.

- `CteGraphBuilder` — `Add`, `Intersect`, `Union`, `Except`, `Ctes`, `Origins`. This is all the rules need.
  Collapses the ~15 repeated `_ctes.Add(...); return new CteRef(_ctes.Count - 1);` pairs into one helper.
- `PatientEverythingLoweringRule`, `ChainLoweringRule`, `MissingParameterLoweringRule` as siblings of the existing
  `CompartmentLoweringRule`.

**Correction found during implementation: `_ctes` and `_origins` are not parallel lists.** This section originally
assumed they advance in lockstep. They do not — `_origins` is *sparse*. It holds `CteOrigin(int CteIndex,
Expression SourceNode)` records, and only the leaf-adjacent entry points record provenance (`Lower` ×2,
`LowerNotReferenced`, `LowerTokenText`, `LowerComposite`, `LowerParameterPresence`); every other append touches
`_ctes` alone. `CteGraphBuilder` therefore needs two `Add` overloads, with and without provenance. Implementing
the assumed lockstep invariant would have added a provenance entry for every set-operation and structural CTE and
broken the provenance tests.

A consequence worth noting: because `_origins` is sparse, `PlanProvenance` can only attribute roughly half the
CTEs back to source IR. Set operations, `ChainJoin`, `CompartmentSource` and the `$everything` scaffolding carry
no origin at all. Whether that is intentional or a gap is a separate question — see deferred findings.

### P5 — Single source for schema mappings

`SearchParamType → table` is encoded in `ResolveMissingTable`, in every leaf rule's
`SqlCatalog.Default.Table("StringSearchParam")` literal, and again in `EmitSortJoins`/`SortValueExpr`
(`"StringSearchParam"`/`"Text"`, `"DateTimeSearchParam"`/`"StartDateTime"`). The composite-shape → table mapping
is in `ResolveMissingCompositeTable`, in `CompositeLoweringDispatcher`, and in each composite rule. One
`SearchParamTableMap` (or an extension of `SqlCatalog`) should own both.

The same pattern appears in emission:

- Type-id set filters render three ways — `EmitTypeInFilter`, the hand-rolled OR-join in `EmitChainJoin`, and
  `IN (...)` in `EmitMultiTypeResourceSource`.
- Visibility filtering exists twice — `ResourceRowFilter` returning a `" AND …"` string, versus inline
  `IsHistory`/`IsDeleted` clause-list appends in `EmitMultiTypeResourceSource`.

### P6 — Replace string concatenation with a clause model

`SqlTextWriter` exists but only the outermost level uses it. Every emitter hand-builds
`"    SELECT …\n    FROM …\n    WHERE …"`, producing load-bearing whitespace that the code documents
apologetically ("the helper's leading space is replaced by this line's indentation. An inline caller must not
trim it"). The empty-type-list guard in `RejectUnsupportedCombinations` exists solely because an empty clause list
interpolates to invalid SQL.

A small `SelectStatement { Columns, From, Joins, Where[], OrderBy, Top }` with one renderer removes ~200 lines of
duplicated formatting and makes "empty clause list emits broken SQL" unrepresentable rather than guarded.

### P7 — Unify the parameter-ordinal traversal

`PlanExplainer` re-implements `SqlBuilder`'s traversal to reproduce identical `@pN` ordinals, held together by
long synchronisation comments, and already reaches into `SqlBuilder.SeedsFromTrimmedMatchPage` to avoid one copy.
The correct fix is for emission to yield explain rows as a by-product of a single traversal. This is the deepest
coupling and the most invasive change; it is recorded here but ranked last.

## Tradeoffs

**Positive:**

- Removes duplicated invariants and the associated drift risk (P0).
- File sizes drop under the project's 500-line comfort threshold without inventing new abstractions.
- Adding a `CteDefinition` variant, a sort key kind, or a search parameter type becomes a localised change.
- Structure becomes uniform: three dispatchers that look alike instead of two plus an inline switch.
- Golden tests make every step verifiable — output either matches byte-for-byte or it does not.

**Negative:**

- File count grows by roughly 25. Navigation cost is real, though the existing `Leaf/`/`Composite/` folders show
  the project already accepts this trade.
- The dense explanatory comments in the current code are institutional memory and must be carried into the
  extracted units, not dropped.
- P6 and P7 touch emission ordering, which is exactly where the byte-identical and ordinal invariants live. They
  carry meaningfully more risk than P0–P5 and should not be bundled with them.
- Refactoring an alpha component that is not yet in production earns no immediate user-facing value.

## Recommended Sequencing

| Step | Risk | Verifiable by | Status |
|------|------|---------------|--------|
| P0 invariants | None — pure extraction | `dotnet test test/Ignixa.Search.Sql.Tests` | Done |
| P1 `SqlBuilder` split | Low — mechanical move | Golden tests unchanged | Done |
| P3 `Lower` split | Low | `Lowering/LowerTests.cs` | Done |
| P4 `StructuralContext` split | Low | `PatientEverythingLoweringTests.cs` | Done |
| P2 sort strategy | Low | Golden tests unchanged | Done |
| P5 schema map | Medium — touches literals used in emitted text | Golden tests | Deferred |
| P6 clause model | Medium-high — changes how SQL text is assembled | Golden tests | Deferred |
| P7 ordinal unification | High — changes traversal | `PlanExplainer` plan-shape tests | Deferred |

P2 was resequenced behind P3/P4 during implementation: `SortEmitter.cs` came out of the P1 split at 289 lines, so
P2 no longer serves the file-size goal and is now purely the polymorphism item. P3 and P4 target the only two
files still over 500 lines, so they earn priority.

P0 through P4 are independently shippable and carry no behavioural risk. P5 onward should be evaluated separately
once the earlier steps land.

## Deferred findings

Surfaced while implementing P0–P1. None are regressions — all predate this work. Recorded here rather than fixed,
because each changes emitted SQL or observable behaviour and so does not belong in a byte-identical refactor.

**1. Inconsistent binding of schema ids (cosmetic, plan-cache cost).** `EmitResourceSource` binds `ResourceTypeId`
as a `@pN` parameter, while sibling emitters inline the same class of schema id as a literal — as does
`EmitNotReferencedSource` for `TargetResourceTypeId`. Since resource type ids come from the schema and not from
user input, the parameterised form buys nothing and costs a distinct plan-cache entry per type. Converging on
literals would shift every downstream parameter ordinal, forcing a golden-test rebaseline; that is a deliberate
change, not a refactor.

**2. Latent correctness gap: `Top`-capped keyset page combined with `_include`.** `EmitIncludesShape` decides
whether the match seed excludes the has-more probe row by checking `OffsetSpec.ProbeExtraRow` only. A keyset page
capped by `Top` also fetches one extra row to detect has-more, but is not covered by that test, so its include
stages will seed from the probe row — pulling includes for a resource that is not in the returned page. Today's
unprotected behaviour is pinned by a test, so fixing it is a deliberate behavioural change with a test update.

**3. `CteDefinition` exhaustiveness is weaker than it appears.** The `_ => throw` arm in `CteEmitter.EmitCte` is
unreachable today, and reads as a compile-time completeness check. It is not: C# cannot prove exhaustiveness for a
switch *expression* returning `string`, so a new `CteDefinition` subtype fails at runtime, not at compile time.
The same caveat applies to the enum switches in P2 — C# never checks enum switch exhaustiveness. This weakens the
"keep the switch, it is compiler-checked" argument used above, and correspondingly strengthens the case for P2:
a strategy registry with a static all-values-mapped assertion fails earlier than four runtime-throwing arms.

**4. Wildcard `:iterate` includes are mutually dependent, producing a misleading cycle error.**
`IncludeStagePlanner.Overlaps` treats a null (wildcard) `Produces`/`Requires` as matching anything. Two wildcard
`:iterate` includes therefore each depend on the other, so `TopologicalSort` reports a cycle. The user-facing
error talks about cycles rather than about unsupported wildcard iteration, which points at the wrong thing. This
reads as a genuine bug rather than a deliberate refusal.

**5. `PlanProvenance` cannot attribute set-operation or structural CTEs.** Because `_origins` is sparse (see P4),
Intersect/Union/Except, `ChainJoin`, `CompartmentSource` and the `$everything` scaffolding have no origin entry.
If provenance is meant to map every CTE back to source IR, it covers roughly half of them today.

**6. The chain depth-guard message hardcodes its own threshold.** `LowerChain`'s refusal states "10-level" in
prose while `MaxChainDepth = 10` is declared immediately above. The two diverge silently if the constant is
raised — precisely the action the message invites ("raise this threshold deliberately").

**7. Dead assignment in `Lower.LowerMatchSet`.** `matchSource` is assigned `expression` and then unconditionally
overwritten in the `else` branch; the initial assignment is live only when `expression is null`, where it is
never read. Harmless, but it obscures the control flow.

## Verdict

Viable. The pipeline design is sound and the leaf tier is already correct — this is a decomposition of three
orchestrator files along boundaries the codebase itself established, not an architectural redesign.

P0 and P1 are implemented and green: `SqlBuilder.cs` went from 1711 lines to 88, split into seven cohesive
emitters, with all 1153 golden tests unchanged across both target frameworks. The duplicated keyset invariants
now share predicates while each layer keeps its own diagnostic wording. P3 and P4 follow, then P2.
