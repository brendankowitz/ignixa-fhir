# Investigation: Current-State Audit of the SQL Search/Index Pipeline

**Feature**: sql-datalayer-architecture
**Status**: Complete
**Created**: 2026-07-11

## Approach

Direct read of `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/*`, `RowGenerators/*`, and the Core `Expression`/`IExpressionVisitor` infrastructure it consumes (`src/Core/Ignixa.Search/Expressions/`). Goal: establish, with citations, whether the SQL data layer is genuinely ad-hoc or just unfamiliar, and identify exactly where the pain is concentrated, so the follow-up investigation can target a fix instead of a rewrite.

## Findings

### 1. Pipeline shape today

`HTTP query string → ExpressionParser (Core) → Expression tree → SearchExpressionQueryBuilder.ApplySearchExpressionAsync → {SearchParameterQueryGenerator | ChainedExpressionProcessor | CompartmentSearchQueryGenerator | PatientEverythingQueryGenerator | ...} → IQueryable<long> of ResourceSurrogateId → EF Core → SQL Server`.

`SearchExpressionQueryBuilder.ApplySearchExpressionAsync` (`Search/SearchExpressionQueryBuilder.cs:80-92`) dispatches on the *concrete Expression subtype* using a C# `switch` expression with `is`-style type patterns — not `expression.AcceptVisitor(...)`. Core already defines a full double-dispatch visitor contract (`Expressions/IExpressionVisitor.cs`, `DefaultExpressionVisitor.cs`, `ExpressionRewriter.cs`), used correctly elsewhere in Core (e.g. `CompartmentSearchRewriter`, `DateTimeEqualityRewriter`), but the SQL backend does not implement it. Practical consequence: adding a new `Expression` subtype to Core does not force the SQL backend to handle it at compile time — it falls through to `throw new NotSupportedException(...)` (`SearchExpressionQueryBuilder.cs:91`) at request time instead.

### 2. Where the "compiler" is implicit vs. missing

There is no shared IR between read and write paths, and no single lowering boundary. Instead:

- **`SearchParameterQueryGenerator.cs` (2113 lines, largest file in the layer)** special-cases four resource-level parameters by magic string (`expression.Parameter?.Code == "_id"`, `"_lastUpdated"`, `"_ttl"`, `"_type"` — lines 77-97), each with its own hand-written `BinaryOperator → EF predicate` switch. The same six-case operator switch (`Equal/GreaterThan/GreaterThanOrEqual/LessThan/LessThanOrEqual/NotEqual`) is retyped nearly verbatim **nine times** in this one file: `ProcessResourceLastUpdatedExpressionAsync` (478-517), `ProcessResourceLastUpdatedMultiaryExpressionAsync` (552-591), `ProcessResourceTtlExpressionAsync` (629-680, two variants for binary vs. missing-modifier), `ProcessResourceTtlMultiaryExpressionAsync` (737-788), and `BuildSingleConditionDateTimeQuery` (1157-1190, once per field). None of these share a helper. (`grep -c "BinaryOperator.GreaterThanOrEqual =>"` → 9 hits in this file alone, 3 more in `CompositeSearchParameterQueryGenerator.cs`.)
- Generic (non-resource-level) search parameters go through `ProcessExpressionAsync` (939-980), which does its own `is` chain, then falls back to **reflection** for `InExpression<T>` (965-977: `exprType.GetGenericTypeDefinition() == typeof(InExpression<>)`, `GetProperty(...).GetValue(...)`) because there's no non-generic base usable in a normal type switch — reflection running in a per-request hot path.
- **`CompositeSearchParameterQueryGenerator.cs` (803 lines)**: `DetermineCompositeType` (46-113) is a hardcoded if-chain over ordered component-type tuples (Token|Token, Token|Quantity, Token|Date, Token|String, Reference|Token, Token|Number|Number). Adding a new composite shape means editing this method, adding an entity, and writing a migration — nothing here is table-driven.
- Each of `ChainedExpressionProcessor`, `IncludeProcessor`, `RevIncludeProcessor`, `CompartmentSearchQueryGenerator`, `PatientEverythingQueryGenerator` (165-380 lines each) independently implements "subtree → `IQueryable<long>` of matching resource ids" under a *different* method name and signature (`ProcessChainAsync`, `GenerateCompartmentQueryAsync`, `GeneratePatientEverythingQueryAsync`, `GenerateNotReferencedQueryAsync`, `GenerateQueryAsync`). `SearchExpressionQueryBuilder` wires each in by hand per `Expression` subtype rather than through one common interface, even though conceptually they all do the same job.

### 3. Ad-hoc smells, concretely

