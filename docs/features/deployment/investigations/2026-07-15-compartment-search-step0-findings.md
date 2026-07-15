# Investigation: Compartment Search Step 0 — Is the Motivating Bug Still Live?

**Date:** 2026-07-15
**Status:** Complete

## Question

`docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md` names `CompartmentSearchProblem.txt`
as its motivating bug: Ignixa's EF-generated compartment query times out where hand-written SQL doesn't.
Does that gap still exist on `feature/fhir-to-sql-compiler` today?

## Finding

No — not in the form the design doc describes. `CompartmentSearchQueryGenerator.cs` (introduced in the
same commit as `CompartmentSearchProblem.txt`, `38a979df`) is unconditionally used for every compartment
search today (`SearchCompartmentHandler.cs:19-27`, `SearchExpressionQueryBuilder.cs:85`), including the
wildcard case the `.txt` file captures. It already batches by `SearchParamId`, `UNION`s per-parameter
queries instead of nesting them, drops the `Resource` table join, and forces `ResourceTypeId` lists to
inline via `EF.Constant()` to avoid EF Core 9+'s `OPENJSON` parameterization.

The one thing it does **not** do that the legacy hand-written SQL in `CompartmentSearchProblem.txt` does:
literalize `SearchParamId` itself (`CompartmentSearchQueryGenerator.cs:182` is a captured/sniffable
parameter, not `EF.Constant`).

## Consequence

The design doc's four-arm factorial, as originally scoped, tests a baseline ("naive EF") that is no
longer reachable in production. The real open question is narrower: **does literalizing `SearchParamId`
close whatever gap remains between today's `CompartmentSearchQueryGenerator` and the known-good legacy
SQL, at realistic data scale and skew?** That's what the rest of this plan measures.

## Task 4: Three-Arm Timing Comparison (real Patient-compartment associations)

Ran 2026-07-15 17:02:08 UTC against `CompartmentStep0`, compartment `step0-patient`.
`searchParamMap` resolved by the real `CompartmentDefinitionManager`/`SearchParameterDefinitionManager` (23 distinct SearchParamId CTEs); all three arms returned 555000 rows (vs. 576,800 raw seeded rows — see "Why 555,000, not 576,800" below).

| Arm | Cold (ms, DBCC FREEPROCCACHE) | Warm x3 (ms) | Warm avg (ms) |
|---|---|---|---|
| A - production `CompartmentSearchQueryGenerator`, unmodified | 1422 | 1195, 939, 883 | 1005.7 |
| B - Arm A + `SearchParamId` literalized via `EF.Constant` | 1074 | 972, 931, 947 | 950.0 |
| C - legacy SQL shape (raw ADO.NET, `SearchParamId` as SQL literal) | 1133 | 930, 912, 888 | 910.0 |

Raw warm-run detail:
- Arm A warm: 1195, 939, 883 (avg 1005.7)
- Arm B warm: 972, 931, 947 (avg 950.0)
- Arm C warm: 930, 912, 888 (avg 910.0)

### Caveats

**Why 555,000, not 576,800 (seeding artifact, not a production characteristic).** All three arms
returned 555,000 rows against 576,800 raw seeded `ReferenceSearchParam` rows. This is a **test-seeding
artifact**, not a characteristic of the production `CompartmentSearchQueryGenerator` code. In real
production, `ResourceSurrogateId` is allocated from a global per-transaction counter —
`surrogateId = transactionId + entryIndex` in `BuildResourceSurrogateIdMap`
(`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlMergeRepository.cs:446-460`) — which is
effectively globally unique across all resource types, so numeric collisions between different resource
types' surrogate IDs essentially never happen in real production data. The test seeder, however,
allocates each resource type's surrogate IDs from its own independent range:
`CompartmentDataSeeder.GetNextSurrogateIdBaseAsync`
(`test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompartmentDataSeeder.cs:362-370`) computes
each resource type's next surrogate ID as `MAX(ResourceSurrogateId) + 1` scoped to that `ResourceTypeId`
alone, so every seeded resource type's surrogate IDs start near 1 and their numeric ranges overlap with
every other seeded resource type's. The final `SELECT` in all three arms (production code included)
projects bare `ResourceSurrogateId` without `ResourceTypeId`
(`CompartmentSearchQueryGenerator.cs:181-185`), and `UNION`/LINQ `.Union()` deduplicate on that bare
value — so rows whose surrogate IDs happen to collide numerically across resource types collapse
together. That collision only happens because of how this test's seeder allocates IDs, not because of
anything in the query generator itself. It does not affect cross-arm comparability — all three arms union
the same seeded rows through the same bare-`ResourceSurrogateId` shape, so they're affected identically
and the timing numbers above remain valid — but the dedup is a seeding artifact, not a preexisting
production characteristic, and should not be described as one.

