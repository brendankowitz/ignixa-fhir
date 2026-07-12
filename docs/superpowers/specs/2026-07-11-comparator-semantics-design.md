# Number/Quantity/DateTime Comparator Semantics Canonicalization — Design

**Date:** 2026-07-11
**Branch:** `worktree-sql-datalayer-architecture`
**Resolves:** Prerequisite #1 from `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md`'s Post-Plan section ("resolve the quantity/number comparator semantics decision... before writing Phase 2's task list")
**Related:** PR #328 (Phase 0/1 of the SQL data layer cleanup); this is "Task A," designed to land and ship before Phase 2 (composite semantic leaf) design starts

---

## Goal

Three independent implementations compare a stored `[Low, High]` (or `[Start, End]`) range against
a search value for `gt`/`ge`/`lt`/`le`, and none of them is fully correct against the canonical
binding (`gt→High>v, ge→High>=v, lt→Low<v, le→Low<=v`, confirmed via `microsoft/fhir-server`'s
`NumericRangeRewriter.cs` — the ancestor this codebase's schema and merge model derive from), each
with a different partial-failure pattern:

| Implementation | Domain | gt | ge | lt | le |
|---|---|---|---|---|---|
| `ComparisonPredicates` (SQL, single-param) | Number/Quantity | ✗ | ✗ | ✗ | ✗ |
| `CompositeSearchParameterQueryGenerator.ApplyQuantityFilterAsync` (SQL, composite) | Number/Quantity | ✗ | ✓ | ✗ | ✓ |
| `ComparisonValueVisitor` (Core, backs FileSystem/BlobStorage/InMemoryIndex) | Number/Quantity | ✓ | ✓ | ✗ | ✗ |

Additionally, `sa`/`eb` (FHIR's "starts after"/"ends before" prefixes) are architecturally aliased
to `gt`/`lt` at parse time (inherited from ms-fhir-server itself) rather than implemented as their
own distinct semantics, and — separately, found while tracing this — the DateTime domain has the
same class of gap on the InMemory backend specifically: `sa` and `gt` (and `eb`/`lt`) currently
produce an *identical* `BinaryExpression` shape once `FieldName` is dropped, which
`SearchQueryInterpreter`'s dispatch does today.

This task canonicalizes all of the above on one mechanism: give `sa`/`eb` real `BinaryOperator`
values (not just for Number/Quantity — for DateTime too), so the operator alone is always
self-describing and no backend needs `FieldName`-based disambiguation to get correct behavior.

## Non-goals

- **Not changing the `eq`/`ap`/`ne` widening rule.** These already correctly widen the search value
  into a `[lo, hi]` precision range and decompose into a pair of `Ge`/`Le` (AND) or `Lt`/`Gt` (OR)
  expressions upstream, in `SearchValueExpressionBuilderHelper`. That mechanism is correct and
  untouched — this task only fixes what `gt`/`ge`/`lt`/`le`/`sa`/`eb` do with the *stored* value's
  range.
- **Not a Phase 2 concern.** This is explicitly a prerequisite fix, not part of Phase 2's composite
  structural refactor (`DetermineCompositeType`, `IsReferenceExpression`/`IsTokenExpression`
  sniffing). Phase 2 starts once this lands.
- **Not touching `SearchQueryInterpreter.VisitBinary`'s dispatch.** An earlier design draft
  considered making the InMemory interpreter respect `expression.FieldName` to recover the DateTime
  `sa`/`gt` distinction. Rejected in favor of giving `sa`/`eb` their own operator — simpler, unifies
  the fix mechanism across Number/Quantity and DateTime, and needs no interpreter dispatch changes.
- **Not touching `TokenCodeStorage`, `CHK_TokenSearchParam_CodeOverflow`, or composite token
  overflow.** Unrelated findings from PR #328's review cycle, already tracked separately in the
  plan's Post-Plan section.
- **Not a data migration.** Stored `LowValue`/`HighValue`/`StartDateTime`/`EndDateTime` columns are
  already correct; only the *query* formula that reads them changes. See Risk section — this is
  still a live behavior change, just not a data problem.

---

## Canonical semantics (reference table)

Derived from `microsoft/fhir-server`'s `NumericRangeRewriter.cs` (`gt→High, ge→High, lt→Low,
le→Low`) and this codebase's own DateTime implementation (already correct, used as the second
confirming reference — same shape, `Start`/`End` instead of `Low`/`High`):

| Prefix | Number/Quantity (stored `[Low, High]`) | DateTime (stored `[Start, End]`) |
|---|---|---|
| `gt` | `High > v` | `Stored.End > search.End` |
| `ge` | `High >= v` | `Stored.End >= search.Start` |
| `lt` | `Low < v` | `Stored.Start < search.Start` |
| `le` | `Low <= v` | `Stored.Start <= search.End` |
| `sa` | `Low > v` | `Stored.Start > search.End` |
| `eb` | `High < v` | `Stored.End < search.Start` |
| `eq`/`ap` | `Low >= lo AND High <= hi` (unchanged — see Non-goals) | unchanged |
| `ne` | `High < lo OR Low > hi` (unchanged — see Non-goals) | unchanged |

For Number/Quantity, `v` is the unwidened search value (per FHIR spec: `gt`/`ge`/`lt`/`le`/`sa`/`eb`
ignore the search value's own implicit precision). For DateTime, the search value already carries
its own `[Start, End]` range from precision, and — as the existing DateTime code already does
correctly — different prefixes compare against different sides of *that* range (`search.End` for
`gt`/`sa`, `search.Start` for `lt`/`eb`/`ge`, etc.); this is pre-existing, correct behavior and does
not change.

The pattern across both domains: `{lt, le, sa}` read the stored range's **low/start** bound;
`{gt, ge, eb}` read the stored range's **high/end** bound. `sa`/`eb` share their extracted bound
with `lt`/`le`-and-`gt`/`ge` respectively but use strict inequality against the *other* side of the
search range than their same-bound sibling — this is exactly the distinction that gets lost when
`sa` is aliased to plain `gt`/`lt`.

---

## Changes

### 1. `Ignixa.Search.Expressions.BinaryOperator` (Core)

Add two new members, appended (not inserted) — verified safe: `BinaryOperator`'s own doc comment
warns about relative-order dependency in a class called `DateTimeBoundedRangeRewriter`, which no
longer exists in this codebase (likely renamed to `DateTimeEqualityRewriter` at some point, leaving
a stale comment). `DateTimeEqualityRewriter.cs` was checked directly: it pattern-matches on named
`BinaryOperator` values, never compares them ordinally. No other ordinal dependency found anywhere
in the codebase (`ComparisonPredicates.cs`, `CompositeSearchParameterQueryGenerator.cs`,
`ComparisonValueVisitor.cs` are the only files with `BinaryOperator`-typed switches; `BinaryOperator`
is never cast to/from `int` for persistence or serialization anywhere).

```csharp
public enum BinaryOperator
{
    Equal = 0,
    GreaterThan = 1,
    GreaterThanOrEqual = 2,
    LessThan = 3,
    LessThanOrEqual = 4,
    NotEqual = 5,
    StartsAfter = 6,   // new
    EndsBefore = 7,    // new
}
```

### 2. `SearchValueExpressionBuilderHelper.cs` (Core, parser)

Two switches stop aliasing `sa`/`eb`:

- `GenerateNumberExpression` (Number/Quantity): remove `case SearchComparator.Sa:` /
  `case SearchComparator.Eb:` fallthroughs into `Gt`/`Lt`. Each becomes its own case emitting
  `BinaryExpression(BinaryOperator.StartsAfter/EndsBefore, fieldName, componentIndex, number)`.
- The DateTime switch (`Visit(DateTimeSearchValue)`): `case SearchComparator.Sa:` /
  `case SearchComparator.Eb:` stop using `Expression.GreaterThan`/`Expression.LessThan` and use
  `Expression.StartsAfter`/`Expression.EndsBefore` instead — two new one-line static factory methods
  on `Expression` (`src/Core/Ignixa.Search/Expressions/Expression.cs:149-167`), added next to the
  existing `GreaterThan`/`GreaterThanOrEqual`/`LessThan`/`LessThanOrEqual` factories, each just
  `return new BinaryExpression(BinaryOperator.StartsAfter/EndsBefore, fieldName, componentIndex,
  value);` — same one-line shape as its four neighbors. **`FieldName` stays exactly as it is today**
  (`FieldName.DateTimeStart` for `sa`, `FieldName.DateTimeEnd` for `eb`), only the operator changes.
  This keeps SQL's existing dispatch-by-`FieldName` routing unchanged (see below).

### 3. `ComparisonPredicates.cs` (SQL, single-param path)

- `ApplyNumberRangeComparison`/`ApplyQuantityRangeComparison`: flip `GreaterThan`/`GreaterThanOrEqual`
  to read `High`, `LessThan`/`LessThanOrEqual` to read `Low` (currently backwards — reads exactly
  the opposite column per operator). Add `StartsAfter => Low > value`, `EndsBefore => High < value`.
  Leave the `Equal`/`NotEqual` arms in place with corrected (canonical) formulas even though they're
  unreachable today (confirmed via the FHIR spec research: `eq`/`ne` always arrive pre-expanded into
  pairs for Number/Quantity, never as a bare operator) — cheap defense-in-depth, self-documenting,
  costs nothing to keep correct.
- `ApplyDateTimeStartComparison`/`ApplyDateTimeEndComparison`: add `StartsAfter`/`EndsBefore` arms.
  Because `FieldName` dispatch is unchanged (see #2), `ApplyDateTimeStartComparison` only ever
  receives `StartsAfter` for what was previously `GreaterThan` in that exact dispatched context —
  so its new arm's formula (`StartDateTime > value`) is **identical** to the existing `GreaterThan`
  arm's formula there. Same for `ApplyDateTimeEndComparison`'s new `EndsBefore` arm vs. its existing
  `LessThan` arm. This is a behavior-preserving addition for DateTime on the SQL backend, not a fix
  — SQL's DateTime `sa`/`eb` were already correct (via `FieldName` dispatch), this just makes that
  correctness explicit at the operator level instead of coincidental.
- New composite-shaped overloads (operating on `TokenQuantityCompositeSearchParamEntity`'s
  `LowValue`/`HighValue`) for `CompositeSearchParameterQueryGenerator` to delegate to (see #4) —
  mirrors ms-fhir-server's own design, where its composite generator delegates to the same
  `QuantityQueryGenerator` the single-param path uses, rather than reimplementing.

### 4. `CompositeSearchParameterQueryGenerator.cs` (SQL, composite path)

`ApplyQuantityFilterAsync`'s inline switch is replaced with calls to the new composite-shaped
`ComparisonPredicates` overloads. Closes the duplication that let this path partially drift from
the single-param path in the first place — after this, there is exactly one place that knows the
Number/Quantity comparator formulas for SQL, not two.

### 5. `ComparisonValueVisitor.cs` (Core, InMemory backends: FileSystem/BlobStorage/InMemoryIndex)

- `AddComparison`'s switch is generic across *all* search value types (String, Token, DateTime,
  Number, Quantity, Reference, Uri) and is already structurally correct (`first.CompareTo(second)`
  compared per operator) — **not rewritten**. Add `StartsAfter` routed to the same comparison as
  `GreaterThan`, `EndsBefore` routed to the same as `LessThan` — the comparison *direction* is
  identical between `sa`/`gt` and `eb`/`lt`; only which bound gets passed as `first` differs, and
  that's decided by the `Visit(...)` methods below, not `AddComparison`.
- `Visit(NumberSearchValue)` / `Visit(QuantitySearchValue)`: currently always pass `.High` regardless
  of operator. Change to select `.Low` for `{LessThan, LessThanOrEqual, StartsAfter}` and `.High`
  for `{GreaterThan, GreaterThanOrEqual, EndsBefore}`.
- `Visit(DateTimeSearchValue)`: currently always passes `.Start` regardless of operator. Change to
  select `.Start` for `{LessThan, LessThanOrEqual, StartsAfter}` and `.End` for `{GreaterThan,
  GreaterThanOrEqual, EndsBefore}` — same selection function, `Start`/`End` instead of `Low`/`High`.

No changes needed to `SearchQueryInterpreter.VisitBinary`/`GetMappedValue` — `expression.FieldName`
was never used for Number/Quantity (there's only one `FieldName.Number`/`FieldName.Quantity` per
value, so nothing was ever lost there), and for DateTime the operator is now fully self-describing,
so the fact that `FieldName` is dropped stops mattering.

### 6. `SearchParameterQueryGenerator.GenerateQuantityAndQueryAsync` (fixed earlier, this session)

No code change expected — verify only. Its `eq`/`ap` handling calls `ComparisonPredicates.
ApplyQuantityRangeComparison` twice (chained `.Where()`), so it automatically inherits whatever
`Ge`/`Le` formulas #3 lands. Its standalone `ne` handling (`HighValue < firstValue || LowValue >
secondValue`) is a direct inline predicate, not a delegate call — confirm this formula is still
correct under the new canonical convention (it should be: full-disjointness doesn't depend on the
`gt`/`ge`/`lt`/`le` directional convention, it's symmetric either way) as part of this task's test
pass, not as a design change.

---

## Testing

- **`ComparisonPredicatesTests.cs`**: extend to a `[Theory]` covering all 8 operators × representative
  `[Low, High]` stored ranges (including `Low == High` point values and `Low != High` fuzzy ranges,
  since point values can't distinguish overlap from containment) × Number and Quantity entities.
  Directly encodes the divergence-matrix research as executable, permanent regression coverage.
- Same shape for the new composite-path overloads, and for `ApplyDateTimeStartComparison`/
  `ApplyDateTimeEndComparison`'s new arms (even though behavior-preserving there, still worth a
  regression pin).
- **`ComparisonValueVisitor`**: new test coverage (currently zero) for Number/Quantity/DateTime
  bound-selection across all 8 operators. `ComparisonValueVisitor` is `internal` — test via
  `SearchQueryInterpreter`'s public surface (`VisitBinary`) end-to-end rather than reflection or
  `InternalsVisibleTo`, matching how the rest of the InMemory backend is tested.
- **End-to-end regression tests** (real parser → real generator, mirroring `SearchParameterQuery
  GeneratorQuantityAndTests.cs`'s pattern) for a few representative fuzzy-value scenarios per
  backend (SQL and InMemory), since Bug 1 already proved that comparator-level unit tests alone
  don't catch extraction/wiring bugs — only an end-to-end test proves the real REST search path
  produces the right answer.
- Explicit test: `GenerateQuantityAndQueryAsync`'s `ne` case, confirming it's still correct after
  `ComparisonPredicates`' `Ge`/`Le` formulas change underneath the `eq`/`ap` case it shares code with.

---

## Risk: this is a live search-behavior change, not just a bug fix

Unlike the `TokenCodeStorage` overflow-threshold bug fixed earlier this session (no legacy data
affected, confirmed via user decision), this fix changes what search results **are returned today**
for any existing indexed value with `Low != High` — i.e., any FHIR value written with implicit
precision (`5.4` mg, a partial date, etc.). No data migration is needed — the `LowValue`/`HighValue`/
`StartDateTime`/`EndDateTime` columns are already correct; only the query formula that reads them
changes. But `gt`/`ge`/`lt`/`le` boundary-case searches against fuzzy values may return different
results after this ships. This should be called out explicitly wherever this change is described to
users of the repo (PR description, release notes) — it is a correctness fix, but one with visible
external effect, not an invisible-to-data-consumers bug fix.

---

## Open items carried forward (not blocking this task)

- `sa`/`eb` for String/Token/Reference: FHIR doesn't define these prefixes for those types, so no
  work needed — `BinaryOperator.StartsAfter`/`EndsBefore` simply won't be constructed for them.
- The composite token overflow bug (composite tables still split token codes at 128 with no
  overflow-aware read path) — unrelated, already tracked in the plan's Post-Plan section.
- `CHK_TokenSearchParam_CodeOverflow`'s fate (materialize vs. remove from model) — unrelated,
  already tracked.