- **(a) Reflection instead of typed dispatch**: `SearchParameterQueryGenerator.cs:965-977` (see above).
- **(b) Duplicated knowledge between write and read paths**: `RowGenerators/TokenSearchParameterRowGenerator.cs` encodes two conventions on the write side — code truncation at 128 chars into a `CodeOverflow` column (100-109) and "empty system string ⇒ store `NULL`, matched via the `|code` pattern" (83-96). `CompositeSearchParameterQueryGenerator.ExtractTokenValuesFromSingle` (509-533) re-derives the identical "empty string means explicit no-system" rule independently on the read side. Neither references a shared constant or helper; the 128-char threshold and null-system convention are tribal knowledge, typed out twice, with no compiler-enforced link between the two copies.
- **(c) Type-specific special-casing that should be data-driven**: composite-type detection (`DetermineCompositeType`, above) and the four/six near-identical resource-level-parameter switches (finding 2).
- **(d) FHIRPath-over-JSON rule**: not applicable here — this layer operates on already-parsed `SearchIndexEntry`/`ISearchValue` and EF entities, not raw FHIR JSON, so the CLAUDE.md FHIRPath guidance doesn't apply to this code path directly.
- **(e) Layer isolation**: DataLayer is cleanly isolated behind `Ignixa.Search.Expressions`/`Ignixa.Search.Models` — Application does not appear to import SQL-specific types (not exhaustively verified, but no violations surfaced during this pass). The isolation *into* DataLayer is fine; the problem is entirely internal to DataLayer's own organization.
- **Structure loss requiring reverse-engineering**: `ExtractComponentExpressions` (`SearchParameterQueryGenerator.cs:205-295`) walks an already-lowered, field-level `Expression` tree, inspecting `IFieldExpression.ComponentIndex` and grouping by matching `HashSet<int?>` values, to reconstruct which sub-expressions belong to which composite component. That structure — component index, resolved `SearchParameterInfo` per component — existed at parse time and is discarded before reaching SQL, then heuristically rebuilt here. `GenerateReferenceTokenQueryAsync` (307-391) goes further: because DocumentReference's `relationship` composite has its component order swapped relative to the FHIR spec's usual Reference|Token ordering, the code runtime-*sniffs* which component is actually the reference vs. the token (`IsReferenceExpression`/`IsTokenExpression`, 318-346) rather than trusting positional/typed metadata. This is precisely the class of problem the referenced ms-fhir-server plan targets by preserving a semantic predicate leaf (parameter identity, comparator, component position) through to the backend instead of lowering to untyped fields early.

### 4. Comparison to the ms-fhir-server "SQL as compiler" plan

| Ms-fhir-server stage | Ignixa today |
|---|---|
| Semantic expression leaf (parameter + comparator + modifier + component position preserved) | **Absent.** Ignixa's `ExpressionParser`/`SearchParameterExpressionParser` (`src/Core/Ignixa.Search/Expressions/Parsers/`) lowers straight to field-level `BinaryExpression`/`StringExpression`, matching ms-fhir-server's *legacy* (pre-plan) shape. Composite component identity is thrown away and reconstructed downstream (finding 3). |
| Single compatibility lowering boundary | **Absent**, and arguably not yet needed — Ignixa doesn't have two representations to lower between yet. |
| Logical relational plan / normalization | **Absent.** |
| SQL catalog + canonical physical planner | **Informally present but not centralized.** The knowledge a catalog would hold (which table, which columns, which value-encoding rules per search-param type) exists, but it's spread across ~19 `RowGenerators/*` files and duplicated again across the `Search/*QueryGenerator` files rather than being one data-driven source both sides consult. |
| Memo optimizer / costing / plan cache | **Absent** — not clearly warranted yet; EF Core + SQL Server's own optimizer is doing this today. |
| Typed SQL AST / differential execution | **Absent** — queries are built as EF `IQueryable<T>` composition, not a typed SQL AST; EF's own LINQ-to-SQL translation is the closest analog. |

Ignixa is closest to reinventing ms-fhir-server's Plan 4 (catalog) badly — the facts a catalog should hold are present but duplicated, not centralized — while Plans 1-2 (semantic expression retention) and 3/5/6 (logical/physical/costed planning) don't exist at all, and are lower priority: Plan 1-2's absence is what's causing the composite-component reverse-engineering pain (finding 3); Plans 3/5/6 solve problems (cost-based plan selection, canary rollout) Ignixa hasn't reported having.

### 5. Migrations / schema evolution

Nine migrations from `20251104` to `20251230`, incrementally additive (background jobs, package/terminology indexes, terminology import tracking, TTL table, source events table, search-param extension columns) — normal, healthy schema evolution, no evidence of churn or patchwork in the migration history itself.

