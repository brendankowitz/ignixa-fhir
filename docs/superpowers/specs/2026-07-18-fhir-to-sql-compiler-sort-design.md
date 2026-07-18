# Sort and Keyset Pagination — Design (Phase 8, part 2)

**Builds on:** Phases 1-7 and Phase 8 part 1 of `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md` (complete, merged to `feature/fhir-to-sql-compiler`). The CTE-graph IR, `Resolve`'s batched-I/O `SymbolTable`, `Lower`'s structural tier, and the `cteMatchPage`/`IncludeStage`/Kahn-sort machinery (Phase 7) all exist. This is the **last increment before Checkpoint 1.5** — the roadmap's explicit stop-and-review gate before Phase 9 (DataLayer wiring).

**Scope of this document:** `_sort` (single- and multi-key, capped at 3 keys this increment) compiled to keyset/seek-style pagination — a deliberate improvement over today's OFFSET-based continuation token, chosen because OFFSET has known correctness (page drift under concurrent writes) and performance (O(n) skip cost) problems that compound in the presence of sort — and its interaction with `_include`/`_revinclude` (Phase 7's own design doc named this fhir-server's densest bug cluster for the whole feature area and explicitly deferred it here as "a first-class design concern, not an afterthought"). This document went through two rounds of Fable adversarial research: a broad pass researching fhir-server's real sort/continuation mechanism and the real bug cluster, and a focused follow-up validating a specific alternative multi-key design (hand-traced against SQL Server NULL semantics) after the user pushed back on an initial recommendation to defer multi-key entirely.

---

## 1. Ground truth (verified, not assumed)

### 1.1 Live production sort/pagination — the parity bar to beat

`SqlEntityFrameworkSearchService.cs`'s `ApplySorting`/`ApplySort`/`ApplyThenBy` build real, working multi-key sort today via `OrderBy`/`ThenBy` chains, one **correlated scalar `MIN`/`MAX` subquery** per sort key against the relevant search-param table (`MIN` for ascending, `MAX` for descending — the "best" value for a multi-valued parameter), correlated by `ResourceSurrogateId` equality alone (no `ResourceTypeId` filter — surrogate ids are globally unique, confirmed by the Phase 8 part 1 Step 0 investigation and `IdHelper.cs`'s timestamp-derived global sequence). A final `.ThenBy(ResourceSurrogateId)` is always appended for determinism. Pagination is pure OFFSET: `Ignixa.Search.Models.ContinuationToken` is `{Offset, Count}` Base64 JSON with no binding to sort keys at all, decoded and applied via `Skip(offset).Take(pageSize)`.

**A real, live, three-way inconsistency exists in the fallback ordering**, confirmed by direct inspection: no `_sort` → `OrderBy(ResourceSurrogateId)` ascending; an unsupported **parameter type** reaching `ApplySort`'s `default` arm → `OrderByDescending(ResourceSurrogateId)` descending (the comment calls this "lenient," but it silently reverses direction); an unresolvable `SearchParamId` → falls back to ordering by `ResourceId` (a third, unrelated key). This is a real correctness gap, independent of this compiler project — flagged here for its own ticket, not fixed as part of this phase, and not perpetuated by the compiled path (§5).

**`$includes`'s own continuation mechanism, confirmed as a separate, wasteful pattern**: `IncludesResourceHandler.cs` decodes its own `IncludesContinuationToken` (also pure offset), then re-executes the *entire* search from scratch at `MaxItemCount * 10` (capped at 10,000), streams everything, discards the match entries it just re-fetched, and offset-skips the include entries in memory. A real, documented weakness of the current system — not something this phase reproduces, and not something this Core-tier project's scope extends to fixing directly (§7).

### 1.2 fhir-server's real mechanism — verified against source, not assumed

**Continuation token**: a positional JSON array — `[sortValue?, resourceTypeId?, surrogateId]` (`ContinuationToken.cs`). **Seek predicate** (`SqlQueryGenerator.HandleTableKindSort`, confirmed real): `((SortColumn = @val AND ResourceSurrogateId > @sid) OR SortColumn {>|<} @val)` — descending flips only the sort-value operand; the surrogate-id tie-break direction never flips, since the final `ORDER BY` is always `SortValue {ASC|DESC}, Sid ASC`. Both parameters are added with `includeInHash: false`, so page N and page N+1 share one cached plan — directly reinforcing this compiler's own "bound parameters, not literals, for anything user-derived" invariant.