**Arm C measures shape, not the legacy query's exact projection.** Arm C is described above (and in the
plan) as testing "the legacy SQL shape" / the "known-good legacy SQL." Its `SELECT`, however, projects
bare `ResourceSurrogateId` only (`RunArmCAsync` in
`test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompartmentSearchStep0Benchmark.cs`), matching
Arms A/B's projection — not the real legacy query's composite
`SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1` projection
(`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/CompartmentSearchProblem.txt:13`). This is a
defensible, intentional simplification — Arm C exists "to confirm there's no floor Arms A/B still
haven't reached, not to reintroduce the 84-CTE literal text" (plan Task 4) — but it was not previously
disclosed as a deviation. To be explicit: Arm C measures the legacy query's CTE-per-`SearchParamId`,
literal-`SearchParamId` *shape*, not its exact original projection. It uses the same
bare-`ResourceSurrogateId` projection as Arms A/B for apples-to-apples comparability with them, not the
legacy query's original composite-key `SELECT`.

**Open question — Arm A's timed search-param resolution is not isolated.** Arm A calls
`CompartmentSearchQueryGenerator.GenerateCompartmentQueryAsync` fresh on every timed cold/warm
invocation, so its own internal search-param-map resolution (looping over ~93 resource-type/code pairs,
with `SearchIndexReferenceDataCache` lookups) runs inside every timed measurement — this is correct, since
it's what production does on every real request. Arms B and C, by contrast, are handed a pre-built
`searchParamMap` (built once via `BuildRealSearchParamMapAsync`) and skip this resolution step entirely
inside their timed portion. This asymmetry was never measured or isolated in this run. It's judged unlikely
to explain more than a small fraction of the Arm-A-vs-B/C timing gap — the cache is warm by the time of
measurement (pre-populated by an earlier, untimed call to the same map-builder), so the resolution loop is
CPU-bound over already-cached lookups rather than fresh DB round-trips — but that is an expectation, not
something this experiment measured directly. A reader should treat this as an open question: do not
attribute 100% of the Arm-A-vs-B gap to `SearchParamId` literalization alone; some unmeasured portion may
be attributable to this resolution-loop asymmetry instead.

## Results

| Arm | Cold (ms) | Warm avg (ms) |
|---|---|---|
| A — production `CompartmentSearchQueryGenerator`, unmodified | 1422 | 1005.7 |
| B — Arm A + `SearchParamId` literalized via `EF.Constant` | 1074 | 950.0 |
| C — legacy SQL shape, `SearchParamId` literal | 1133 | 910.0 |

## Conclusion

