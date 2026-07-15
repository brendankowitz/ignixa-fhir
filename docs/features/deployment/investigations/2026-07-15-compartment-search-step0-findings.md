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
`searchParamMap` resolved by the real `CompartmentDefinitionManager`/`SearchParameterDefinitionManager` (23 distinct SearchParamId CTEs); all three arms returned 555000 rows.

| Arm | Cold (ms, DBCC FREEPROCCACHE) | Warm x3 (ms) | Warm avg (ms) |
|---|---|---|---|
| A - production `CompartmentSearchQueryGenerator`, unmodified | 1422 | 1195, 939, 883 | 1005.7 |
| B - Arm A + `SearchParamId` literalized via `EF.Constant` | 1074 | 972, 931, 947 | 950.0 |
| C - legacy SQL shape (raw ADO.NET, `SearchParamId` as SQL literal) | 1133 | 930, 912, 888 | 910.0 |

Raw warm-run detail:
- Arm A warm: 1195, 939, 883 (avg 1005.7)
- Arm B warm: 972, 931, 947 (avg 950.0)
- Arm C warm: 930, 912, 888 (avg 910.0)