**Two-phase missing-value segmentation, and why the seek predicate never needs a NULL-aware comparison**: resources lacking the sort parameter are a *separate query segment* (`NOT EXISTS`), not NULL rows mixed into the sorted set — ascending is missing-first, descending is valued-first-missing-last. The executor decides to transition phases by probing with `MaxItemCount = 1`; a sentinel token value marks the handoff. **This design's answer to "does a keyset seek ever need to be NULL-aware": no, because segmentation guarantees the valued segment contains no NULLs and the missing segment has no sort column at all** — verified as the correct, load-bearing property to preserve.

**Sort source**: write-time `IsMin`/`IsMax` flag columns (`WHERE IsMin = 1` for ascending, `IsMax = 1` for descending) — one flagged row per (resource, parameter), set by `ResourceWrapperFactory.ExtractMinAndMaxValues` at ingestion. No correlated subquery, no `GROUP BY` — the flag lets the seek predicate ride the `(SearchParamId, Text)` index directly. **Only String and Date search-parameter types are SQL-sortable in fhir-server, and — confirmed directly against `SqlServerSortingValidator.cs` — its SQL path supports at most one search-parameter-table sort key**, plus one special case: `_sort=_type,_lastUpdated` (both resource-table columns, same direction only). There is no fhir-server precedent for general multi-key sort over search-parameter tables — the design below has no reference implementation to port and is validated independently (§3.3).

### 1.3 The real bug cluster — verified against actual PRs, correcting Phase 7's citations

| # | What it actually is | Root cause |
|---|---|---|
| PR #5242 | Includes vanishing / 500s / lost includes-token, all under `_sort` | Sort-phase state and includes-continuation state were threaded independently through `SqlServerSearchService`, uncoordinated across the phase handoff. |
| PR #5297 | Infinite pagination loop, multiple includes + `_sort` | The reader derived continuation state assuming an ordering (`ResourceTypeId, ResourceSurrogateId`) the emitted SQL never actually stated. |
| PR #5362 | Skipped resources during token paging (regression from #5297) | `TOP` applied to an inner `SELECT` with **no `ORDER BY`** — SQL Server took arbitrary rows before the outer statement reordered them. |
| #1792/#1793 | Duplicate rows across sorted pages | Seek predicate on the sort value alone, no tie-break — the reason the two-clause OR-chain shape exists at all. |
| #2818 | SQL Server Error 8623 (plan-compilation failure) on sort + multi-include | Pathological plan size — the same failure class the compartment increment's Step 0 investigation already characterized. |
| #5672 (open) | SMART compartment-scoped search + `_sort` by a parameter → empty results | Directly on this project's compartment+SMART validation path (Phase 8 part 1). |

**Phase 7's §7 miscategorized #2950 (includes truncation-budget arithmetic, unrelated to sort) and #2382 (`:iterate` multi-target resolution, unrelated to sort) as part of this cluster — corrected here.** The real through-line across every genuine sort bug: state that should live in one place (the compiled plan) leaked across request boundaries or generation stages instead. Every recommendation below is built to make that leak structurally impossible, not merely "be careful."

### 1.4 Two new, real gaps found in Ignixa's live system while researching this phase

1. **`IsMin`/`IsMax` are never populated anywhere in this codebase.** The schema has the columns (`97.sql`), the row generators faithfully copy `ISupportSortSearchValue.IsMin`/`IsMax` into the TVPs, and `StringSearchValue`/`DateTimeSearchValue` implement the comparison interface — but nothing at write time ever sets the flags to `true` (confirmed by a repo-wide search: zero hits for `.IsMin = true` outside the fhir-server reference checkout). A compiled sort that naively filters `IsMin = 1` the way fhir-server does would return **zero rows against every real Ignixa database**. This is the same disease class as the previously-found never-called `CreateResourceSearchParamStats` sproc and the un-set composite-string overflow width — machinery ported from fhir-server, nothing left to own calling it. §3.2 resolves this for the compiled path without waiting for a write-side fix; the write-side fix + backfill is recorded as an independent, blocking-for-Phase-9-performance ticket (§7), not built here.
2. **`cteMatchPage`'s `TOP` has no `ORDER BY` today.** Harmless while nothing consumes ordering (the compiled path isn't wired to production yet), but it is precisely PR #5362's precondition: an unordered `TOP` selects an arbitrary page. §4 makes "every `TOP` this compiler emits is paired with an `ORDER BY` in the same `SELECT`" a stated `Emit` invariant, pinned by a golden test.