`PostMergeExtensionUpdater.cs` exists and implements exactly the pattern CLAUDE.md Section 5 documents as a deliberate, accepted tradeoff (nullable extension columns like `IdentifierType*` updated via EF `ExecuteSqlRawAsync` after `MergeResources` commits core data). This is **not** evidence of deeper ad-hoc-ness — it's a documented, intentional degraded-state design. It is, however, one more place (alongside the row generators and query generators) that has to know the same per-search-param-type column facts, reinforcing finding 3(b).

### 6. Test coverage

`test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/` contains dedicated unit tests for `ChainedExpressionProcessor`, `IncludeProcessor`, `IterateProcessor`, `RevIncludeProcessor`, `NotReferencedSearchParameter`, and `ReferenceSearchParameter`. **No test file targets `SearchParameterQueryGenerator` or `CompositeSearchParameterQueryGenerator` by name** — the two largest and most duplicated files in the layer (2113 and 803 lines respectively). Their coverage, if any, is presumably indirect via broader E2E search suites (`docs/features/e2e-testing/`), which was not confirmed in this pass. No golden-SQL / generated-query-shape tests were found anywhere in the layer.

**Practical implication**: a refactor of `SearchParameterQueryGenerator` or the composite generator today would have to rely on E2E tests to catch regressions — meaning failures surface late and broadly rather than locating the specific broken case. Any refactor phase should add focused unit tests for the behavior being extracted *before* extracting it (strangler-fig style), not after.

## Evidence

All findings above are drawn directly from file reads on 2026-07-11 against the `sql-datalayer-architecture` worktree: `Search/SearchExpressionQueryBuilder.cs`, `Search/SearchParameterQueryGenerator.cs` (partial — first 1200 of 2113 lines), `Search/CompositeSearchParameterQueryGenerator.cs` (full), `RowGenerators/TokenSearchParameterRowGenerator.cs` (full), and a `wc -l` / `grep -c` pass over `Search/*.cs` and `RowGenerators/*.cs`. `SqlEntityFrameworkSearchService.cs` (1329 lines), the remainder of `SearchParameterQueryGenerator.cs` (lines 1201-2113), and the other 18 `RowGenerators/*` files were **not** individually read in this pass — flagged as follow-up reads before any Phase 1 work begins (see staged-query-compiler investigation), since they may contain further instances of the same patterns.

Reference plan for the compiler-pipeline comparison: `docs/superpowers/plans/2026-07-10-fhir-search-semantic-expression-foundation.md` (ms-fhir-server / `fhir-server` repo, `brendankowitz-ideal-happiness` worktree).

## Verdict

The user's assessment is correct and specific, not vague: the SQL data layer isn't "messy" in a diffuse way — it has one concentrated failure mode (duplicated per-search-param-type knowledge with no shared IR, worst in `SearchParameterQueryGenerator.cs`) that shows up three times (operator-switch duplication, composite structure loss, read/write convention duplication). Core already provides the visitor infrastructure needed to fix the dispatch half of this; it's simply unused by SQL. This is a targeted, incremental refactor problem, not a rewrite problem — see [staged-query-compiler](staged-query-compiler.md).

## Addendum (2026-07-11): a fourth instance of the same failure mode, found during Phase 0 implementation

Phase 0 of the staged-query-compiler plan (characterization tests + mechanical dedup, now complete) surfaced a concrete, live example of exactly the risk this audit warned about — duplicated per-search-param-type knowledge silently drifting apart. During Task 3/4's dedup work, a Fable-led phase review found that **quantity/number comparator semantics differ between the single-parameter and composite code paths**:

- `SearchParameterQueryGenerator`'s single-parameter path (now `ComparisonPredicates.ApplyQuantityRangeComparison`): `ge → LowValue >= value`, `le → HighValue <= value` — the entire stored range must satisfy the comparison.
- `CompositeSearchParameterQueryGenerator.ApplyQuantityFilter` (~lines 681-688): `ge → HighValue >= value`, `le → LowValue <= value` — range-*overlap* semantics, explicitly documented as deliberate in that method's own comments.

These are genuinely different search results whenever a stored value's `LowValue != HighValue`, and FHIR's search-prefix semantics for implicit-precision ranges favor the composite path's overlap reading — meaning the single-parameter path is likely stricter than spec and can miss boundary matches today, in production, independent of anything this plan has done. Phase 0 preserved both behaviors exactly as found (correctly, given its zero-behavior-change mandate) — this is not a regression introduced by the cleanup work, it's a pre-existing divergence the cleanup work's own verification process (arm-by-arm diff review against characterization tests) happened to surface.

This is now the concrete "why" for Phase 2/3: a shared catalog or semantic predicate layer must make a *deliberate, spec-cited* choice about which semantics is correct, backed by characterization tests using `LowValue != HighValue` data, rather than mechanically unifying the two copies as if they'd always agreed. See `staged-query-compiler.md`'s Post-Plan section for the full finding.
