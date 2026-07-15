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
