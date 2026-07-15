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