## 2. The keyset continuation token

Versioned, discriminated JSON, Base64-encoded (matching `ContinuationToken.cs`'s existing transport, widened):

```jsonc
{
  "v": 2,                         // schema version; exact match required, else the request fails closed (400)
  "s": "name,-birthdate",         // echo of the effective _sort -- integrity guard, see below
  "ph": "valued" | "missingPrimary",   // omitted when there is no sort (plain surrogate-id keyset)
  "k": ["1974-...", "..."],       // boundary values, POST-sentinel-substitution (§3.3) -- one per active key
  "t": 103,                       // boundary ResourceTypeId
  "sid": 5000000000000000123      // boundary ResourceSurrogateId -- always present, the ultimate tie-break
}
```

- **No sort**: `{v, t, sid}` only — the seek degenerates to `(T1 > @t) OR (T1 = @t AND Sid1 > @sid)`, and for a single-type query collapses further to `Sid1 > @sid`. `_lastUpdated`-only sort is this exact shape (§3.1).
- **`ph` arity rule**: in the `valued` phase, `k` carries one value per requested sort key (N values). In the `missingPrimary` phase, the primary key has no value by definition, so `k` carries N-1 values (the secondary keys only) — a token whose `k` length doesn't match its `ph`/`s` combination is invalid and fails closed.
- **`s` is a load-bearing integrity check, not a courtesy.** A client that changes `_sort` mid-paging today gets a seek predicate silently applied to the wrong column. The executor compares `s` against the request's effective sort and rejects a mismatch with 400, rather than mis-seeking. `s` also supplies each key's type (string vs. date, for value parsing) so `k[i]` can be decoded correctly.
- **Fail closed on anything unrecognized**: wrong `v`, missing required fields, unparseable `k[i]`. This is fhir-server's own behavior, and it incidentally closes an existing, separate Ignixa gap — today's `{Offset,Count}` decoder silently truncates on garbage input rather than rejecting it (an independent ticket, §7). A legacy `v`-less token (no sort ever compiled) can be recognized and routed to the frozen OFFSET path if Phase 9 chooses to keep it as a rollback lever; that policy choice belongs to Phase 9, not this document — this design's only obligation is that the two token shapes are always discriminable.
- **Boundary values (`k`, `t`, `sid`) always render as bound `SqlParameterRef`s, never inlined literals** — they are client-controlled input, and binding them lets page N and page N+1 share one query plan (matching fhir-server's `includeInHash: false`).

## 3. The IR

**No new `CteDefinition`.** Sort and paging are tier-3 result-shape fields on `QueryPlan`, synthesized by `Emit` at the page-selection site — the exact precedent Phase 7's `Includes`/`cteMatchPage` already set, and the original design doc's own tier table already named this destination ("Produce `IncludeStage`/`SortSpec`/`PageSpec`, never CTEs"). Keeping the match graph (`Ctes`/`Match`) permanently free of ordering concerns is what makes composition with `ChainJoin`/`CompartmentSource`/`Intersect`/`Union` require zero changes to any of them — fhir-server's counter-example (its sort decoration threads through the match pipeline via `IsInSortMode` state on the generator itself) is the direct, verified cause of its sort×chain bug (#2347) and a large share of the generator's own internal complexity.

```csharp
public enum SortKeyKind { String, Date, LastUpdated }

public sealed record SortKey(short? SearchParamId, SortKeyKind Kind, SortOrder Direction);
// SearchParamId is null only for Kind == LastUpdated (a resource-column key, no join needed -- §3.1).

public enum SortPhase { Valued, MissingPrimary }

public sealed record SortSpec(IReadOnlyList<SortKey> Keys, SortPhase Phase);
// Keys.Count is 1-3 this increment (Global Constraints); Phase applies to Keys[0] only (§1.2/§3.1).

public sealed record PageSpec(
    IReadOnlyList<SqlParameterRef> Boundary,     // post-sentinel-substitution; N values (Valued) or N-1 (MissingPrimary)
    SqlParameterRef BoundaryResourceTypeId,
    SqlParameterRef BoundarySurrogateId);

// QueryPlan gains two trailing optional fields (purely additive, matching every prior phase's precedent):
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null);
```

### 3.1 Emitted SQL — single key (`_sort=name`, ascending)

```sql
;WITH <existing ctes...>
SELECT TOP (11) m.T1, m.Sid1, sk0.SK0 AS SortValue0
FROM cte{Match} m
INNER JOIN (
    SELECT ResourceTypeId, ResourceSurrogateId, MIN(Text) AS SK0
    FROM dbo.StringSearchParam
    WHERE SearchParamId = 202
    GROUP BY ResourceTypeId, ResourceSurrogateId
) sk0 ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1
WHERE (sk0.SK0 = @p0 AND m.Sid1 > @p1) OR sk0.SK0 > @p0     -- only when Page is non-null
ORDER BY sk0.SK0 ASC, m.Sid1 ASC
```

`MIN`/`MAX` per direction (ascending/descending) — **query-time aggregation, not an `IsMin`/`IsMax` flag filter**, resolving §1.4's gap: an `INNER JOIN` against a `GROUP BY`-aggregated derived table (one row per resource that has the parameter at all) is both correct today and index-friendly enough for a primary key (it drives off `(SearchParamId, Text)` per the `INNER JOIN`'s own filter/group). Write-side `IsMin`/`IsMax` population is a documented future optimization (§7), not a blocker — it would let this same shape drop the `GROUP BY` in favor of `WHERE IsMin = 1`, an internal `Emit` change with no IR-shape impact. `_lastUpdated` as a sort key needs no join at all — it renders directly as `m.Sid1 {ASC|DESC}` (the compiler's own existing precedent already treats `_lastUpdated` as a derived function of the surrogate id, per the sixth increment's `ResourceColumnLoweringRule`), which is also exactly fhir-server's own "the surrogate id is the timestamp" reasoning for `_lastUpdated` sort.

**No-sort case**: the seek degenerates to `(m.T1 > @t) OR (m.T1 = @t AND m.Sid1 > @sid)` (or `Sid1 > @sid` alone for a single-scoped-type query), `ORDER BY m.T1, m.Sid1` — this is the existing no-includes/no-sort shape *plus* an `ORDER BY`, which §1.4 already established as a required `Emit` invariant regardless of whether sort is present, since an unordered `TOP` is `PR #5362`'s exact precondition.

### 3.2 Missing-primary phase

Same site, `INNER JOIN` replaced by `NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)`, `ORDER BY m.Sid1` (no primary sort column exists in this segment by construction), sid-only seek when there are no secondary keys. **Secondary keys still apply as tie-breakers within the missing-primary segment** — this is the more correct reading of FHIR's `_sort` semantics (sort keys are an independent priority list, not sub-keys of the primary), it costs nothing beyond the same `LEFT JOIN` machinery §3.3 already needs, and degenerating this segment to sid-only ordering would gratuitously diverge from that shared machinery for no benefit.

### 3.3 Multi-key sort — validated design, capped at 3 keys this increment

Two rounds of adversarial validation (broad research, then a focused hand-traced follow-up after the user pushed back on an initial recommendation to defer multi-key entirely) converged on: **phase-segment only the primary key** (exactly as §3.1/§3.2 — 2 segments total, never 2^N), and **treat every secondary key as an ordinary tie-breaker via `LEFT JOIN` plus SQL Server's standard "seek method" pattern for nullable columns**: `ISNULL(col, sentinel)` used identically in both `ORDER BY` and the seek predicate, so a secondary key's SQL `NULL` never has to survive into a raw `>`/`<` comparison (which is always false against `NULL` in T-SQL and would silently break seek resumption).

**Verified, not assumed**: SQL Server's `ORDER BY` places `NULL` first ascending, last descending — documented, deterministic behavior (Microsoft Learn), unaffected by `COLLATE` (which governs only non-`NULL` ordering) and identical for base, joined, or expression columns. This confirms the *default* `ORDER BY` behavior is already correct for a raw nullable column — but the seek predicate is not automatically correct alongside it (next paragraph), which is why the design still needs the `ISNULL` wrapper.

**The one correction this validation pass caught before it became a real bug**: the `ORDER BY` expression and the seek-predicate expression for a secondary key must be the **byte-identical** `ISNULL(col, sentinel)` text (same sentinel, same `COLLATE`, if any) on both sides — not "raw column in `ORDER BY`, sentinel only in the seek predicate," which was an earlier, incorrect framing of this design. Concretely: if the sentinel differs from raw-`NULL`'s ordering position even slightly, rows land in a different relative order between the `ORDER BY` clause and the seek predicate's tie boundary, and a row can fall into neither branch of the OR-chain — a silent, undetectable drop. This project's `Emit` architecture makes the fix free: one helper renders a key's expression once; both the `ORDER BY` term and the seek term call it, so the two can never drift. **This is a required `Emit` invariant, pinned by a golden-string test with a deliberate sentinel-tie fixture, not left to convention.**

**Sentinels**: String (`Text NVARCHAR(256)`) → `N''` (FHIR prohibits empty strings; the indexer never emits one; the residual collision — a value consisting solely of collation-ignorable characters — merely places that value at the tie boundary, which is safe, not a correctness bug, per the next paragraph). Date (`DATETIME2(7)`) → the type's minimum, `0001-01-01T00:00:00.0000000` (a real, if pathological, expressible FHIR date; same "safe placement, not corruption" property applies).

**Why a sentinel collision is cosmetic, not a correctness bug**: because the `ORDER BY` and seek expressions are required to be byte-identical (previous paragraph), a colliding real value and a truly-missing value tie at the sentinel and interleave — resolved deterministically by the next key/the final `(T1, Sid1)` tie-break — exactly as any other genuine tie would be. Pagination neither skips nor duplicates rows; the only effect is that the colliding row's relative position among other missing-value rows is arbitrary within the tie, which is already true of any tie on any key.

**Mixed ascending/descending across keys** (worked, hand-traced example, `_sort=name,-birthdate`, key0 = name ASC, key1 = birthdate DESC, sentinel `0001-01-01`):

```sql
WHERE (   sk0.SK0 > @p0
       OR (sk0.SK0 = @p0 AND ISNULL(sk1.SK1, '0001-01-01') < @p1)
       OR (sk0.SK0 = @p0 AND ISNULL(sk1.SK1, '0001-01-01') = @p1 AND m.T1 = @p2 AND m.Sid1 > @p3)
       OR (sk0.SK0 = @p0 AND ISNULL(sk1.SK1, '0001-01-01') = @p1 AND m.T1 > @p2))
ORDER BY sk0.SK0 ASC, ISNULL(sk1.SK1, '0001-01-01') DESC, m.T1 ASC, m.Sid1 ASC
```

Equality prefixes (`sk0.SK0 = @p0`) never flip with direction; only each level's own strict inequality does (`<` for the DESC `birthdate`, `>` for ASC `name`/`T1`/`Sid1`). This generalizes to N keys as the standard bounded lexicographic tuple-seek pattern — `N+1` OR-branches (one per key level, plus the final `(T1, Sid1)` composite tie-break), never exponential in `N`. Hand-traced against a concrete 4-row fixture during design validation and confirmed to select and paginate correctly, including a value crossing the sentinel boundary between pages.

**Cap of 3 keys this increment, as a policy guard, not an architectural limit.** The join list, `ORDER BY` term list, and seek-predicate OR-chain are the same loop for 2 keys as for 17 — the cap exists to bound per-request join cost, plan-shape risk (matching this project's own repeated "bounded, grouped-CTE discipline" response to Error-8623-class failures), and this increment's own golden-string test surface, not because the mechanism stops generalizing. `Lower` throws a named `NotSupportedException` beyond 3 keys, citing the cap explicitly — raising it later is a one-line change plus tests, not a redesign. **Key kinds supported this increment: String, Date, and `_lastUpdated`** — matching fhir-server's own supported search-parameter sort types plus the resource-column case that needs no join at all. Token/Number/Quantity/Reference/Uri sort throws a named `NotSupportedException`, deferred.

**A genuine, honest performance trade-off, stated plainly**: with 2+ keys sourced from different tables, no single index can satisfy the composite `ORDER BY`, so a `TOP`-`N` sort operator is unavoidable regardless of the `ISNULL` wrapper — multi-key sort forfeits the pure index-order streaming scan single-key sort gets. This still strictly beats the OFFSET parity bar (§1.1): the live executor sorts the *entire* matched set on every page and pays OFFSET's row-discard cost, and its token can silently skip or duplicate rows under concurrent writes, neither of which the keyset design permits. **`N = 1` (the overwhelmingly common real case) must render the pure single-key shape with none of the multi-key `LEFT JOIN`/`ISNULL` machinery** — the proven, cheap path is not taxed to support the general one.

## 4. Sort + includes — the interaction this whole document exists to get right

**`cteMatchPage` composes with sort by construction — the include machinery (`IncludeStage`, `SeedStages`, `EmitSeedExists`, the `incN`/`incNlim` pairs, the Kahn topological sort) needs zero changes.** `cteMatchPage`'s `SELECT` gains the sort-key joins, the seek `WHERE` clause, and an `ORDER BY` — but every include stage still seeds from `cteMatchPage`'s `(T1, Sid1)` columns exclusively (confirmed directly against `Emit.cs`'s `EmitSeedExists`, which references only `m.T1`/`m.Sid1`, never any other column). This is precisely the structural property whose *absence* caused fhir-server's #5242/#5297: its sort state (`IsInSortMode`, `SortValue`) threads through the mutable generator and every downstream CTE has to know about it. Here, the sort decoration lives entirely inside `cteMatchPage`'s own construction and stops there.

**Two `Emit` changes this phase makes, both scoped to the page-selection sites, neither touching `IncludeStage`:**

1. **A `CTE`'s own `ORDER BY` governs only which rows its `TOP` selects — it does not carry through to the statement that consumes the CTE.** So `cteMatchPage AS (SELECT TOP(n) ... ORDER BY ...)` correctly picks the right *rows*, but the final outer `SELECT` (the `UNION ALL` of match + include rows) needs its **own** `ORDER BY` to actually deliver them in order: `ORDER BY IsMatch DESC, <sort-key columns, NULL for include rows>, T1, Sid1` — extending the `IsMatch DESC` ordering Phase 7 already emits, not replacing it. When `Includes` is empty, there is no separate consuming statement — the plain `SELECT TOP(n) ... ORDER BY ...` is one self-contained statement and needs no second `ORDER BY`.
2. **The result-shape contract widens again**, following Phase 7's own precedent for the `(T1, Sid1, IsMatch, IsPartial)` shape: whenever `plan.Sort` is non-null, the projection gains one trailing `SortValueN` column per active key (`NULL` on include rows) so the executor can read the last *match* row's boundary values directly, without a second query, to mint the next continuation token. Documented on `EmittedSql`'s XML doc exactly as Phase 7 documented the 4-column include shape.

**One FHIR-semantics consequence to state explicitly, not silently discover later**: with an ascending sort, the *first* page of a sorted, include-bearing search seeds its includes from the **missing-primary-key** segment (phase order: missing-first ascending). This is fhir-server-conformant (includes always follow whatever the current match page actually is) and needs no special mechanism — it only needs to be named, so Phase 9's differential-test suite reads it as an intentional consequence of the phase model, not a bug.

**How this design avoids each real bug from §1.3, concretely**: #5362 (unordered `TOP`) is structurally unrepresentable — every `TOP` this compiler emits is required to carry an `ORDER BY` in the same `SELECT` (§1.4/§3.1), pinned by a golden test. #5297 (reader assumes an unstated ordering) is avoided because the compiled contract states the include ordering explicitly (`T1, Sid1`) rather than leaving it to clustered-index accident, and the next-token boundary is read only from `cteMatchPage`'s own, explicitly-ordered rows. #5242 (phase state and includes state uncoordinated across requests) is avoided because the phase is a **compiler input** (`SortSpec.Phase`), not ambient runtime state — a compiled plan for `(phase, boundary, includes)` is one deterministic, self-contained artifact; there is no code path where an include stage could observe a stale or mismatched phase, because the phase is part of the plan's own identity, not something mutated during generation. #1792 (duplicate rows) is avoided because the `(T1, Sid1)` tie-break is present in both the `ORDER BY` and the seek predicate by construction of `PageSpec` — never optional, never forgotten. #2347 (sort × chain) and #2818 (plan-compilation blowup) are avoided by the same "match graph never sees sort" property that avoids #5242, plus the bounded-CTE discipline Phase 8 part 1 already established. #5672 (compartment + sort, open in fhir-server) composes for free here: a compartment match root is just another `Union` of `CompartmentSource` — the sort decoration is root-agnostic.

## 5. `Resolve`/`SymbolCollectingVisitor` widening

`SortExpression` lives on `SearchOptions.Sort`, never inside `options.Expression` — confirmed, no `VisitSortParameter` override would ever fire, the same situation Phase 7's `IncludeExpression` was in (not Phase 6's `ChainedExpression`, which genuinely is part of the walked tree). `Resolve.RunAsync` gains a required `IReadOnlyList<SortExpression> sort` parameter (matching the `includes`/`revIncludes` precedent — an empty list for the common no-sort case, not an optional/defaulted parameter, since sort is exactly as fundamental to a search as includes are). A new `SymbolCollectingVisitor.CollectSort(SortExpression)` method (mirroring `CollectInclude`'s "not a visitor override" shape exactly) adds each non-`_lastUpdated` key's `SearchParameterInfo` to `Parameters` for the existing `SearchParamId` resolution loop — `_lastUpdated` needs no `SearchParamId` at all (§3.1). No new `SymbolTable` surface is needed; `SearchParamId(SearchParameterInfo)` already covers every key kind that needs one.

**One nullability inconsistency this phase must resolve while touching these signatures**: Phase 8 part 1 left `Resolve.RunAsync`'s `targetResourceType` non-nullable while widening `Lower.Run`'s to `string?` for the wildcard-compartment case (recorded as a Minor finding in that increment's final review). A sorted wildcard-compartment search would hit this immediately — align `Resolve.RunAsync`'s `targetResourceType` to `string?` in the same change, closing that carried-forward gap rather than adding a second one beside it. Sorting a wildcard compartment search (`targetResourceType is null`) throws a named `NotSupportedException` at `Lower` — a `SortSpec` needs a single `ResourceTypeId` scope for its joins, the same reasoning already established for typed leaves under a null scope (Phase 8 part 1 §4).

## 6. SMART/compartment boundary — unaffected, confirmed non-foreclosing

This phase adds no new predicate/filter surface — sort decorates *ordering*, never *membership*. The named Phase 9+ seats from Phases 6/7/8-part-1 (`OutputTypeIds`/a future `OutputScopeFilter`, the compartment `Union` `CteRef` as the compartment-membership CTE) are untouched by anything here; a future instance-level SMART filter composes with sort exactly as it would with any other `Intersect`/`Union`-shaped match root, since sort only ever reads `cteMatchPage`'s `(T1, Sid1)` pair, never the match graph's own construction.

## 7. Explicitly in scope / explicitly deferred

**In scope for this increment:**
- Single- and multi-key (capped at 3) `_sort`, String/Date/`_lastUpdated` key kinds, ascending/descending per key independently
- Keyset/seek continuation token (versioned, discriminated, fail-closed), replacing OFFSET semantics for any request that reaches the compiled path
- The two-phase (valued/missing-primary) segmentation, with secondary-key tie-breaking within each phase
- Sort composing with `_include`/`_revinclude` via `cteMatchPage`, with zero changes to `IncludeStage`
- The `Emit` "every `TOP` needs an `ORDER BY`" invariant (closes §1.4's live gap in the compiled path)
- Query-time `MIN`/`MAX` aggregation as the sort-source mechanism (works against real Ignixa data today, unlike a naive `IsMin`/`IsMax` port)

**Explicitly deferred, named so Phase 9 inherits them as known requirements, not surprises:**
- Write-time `IsMin`/`IsMax` population + backfill (§1.4) — an independent, blocking-for-Phase-9-*performance* ticket (not correctness — §3.1's aggregation is correct without it), structurally identical to the earlier `TextOverflow` reindex follow-up
- The live executor's three-way inconsistent fallback ordering (§1.1) and the legacy `{Offset,Count}` token's silent-truncation-on-garbage-input behavior — both real, both independent of this compiler
- Token/Number/Quantity/Reference/Uri sort types, and sort keys beyond the 3-key cap — named `NotSupportedException`s, not silent truncation
- The `$includes` operation's own re-execute-and-discard pattern (§1.1) — a real weakness, out of this Core-tier project's scope to fix directly; the seat this design leaves for a future fix (a pinned surrogate-id-range match-page boundary plus phase, matching fhir-server's own `IncludesContinuationToken` shape) is additive to `PageSpec`, not foreclosed
- Instance-level SMART/compartment filtering, and the `IncludeStage.Direction`/`Reversed` dual-source-of-truth risk — both already-named Phase 9 follow-ups from prior increments, untouched here
- The Phase 8 part 1 nested-`And` `ExtractResourceColumnPredicates` gap — a sorted compartment search combined with 2+ ordinary predicates inherits that same limitation; same resolution owner (Phase 9), not duplicated here
