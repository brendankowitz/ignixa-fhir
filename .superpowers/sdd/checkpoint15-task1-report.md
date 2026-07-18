# Checkpoint 1.5 -- Task 1: Seek predicate AND/OR precedence fix

## Bug

`Emit.EmitSeekPredicate` (`src/Core/Ignixa.Search.Sql/Ast/Emit.cs`) builds a keyset-pagination
seek predicate as a list of `branches` joined with `"\n       OR "`. Because the method always
appends the two final type/sid tie-break branches unconditionally, `branches.Count` is always
`>= 2` for any real page, so the method always returned an **unparenthesized** multi-branch `OR`
chain.

`Emit.Run` puts that string into `whereClauses` alongside the `OuterPredicate`-derived filter
and/or the `SortPhase.MissingPrimary` `NOT EXISTS` filter, then joins everything with
`string.Join(" AND ", whereClauses)`. T-SQL's `AND` binds tighter than `OR`, so whenever the seek
predicate shared `whereClauses` with another clause, the emitted SQL parsed as
`(clauseA AND seekBranch0) OR seekBranch1 OR seekBranch2` -- the second and third OR branches
silently bypassed `clauseA` entirely.

Two concrete failure modes, neither previously covered by a test (every existing `Page`-bearing
golden test had the seek predicate as the *only* `WHERE` clause):

1. **Filtered + sorted search, page 2+**: `Patient?_lastUpdated=gt2020-01-01&_sort=name` page 2
   could return rows that violate `_lastUpdated` entirely, because the tie-break branches ignored
   the outer filter.
2. **`SortPhase.MissingPrimary` + page 2+**: the `NOT EXISTS` "value is missing" filter only bound
   to the seek predicate's first branch; rows that *do* have a name value could leak into the
   missing-name phase's later pages via the unfiltered tie-break branches -- duplicate/incorrect
   rows across the Valued/MissingPrimary phase boundary.

## Fix

`EmitSeekPredicate`'s final `return` now wraps the joined multi-branch chain in parentheses,
mirroring the convention `EmitPredicate` already uses for `Predicate.And`/`Predicate.Or` (both
already return a single parenthesized/atomic unit "safe to AND with siblings"):

```csharp
return branches.Count == 1
    ? branches[0]
    : $"({string.Join("\n       OR ", branches)})";
```

The single-branch case is left bare (matches existing convention: a lone term needs no
disambiguating parens). No other line in `EmitSeekPredicate` changed -- branch construction,
direction operators (`>`/`<`), sentinel handling (`ISNULL(...)`/`N''`/`'0001-01-01...'`), and every
call into `SortValueExpr` (the F1 invariant) are untouched.

## Before / after (representative case: filtered + sorted, page 2)

Plan: `ParamSource` on `Text = 'Smith'`, `OuterPredicate: ResourceId = '123'`, `Sort`: single
ascending String key (`_sort=name`), `Page`: boundary `["Adams"]`, type `103`, sid `5000`.

**Before** (unparenthesized -- WRONG, `ResourceId = @p1` only applies to the first OR branch):

```sql
WHERE ResourceId = @p1 AND sk0.Text > @p2
       OR (sk0.Text = @p2 AND m.T1 = @p3 AND m.Sid1 > @p4)
       OR (sk0.Text = @p2 AND m.T1 > @p3)
ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC
```

Parses as `(ResourceId = @p1 AND sk0.Text > @p2) OR (...) OR (...)` -- rows matching either
tie-break branch bypass the `ResourceId` filter completely.

**After** (parenthesized -- CORRECT, `ResourceId = @p1` ANDs against the whole chain):

```sql
WHERE ResourceId = @p1 AND (sk0.Text > @p2
       OR (sk0.Text = @p2 AND m.T1 = @p3 AND m.Sid1 > @p4)
       OR (sk0.Text = @p2 AND m.T1 > @p3))
ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC
```

The same shape applies to the `SortPhase.MissingPrimary` case: `NOT EXISTS(...) AND (branch0 OR
branch1 OR branch2)` instead of `NOT EXISTS(...) AND branch0 OR branch1 OR branch2`.

## Test coverage added

`test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

- `GivenAnOuterPredicateAndASortWithAPageBoundary_WhenEmitted_ThenTheSeekPredicateOrChainIsParenthesizedSoItStaysAndedWithTheOuterFilter`
  -- `OuterPredicate` + `Sort` + `Page` together; asserts the exact
  `WHERE ResourceId = @p1 AND (sk0.Text > @p2 OR (...) OR (...))` text.
- `GivenTheMissingPrimaryPhaseWithAMultiBranchPageBoundary_WhenEmitted_ThenTheNotExistsFilterAppliesToEveryBranchOfTheParenthesizedSeekPredicate`
  -- a two-key sort in `SortPhase.MissingPrimary` with a page boundary, giving the seek predicate 3
  branches (not just the 2-branch degenerate case), proving `NOT EXISTS` binds to the whole
  parenthesized chain, not just the first branch.

`test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:

- Strengthened `GivenTheMissingPrimaryPhaseWithAPageBoundary_WhenCompiledEndToEnd_ThenTheSeekPredicateIsSidOnly`
  to assert the full `NOT EXISTS(...) AND (...)` combined text (previously only checked loose,
  independent substrings that couldn't have caught this bug).

Four existing golden-SQL assertions in `EmitTests.cs` (`GivenASortWithAPageBoundary...`,
`GivenAMultiKeySortWithMixedDirectionsAndASecondaryKeyTie...`, `GivenNoSortButAPageBoundary...`,
`GivenASortedIncludedSearchOnPageTwo...`) had their expected multi-branch `OR` chains updated to
include the new wrapping parentheses.

## Verification

- `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj` -- 0 warnings, 0 errors.
- `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` -- 225/225 passing on
  both `net9.0` and `net10.0` (223 pre-existing + 2 new).

## Scope check

Only `EmitSeekPredicate`'s return statement changed in production code. No changes to
`EmitPredicate`, `EmitMissingPrimaryFilter`, `SortValueExpr`, `EmitOrderBy`, branch construction,
or any other Emit.cs method.
