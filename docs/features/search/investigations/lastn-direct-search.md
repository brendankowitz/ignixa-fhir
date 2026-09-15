# Investigation: CTE-only Observation $lastn

**Feature**: search
**Status**: Prototype
**Created**: 2026-09-06

## Decision

This is a functionality-first alternative to
[the materialized implementation in PR #456](https://github.com/brendankowitz/ignixa-fhir/pull/456).
It retains the typed `LastNSearchOptions`, version-aware parser, terminal SQL
plan shape, and raw ADO.NET executor, but executes one read-only statement
against the existing search schema.

There are no added permanent or temporary tables, table variables, views,
stored procedures, indexes, schema versions, backfills, or write-path changes.
CTEs are query expressions, not deployed database objects. SQL Server may
nevertheless use its own internal worktables or spill to tempdb.

The scope matches the reference PR's library implementation: there is no
Application handler, HTTP route, Bundle materialization, or capability
advertisement. The legacy EF search implementation is unchanged.

## Query algorithm

1. The ordinary search compiler selects candidate resource identities,
   including filters, visibility, access constraints, and surrogate bounds.
2. `TokenSearchParam` provides every candidate's full coding identity:
   nullable `SystemId` plus `Code` concatenated with `CodeOverflow`. A dense
   numeric ordinal represents each identity using the index's case-sensitive
   collation.
3. A star rooted at each Observation's smallest code node connects all its
   translations. Bidirectional edges preserve transitive equivalence without
   constructing a clique for every multi-coded Observation.
4. A recursive CTE walks simple paths, tracking visited node ordinals to stop
   cycles. Only nodes greater than the walk's root need to be visited: the
   minimum node in a component can still reach every member. Each node's
   minimum reaching root identifies its component.
5. `TokenText` supplies case-sensitive fallback groups only for candidates
   without a code token. Coding display text does not add another group.
6. `DateTimeSearchParam.IsMax` supplies the effective start time. `RANK()`
   over each group retains the first `max` sorted positions and every
   Observation tied with the boundary. Neither `ROW_NUMBER()` nor
   `DENSE_RANK()` has these semantics.
7. The final result contains distinct `(T1, Sid1)` resource identities, ordered
   by group, rank, and descending surrogate id for deterministic ties.

**Equivalence is candidate-local.** A bridge excluded by search filters,
authorization, or default current/non-deleted visibility cannot merge groups.
This differs intentionally from #456's database-wide materialized graph.
Filtering can therefore change code equivalence as well as result membership.

## Input and result policies

`max` defaults to 1; the parser accepts 1 through 1000. R4 and later require
a successfully compiled patient/subject input plus category or a code-bearing
search parameter. The latter is classified through schema-aware FHIRPath
result types (`code`, `Coding`, or `CodeableConcept`), not parameter names.
STU3 retains its less restrictive required-input rules.

Ordinary `_sort`, `_count`, continuation, `_include`, and `_revinclude` controls
are rejected instead of silently changing the operation. Candidates default to
current, non-deleted resources; no implicit status restriction is added.

Observations without effective time sort after dated Observations and fill
remaining positions in descending surrogate-id order. They are not treated as
an unbounded tie, and `meta.lastUpdated` is not substituted. Candidates with
neither code tokens nor indexed code text do not form a group.

## Limitations

This is for small-dataset experiments, not a production latency commitment.
Recursive traversal can enumerate exponentially many simple paths in highly
connected graphs. CTEs are not guaranteed to be materialized, so SQL Server may
repeat expensive index scans and joins. `MAXRECURSION 0` avoids truncation at
SQL Server's default 100 levels; visited-path detection ensures finite walks.
The normal SQL command timeout and cancellation still apply, and failures
propagate rather than becoming partial or empty success responses.

The temporary-table prototype's and materialized implementation's benchmark
numbers do not measure this CTE-only query. No sub-100-ms or large-dataset
performance claim is made.

## Validation surfaces

- `Ignixa.Search.Tests`: required inputs, supported versions, `max`, custom
  code-bearing expressions, and result-control intent.
- `Ignixa.Search.Sql.Tests`: terminal-plan validation, parameterization,
  deterministic SQL, and ScriptDom inspection requiring a single read-only
  SELECT whose table references resolve to existing tables or local CTEs.
- `Ignixa.DataLayer.SqlServer.Tests`: explicit parameter types, tenant and
  cancellation forwarding, and refusal of ordinary searches.
- `Ignixa.DataLayer.SqlServer.IntegrationTests/LastNSqlSemanticsTests`: real
  baseline-schema deployment into a disposable database; direct executor
  coverage for transitive chains, cycles, more than 100 hops, duplicate
  codings, case/system identity, overflow, ties, missing dates, empty results,
  and excluded bridges.

## References

- [STU3 operation](https://hl7.org/fhir/STU3/operation-observation-lastn.html)
- [R4 narrative](https://hl7.org/fhir/R4/observation-operation-lastn.html)
- [R4B operation](https://hl7.org/fhir/R4B/operation-observation-lastn.html)
- [R5 operation](https://hl7.org/fhir/R5/operation-observation-lastn.html)
- [Search SQL decomposition](search-sql-decomposition.md)