**Arm B ≈ Arm C. Literalizing `SearchParamId` closes the residual gap.** Warm, Arm B (950.0ms) is 40ms above Arm C
(910.0ms) — a 4% residual. Cold, the order inverts outright: Arm B (1074ms) beats Arm C (1133ms) by 59ms.
A gap that flips sign between the cold and warm measurement isn't a stable, attributable shape effect —
it's within run-to-run noise. Arm B and Arm C are the same result within measurement error. Both close the
overwhelming majority of the distance from Arm A: cold, A is 289-348ms (20-24%) slower than B/C (289ms/20.3%
vs. C, 348ms/24.5% vs. B, both relative to Arm A's 1422ms); warm, A is 56-96ms (6-10%) slower than B/C
(56ms/5.6% vs. B, 96ms/9.5% vs. C, both relative to Arm A's 1005.7ms).

Per the design doc's own instruction, the honest conclusion is that the compartment case should stop being
this document's headline. A single change — literalize `SearchParamId` via `EF.Constant` in
`CompartmentSearchQueryGenerator.cs:182`, the same treatment already applied to `ResourceTypeId` two lines
below it — recovers essentially all of the measured gap to hand-written SQL. That is a standalone bug-fix
PR against one file, not a justification for a multi-week compiler project. Recommend filing it
independently of this roadmap, not as a Phase 1 task.

**A finding not asked for by the plan, but too load-bearing to leave out:** the three arms did not "come
close" to a 180-second timeout, because there was never a 180-second timeout to come close to.
`CompartmentSearchProblem.txt:855-856` reads `OPTION (RECOMPILE)` / `-- execution timeout = 180 sec.`, but
per the design doc's own analysis
(`docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md`, "The confound: the working query is
not only a different shape"), that text is diagnostic comment `SqlServerSearchService.cs:1723-1726` appends
to a logging `StringBuilder` — so a human can paste it into SSMS for a sniffed plan — and it was never sent
to SQL Server as part of the executed command. What the artifact actually records is a **compile-time
plan-generation failure**, not a runtime timeout: `Microsoft.Data.SqlClient.SqlException`, SQL Server
**Error 8623** — "The query processor ran out of internal resources and could not produce a query plan...
only expected for extremely complex queries or queries that reference a very large number of tables or
partitions" (`CompartmentSearchProblem.txt:3232`, `Error Number:8623,State:1,Class:16` at `:3265`) — thrown
after the request had already run for **~7.8 seconds**
(`Request finished HTTP/1.1 GET ... 7801.8557ms`, `:3289`), not 180. All three arms here completed in under
1.5s cold and under 1.1s warm, at 555,000 result rows with one `SearchParamId` carrying 550,000 of them —
the same skew profile the original bug report describes — and none of them threw 8623 or anything like it.
Today's production `CompartmentSearchQueryGenerator`, unmodified (Arm A), does not reproduce the motivating
failure at all at this scale.

The better-evidenced explanation for that non-reproduction is structural, not data scale/skew, and it is
directly measurable in the same artifact. `CompartmentSearchProblem.txt` never mentions
`CompartmentSearchQueryGenerator` — the query that actually threw 8623 was built entirely by the older,
per-branch `SearchParameterQueryGenerator` (166 "Generating query for search parameter" log lines between
`:874` and `:3226`), which does not deduplicate by `SearchParamId`: it nests one subquery per (resource
type, code) branch of the compartment's OR-of-AND expansion, producing roughly 422 nested table references
(100 `ReferenceSearchParam` + 256 `Resource` + 66 `TokenSearchParam` joins, by direct count) even though
only 23 distinct codes underlie those 166 branches — the same count of distinct `SearchParamId`s this
benchmark's `searchParamMap` resolved for the same compartment (Task 4, above). That is exactly the "very
large number of tables" Error 8623 names. Today's `CompartmentSearchQueryGenerator` — the code Arm A
exercises unmodified, and the shape Arms B/C match — already dedupes to one CTE per distinct `SearchParamId`
and `UNION`s them (23 CTEs here) instead of nesting, per the Finding at the top of this document. That
dedup-and-`UNION` rewrite, shipped in commit `38a979df` before this benchmark ran, is a structural fix for
precisely the defect Error 8623 describes — a more directly evidenced explanation for why none of the three
arms reproduced any failure than an untested data-scale/skew hypothesis, though the latter remains a live,
unfalsified possibility this experiment didn't reach either. Either way, the design doc's framing —
"Ignixa's EF-generated compartment query times out where hand-written SQL doesn't" — is not currently true
of the code as it stands: there was never a 180-second timeout to begin with, and the compile-time failure
that did occur has a shipped structural fix already in place. That fact should carry equal weight with the
timing numbers above when anyone reads this as a motivator for Phase 1.

This conclusion is qualified, not undermined, by the three caveats above:
- The 555,000-vs-576,800 row discrepancy is a test-seeder artifact (surrogate ID ranges colliding across
  resource types in the seeder, not in production). It affects all three arms identically, so it doesn't
  change the cross-arm comparison.
- Arm C tests the legacy query's CTE-per-literal-`SearchParamId` *shape*, not its original composite
  `ResourceTypeId`+`ResourceSurrogateId` projection. "Arm B ≈ Arm C" should be read as "literalization
  closes the gap to a shape-matched legacy baseline," not as an exact reproduction of the original
  hand-written query's real-world performance.
- Arm A's per-call search-param-map resolution runs inside its timed measurement while B/C skip it
  (pre-built map). This was not isolated, so some unmeasured slice of the A-vs-B/C gap could be
  attributable to that asymmetry rather than to literalization alone — judged unlikely to be large (warm
  cache, CPU-bound lookups), but not measured directly.

**Recommendation:** ship the one-line `SearchParamId` literalization fix independently of this roadmap. Do
not use compartment search as the motivating case for Phase 1 of the FHIR-to-SQL compiler. If Phase 1
proceeds, its justification rests on the storage-convention-ownership, testability, and
injection-safety-by-construction arguments made elsewhere in the design doc — not on a performance gap a
one-line change already closes, and not on a 180-second timeout that never existed in the first place (the
real failure — SQL Server Error 8623, a plan-compilation failure — already has a shipped structural fix in
`CompartmentSearchQueryGenerator`, which is why this experiment could not reproduce it).

