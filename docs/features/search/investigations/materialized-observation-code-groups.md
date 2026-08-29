# Investigation: Materialized Observation Code Groups

**Feature**: search
**Status**: Pending evaluation
**Created**: 2026-08-28

## Problem Statement

FHIR `Observation/$lastn` returns the newest observations within each
equivalent `Observation.code` group. Code equivalence is transitive: codings
that occur on one Observation translate one another, so an `a`/`b` Observation
and a `b`/`c` Observation place `a`, `b`, and `c` in one group. The result also
includes every Observation tied at the requested effective-time boundary.

The direct `$lastn` compiler currently constructs that graph after candidate
filtering. Its exact, bounded component-label prototype passed semantic tests
but did not meet the latency requirement. This investigation defines a
write-maintained, direct-SQL materialization that moves graph work from reads
to the resource-indexing boundary. It does not add `$lastn` behavior to
`SqlEntityFrameworkSearchService`.

## Current Architecture and Boundary Gap

`LastNSearchOptionsBuilder` and `LastNSearchOptions` make `$lastn` an
operation-specific `Ignixa.Search` input. `Ignixa.Search.Sql` resolves its
implicit code and effective-date parameters, lowers a closed
`ResultShape.LastN`, and `LastNEmitter` currently builds temporary candidate,
membership, node, and edge tables before applying `RANK()`.

The direct SQL Server schema already persists the raw index inputs:

- `TokenSearchParam` stores code system, the case-sensitive 256-character
  `Code` prefix, and case-sensitive `CodeOverflow`.
- `TokenText` stores a token's fallback text, but uses a case-insensitive
  collation and therefore needs an explicit case-sensitive comparison for
  `$lastn`.
- `DateTimeSearchParam` stores effective-date ranges; `IsMax = 1` identifies
  the row used for the existing effective-time ordering.
- `Resource` identifies the current, non-deleted resource by
  `(ResourceTypeId, ResourceSurrogateId)`.

There is no direct writer that owns both a resource-index change and an
Observation-code graph update. `MergeResources` and
`UpdateResourceSearchParams` replace search-index rows; `HardDeleteResource`
removes them. The existing EF merge repository calls `MergeResources` and
then performs best-effort extension-column updates outside that procedure.
That extension pattern cannot maintain `$lastn`: a successful resource write
with a missing graph update would make a read silently wrong.

The direct `Ignixa.DataLayer.SqlServer` library supplies tenant-scoped raw
ADO.NET execution and deploys the SSDT database project through
`SchemaDeployer`, but it does not yet expose a direct resource-index writer.
The missing boundary is therefore a transaction-owning direct writer façade
and its wrapper procedures, not another branch in the legacy EF search
service.

## Decision Under Evaluation

Maintain one exact, current graph for every enabled
`(ResourceTypeId, CodeSearchParamId)` scope:

1. code identities;
2. current Observation-to-code memberships;
3. reference-counted unordered co-occurrence edges;
4. the minimum identity id as each code identity's component label; and
5. one current Observation-to-group row.

Only current, non-deleted Observations contribute. Component labels are
internal implementation values and may change when an edge removal splits a
component. `$lastn` is unavailable for a scope until its materialization
generation is `Ready`; it must never fall back to the rejected query-time
graph.

## Considered Approaches

| Approach | Result | Rationale |
|----------|--------|-----------|
| Query-time connected components | Rejected for production | It exactly models translations, but every request derives and joins the graph. The benchmark missed the latency target by a large margin. |
| One selected coding or `PARTITION BY SystemId, Code` | Rejected | It loses translations and transitive equivalence, duplicates results, or splits one FHIR code group. |
| Persist every root-to-node reachability relation | Rejected | Exact but can require O(V²) rows per component and makes edge-removal repair needlessly expensive. |
| Elasticsearch-style side index | Rejected | Adds another consistency boundary and does not provide an existing compliant implementation to adopt. |
| Reference-counted direct-SQL graph materialization | Selected for evaluation | Stores O(V + E + M) state, preserves every distinct same-Observation edge once, supports deletion, and leaves the read path as indexed grouping and ranking. |

Reference counts are required because more than one Observation can support the
same edge. Removing one Observation decrements its distinct edges but must not
disconnect codes while another Observation still supports the translation.

## Proposed Schema

The SSDT project adds the following additive tables. `ResourceTypeId` and
`SearchParamId` define the materialization scope. They are retained on every
table even when `CodeIdentityId` is globally unique, so all joins and locks
make the isolation boundary explicit.

### Code identity and graph

| Table | Columns and key | Indexes and invariant |
|-------|-----------------|-----------------------|
| `LastNCodeIdentity` | `CodeIdentityId BIGINT IDENTITY NOT NULL` primary key; `ResourceTypeId SMALLINT NOT NULL`; `SearchParamId SMALLINT NOT NULL`; `SystemId INT NULL`; `Code VARCHAR(256) COLLATE Latin1_General_100_CS_AS NOT NULL`; `CodeOverflow VARCHAR(MAX) COLLATE Latin1_General_100_CS_AS NULL`; `CodeHash BINARY(32) NOT NULL`; `ComponentCodeIdentityId BIGINT NOT NULL` | `UX_LastNCodeIdentity_Id_Scope` on `(CodeIdentityId, ResourceTypeId, SearchParamId)` supports scoped foreign keys. `IX_LastNCodeIdentity_Lookup` on `(ResourceTypeId, SearchParamId, CodeHash)` includes `SystemId`, `Code`, and `CodeOverflow`; the procedure performs full equality after the hash seek. `IX_LastNCodeIdentity_Component` on `(ResourceTypeId, SearchParamId, ComponentCodeIdentityId, CodeIdentityId)` finds a component. |
| `LastNObservationCodeMembership` | `ResourceTypeId SMALLINT NOT NULL`; `SearchParamId SMALLINT NOT NULL`; `ResourceSurrogateId BIGINT NOT NULL`; `CodeIdentityId BIGINT NOT NULL`; clustered primary key `(ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)` | `IX_LastNObservationCodeMembership_Code` on `(ResourceTypeId, SearchParamId, CodeIdentityId, ResourceSurrogateId)` finds Observations affected by a component repair. A scoped foreign key references `LastNCodeIdentity`. |
| `LastNCodeEdge` | `ResourceTypeId SMALLINT NOT NULL`; `SearchParamId SMALLINT NOT NULL`; `LeftCodeIdentityId BIGINT NOT NULL`; `RightCodeIdentityId BIGINT NOT NULL`; `SupportCount INT NOT NULL`; clustered primary key `(ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId)` | A check requires `LeftCodeIdentityId < RightCodeIdentityId` and `SupportCount > 0`. `IX_LastNCodeEdge_Right` on `(ResourceTypeId, SearchParamId, RightCodeIdentityId, LeftCodeIdentityId)` makes either endpoint seekable. Scoped foreign keys reference code identities. |

`CodeHash` is `SHA2_256` over an unambiguous binary serialization of the
nullness and value of `SystemId`, followed by the bytes and lengths of `Code`
and `CodeOverflow`. It reduces candidate lookup work only. Identity equality
is always the null-safe comparison below under
`Latin1_General_100_CS_AS`; a hash collision cannot merge identities:

```text
same identity =
    same ResourceTypeId
    and same SearchParamId
    and (both SystemId values are null or they are equal)
    and same Code under Latin1_General_100_CS_AS
    and (both CodeOverflow values are null or they are equal under
         Latin1_General_100_CS_AS)
```

`ComponentCodeIdentityId` is the smallest `CodeIdentityId` in the connected
component. It initially equals `CodeIdentityId`; it is not a durable external
identifier.

### Current group and generation state

| Table | Columns and key | Indexes and invariant |
|-------|-----------------|-----------------------|
| `LastNObservationCodeGroup` | `ResourceTypeId SMALLINT NOT NULL`; `SearchParamId SMALLINT NOT NULL`; `ResourceSurrogateId BIGINT NOT NULL`; `GroupKind TINYINT NOT NULL`; `CodeGroupId BIGINT NULL`; `TextCode NVARCHAR(400) COLLATE Latin1_General_100_CS_AS NULL`; clustered primary key `(ResourceTypeId, SearchParamId, ResourceSurrogateId)` | A check permits exactly one representation: coded (`GroupKind = 0`, non-null `CodeGroupId`, null `TextCode`) or text-only (`GroupKind = 1`, null `CodeGroupId`, non-null `TextCode`). `IX_LastNObservationCodeGroup_Rank` on `(ResourceTypeId, SearchParamId, GroupKind, CodeGroupId, TextCode, ResourceSurrogateId)` supports the candidate join and rank partition. |
| `LastNCodeGroupGeneration` | `ResourceTypeId SMALLINT NOT NULL`; `SearchParamId SMALLINT NOT NULL`; `Generation BIGINT NOT NULL`; `State VARCHAR(16) NOT NULL`; `SnapshotHighWaterSurrogateId BIGINT NULL`; `StartedDateTime DATETIME2(7) NOT NULL`; `CompletedDateTime DATETIME2(7) NULL`; `FailureReason VARCHAR(1000) NULL`; primary key `(ResourceTypeId, SearchParamId)` | A check limits state to `Pending`, `Building`, `Ready`, or `Failed`. `Generation` increments for each rebuild. Only a `Ready` row admits `$lastn`. |
| `LastNCodeGroupDirtyObservation` | `ResourceTypeId SMALLINT NOT NULL`; `SearchParamId SMALLINT NOT NULL`; `Generation BIGINT NOT NULL`; `ResourceSurrogateId BIGINT NOT NULL`; primary key `(ResourceTypeId, SearchParamId, Generation, ResourceSurrogateId)` | A write during `Building` upserts the current resource id once. The final replay consumes rows only for the active generation. |

Text-only code values never join the coding graph. When a current Observation
has no `TokenSearchParam` row for the code parameter, its `TokenText` value is
materialized as `GroupKind = 1` using the explicit case-sensitive collation.
This preserves the current compiler's text-only branch without treating a
display string as a coding translation.

## Write Algorithms

All algorithms execute per locked scope and derive their final state from
current index rows. Consequently replaying a completed or interrupted request
is idempotent.

### Remove an Observation's old contribution

For every affected current or previous surrogate id:

1. Read its `LastNObservationCodeMembership` rows and their prior component
   labels into a work table.
2. Delete its `LastNObservationCodeGroup` row and membership rows.
3. Form each distinct unordered pair of its code identities once; decrement
   the matching edge's `SupportCount` once. Delete edges that reach zero.
4. Record every old component label and every endpoint touched as affected.

The procedure does not delete an identity with no memberships. Keeping an
orphan identity makes retries deterministic and lets a later identical coding
reuse its stable identity id. A separate, explicitly scheduled retention task
may prune orphan identities only after proving no membership or edge references
them.

### Add an Observation's new contribution

For each current, non-deleted Observation after its base index rows are
written:

1. Select distinct `TokenSearchParam` identities for the configured code
   parameter. Seek by `CodeHash`, then apply full null-safe equality; under the
   scope lock insert any missing identity with its own id as the component
   label.
2. Insert one membership per distinct identity.
3. Generate pairs only where `LeftCodeIdentityId < RightCodeIdentityId`.
   `MERGE` is not used: an update-first, insert-if-absent sequence increments
   `SupportCount` exactly once per distinct pair.
4. If no coding membership exists, read the single current `TokenText` row,
   explicitly collate its text case-sensitively, and insert the text-only
   group row. If neither source exists, insert no group row.
5. Record identities, old labels, and newly joined labels as affected.

Every coded Observation's memberships are connected by its generated pairs, so
all of its coded identities acquire one component label. The group row stores
that label after component repair.

### Merge, update, delete, and reindex

Wrapper procedures, rather than changes to `MergeResources` or any existing
TVP schema, own the materialization transaction:

- `MergeResourcesAndMaintainLastNGroups` determines configured scopes for the
  incoming resources, collects the prior current surrogate ids, removes their
  contributions, invokes `MergeResources` inside the outer transaction, adds
  the newly current contributions, repairs components, and commits.
- `UpdateResourceSearchParamsAndMaintainLastNGroups` performs the same
  remove/base-update/add/repair sequence around
  `UpdateResourceSearchParams`. Reindex callers use this wrapper, so a
  changed code index cannot leave an old group behind.
- `HardDeleteResourceAndMaintainLastNGroups` removes the current contribution
  before invoking `HardDeleteResource` when `KeepCurrentVersion = 0`. A
  history-only delete does not alter current group state.

The direct writer façade is the only application caller of these wrappers.
Its methods accept the existing typed resource/index inputs and `CancellationToken`;
it does not expose an API for independently mutating graph tables. Legacy EF
callers remain unchanged until production serving is deliberately migrated.

### Component repair and splits

After the batch's removals and additions, the procedure expands the affected
set to every identity in every old or newly joined component. It traverses the
remaining undirected `LastNCodeEdge` rows within that set and repeatedly
propagates the minimum reachable `CodeIdentityId` until no label changes.
Isolated identities retain their own id. It then:

1. updates `ComponentCodeIdentityId` for every identity in the affected set;
2. finds all coded Observation memberships for those identities; and
3. replaces their `LastNObservationCodeGroup` rows with the repaired label.

An edge addition may merge components; removal of the last supporting edge
recomputes the entire formerly connected affected component, so it correctly
creates two or more labels after a split. A full-scope rebuild is reserved for
generation finalization and repair tooling, not the normal write path.

## Transactions, Locks, and Failure Semantics

The wrapper begins a SQL transaction before calling the existing base
procedure. `MergeResources` observes the outer transaction and does not commit
it; the wrapper commits only after base indexing and group maintenance both
succeed. It preserves the existing `MergeResources` procedure and TVP
contracts exactly.

For the initial correctness implementation, each wrapper:

1. derives affected `(ResourceTypeId, SearchParamId)` scopes;
2. sorts them lexicographically;
3. acquires `sp_getapplock` with resource name
   `LastNCodeGroup:{ResourceTypeId}:{SearchParamId}`, lock mode
   `Exclusive`, and owner `Transaction`; then
4. performs graph maintenance in that order.

The transaction rolls back if a lock cannot be acquired, base indexing fails,
an invariant check fails, or component repair fails. No success-shaped
fallback is allowed. Locks release with the transaction, giving retried
requests a complete prior state or no state, never a partially repaired graph.
The existing base merge retry is safe because the wrapper derives and replaces
the current contribution; re-executing the wrapper converges to the same rows
and edge counts.

The materialization intentionally differs from `PostMergeExtensionUpdater`.
That updater is nullable and best-effort after a completed merge. Group
maintenance is correctness-critical, must be atomic, and must surface failure
to the writer. Operational telemetry records lock wait, affected identities,
edge changes, component repairs, generation state transitions, and failures.

## Indexed `$lastn` Query Shape

`LastNEmitter` retains its existing candidate CTE, authorization, tenant,
visibility, ordinary Observation-filter, and tie semantics. It replaces
temporary graph construction with a readiness check and indexed joins:

```sql
WITH lastn_candidates AS (
    -- Existing match-page output: current authorized Observation (T1, Sid1)
),
groups AS (
    SELECT candidate.T1, candidate.Sid1,
           groupRow.GroupKind, groupRow.CodeGroupId, groupRow.TextCode
    FROM lastn_candidates AS candidate
    CROSS JOIN dbo.LastNCodeGroupGeneration AS generation
    INNER JOIN dbo.LastNObservationCodeGroup AS groupRow
        ON groupRow.ResourceTypeId = candidate.T1
       AND groupRow.ResourceSurrogateId = candidate.Sid1
       AND groupRow.SearchParamId = @codeSearchParamId
    WHERE generation.ResourceTypeId = @observationResourceTypeId
      AND generation.SearchParamId = @codeSearchParamId
      AND generation.State = 'Ready'
),
effective_rows AS (
    SELECT groups.*, dateRow.StartDateTime AS EffectiveStart
    FROM groups
    LEFT JOIN dbo.DateTimeSearchParam AS dateRow
        ON dateRow.ResourceTypeId = groups.T1
       AND dateRow.ResourceSurrogateId = groups.Sid1
       AND dateRow.SearchParamId = @effectiveDateSearchParamId
       AND dateRow.IsMax = 1
),
ranked AS (
    SELECT *,
           RANK() OVER (
               PARTITION BY GroupKind, CodeGroupId, TextCode
               ORDER BY CASE WHEN EffectiveStart IS NULL THEN 1 ELSE 0 END,
                        EffectiveStart DESC,
                        CASE WHEN EffectiveStart IS NULL THEN Sid1 END DESC) AS EffectiveRank
    FROM effective_rows
)
SELECT T1, Sid1
FROM ranked
WHERE EffectiveRank <= @maximum;
```

The production emitter keeps its deterministic outer ordering and groups
duplicate resource ids as it does today. `RANK()`, not `ROW_NUMBER()` or
`DENSE_RANK()`, remains mandatory so every Observation tied with the Nth
effective time is returned. The explicit null-effective ordering is retained
from the current compiler and requires an acceptance test before the operation
is served. If the generation row is absent or not `Ready`, the direct adapter
returns a clear operation-unavailable failure; it does not issue the
query-time graph query.

## Migration, Generation, and Backfill

1. Deploy the additive tables, indexes, checks, foreign keys, and wrappers
   through the SQL database project and `SchemaDeployer`.
2. Create a `Pending` generation row for each enabled version-specific
   Observation code search-parameter scope. Existing direct search remains
   unaffected because no route serves `$lastn` yet.
3. Change the new direct writer façade to write through the wrappers. During
   `Building`, every changed Observation is also upserted into
   `LastNCodeGroupDirtyObservation` for that generation.
4. Set `Building` and capture `SnapshotHighWaterSurrogateId`. Batch current,
   non-deleted Observation resources through the existing
   `GetResourcesByTypeAndSurrogateIdRange` pattern. Each batch uses the
   idempotent remove/add algorithm and can restart from its committed range.
5. Acquire the scope application lock, replay dirty resources for the active
   generation, recompute labels and group rows for the full scope, validate
   referential and count invariants, and atomically mark the generation
   `Ready`.
6. On cancellation or failure, retain the row as `Failed` with the reason;
   the next attempt increments `Generation` and starts a fresh resumable
   build. `$lastn` remains unavailable throughout `Pending`, `Building`, and
   `Failed`.

Dirty replay handles resources written after the snapshot and resources whose
current version changed while their older surrogate id was being scanned.
Final locking prevents a write between the last replay, component computation,
and readiness transition.

## Implementation Tasks

1. Add the additive schema objects, deployment tests, and a scope registry
   backed by `LastNCodeGroupGeneration`.
2. Implement stored-procedure graph primitives and the three transaction-owning
   wrappers without altering `MergeResources`, `HardDeleteResource`, or their
   existing TVP types.
3. Add the direct SQL Server writer façade and route direct merge, search-index
   update/reindex, and hard-delete paths through it.
4. Add generation orchestration, resumable batch backfill, dirty replay,
   validation, operational metrics, and repair tooling.
5. Replace only the graph-building portion of `LastNEmitter` with the ready
   generation and materialized-group joins; preserve compiler plan validation,
   candidate filtering, parameter order, and deterministic SQL behavior.
6. Add the direct execution adapter, Application handler, Minimal API route,
   capability advertisement, and explicit unavailable response only after the
   materialized query and backfill acceptance criteria pass.

## Test Matrix and Acceptance Criteria

| Area | Required coverage |
|------|-------------------|
| Identity | Null and non-null systems, case-sensitive code/text values, 256-character prefix plus `CodeOverflow`, and a forced equal-hash test double proving full-value equality still decides identity. |
| Graph | Single coding, multiple codings, transitive bridges, duplicate coding rows, shared-edge reference counts, merge, removal, last-edge deletion, and a split into two or more components. |
| Current state | Create, update, retry, reindex, hard delete, history-only delete, deleted resources, and no-coding/text-only Observations. |
| Concurrency | Competing writes in one scope, independent scopes, deterministic multi-scope lock order, lock timeout, deadlock retry, rollback, and no visible partial group update. |
| Backfill | Empty scope, resumable batches, writes during building, dirty catch-up, generation failure, ready transition, and rejection of every non-ready generation. |
| Query | Normal filters and authorization before grouping, coded and text-only groups, `RANK()` boundary ties, missing effective times, empty results, code-group ordering, and no ordinary paging, sort, include, or revinclude behavior added without a separate decision. |
| Compiler and database | ScriptDom grammar, deterministic emitted SQL and parameter order, schema deployment/upgrade, query plans using the materialized indexes, and direct integration execution. |
| Performance | The recorded 10,000-candidate, 400-group, one-to-three-coding, transitive-bridge workload must demonstrate warm P95 below 100 ms with no spill. |

The performance target is an acceptance gate, not an estimate. Benchmark
reports record SQL Server version, compatibility level, hardware, warm-up and
sample counts, P50/P95/maximum, row and graph cardinalities, command timeout,
and actual-plan spill markers.

## Benchmark Evidence

The rejected query-time prototype used the repeatable opt-in fixture
`LastNSqlBenchmarkTests` with 10,000 current Observation candidates, 400
independent groups, one to three codings per Observation, explicit
`a -> b -> c -> d` bridges in every group, and five warm-ups followed by 30
measured executions. Its exact component-label representation stored one label
per node rather than a quadratic reachability table, yet measured:

| Metric | Result |
|--------|--------|
| P50 | 430.208 ms |
| P95 | 625.414 ms |
| Maximum | 634.397 ms |
| Code nodes / labels | 1,600 / 1,600 |
| Distinct code edges | 4,800 |
| Final components | 400 |
| Actual-plan spill marker | None |

The P95 is 6.25 times the sub-100 ms acceptance target. A second exact
identity-mapping variant avoided wide-sort spill risk but regressed to
927.872 ms P95. The evidence rejects query-time graph derivation, not exact
transitive grouping. The materialized design must repeat this workload and
meet the target before the verdict changes.

## Tradeoffs, Risks, and Alignment

| Benefit | Cost or risk | Mitigation |
|---------|--------------|------------|
| Read path is indexed and avoids per-request graph construction | Writes do graph maintenance and serialize per scope | Scope-specific transaction application locks; benchmark write latency and contention. |
| Exact translations and reference counts preserve FHIR semantics | Component splits require localized traversal | Recompute every identity in affected old/new components and test split cases. |
| Additive schema and wrappers preserve established procedure/TVP contracts | A new direct writer boundary is required | Keep it narrow, tenant-scoped, and the only graph-table mutation surface. |
| Ready generation prevents partially backfilled reads | `$lastn` is unavailable during build or repair | Return an explicit unavailable result and surface generation telemetry. |
| Stable minimum-id labels make updates deterministic | Labels change after splits and cannot be exposed | Treat labels as internal and return only resource results. |

- [x] Preserves API → Application → Domain ← DataLayer layering: graph state is
  maintained in the SQL data layer.
- [x] Keeps `$lastn` out of `SqlEntityFrameworkSearchService`.
- [x] Preserves existing `MergeResources` procedures and TVP schemas.
- [x] Keeps the direct compiler's candidate-filter, authorization, and
  parameter-order invariants.
- [x] Uses case-sensitive exact code and text comparison where required.
- [ ] Demonstrates the sub-100 ms P95 target on the materialized implementation.

## Related Material

- [Direct `$lastn` investigation](lastn-direct-search.md) — operation model,
  compiler shape, FHIR evidence, and rejected query-time benchmark.
- [Search SQL decomposition](search-sql-decomposition.md) — public plan,
  emitted SQL, and parameter-order invariants the emitter change must retain.
- [ADR 2509: InMemory Search Architecture](../../../adr/adr-2509-inmemory-search.md)
  — existing search/indexing context.
- [ADR 2512: Event-Sourced Conformance Management](../../../adr/adr-2512-event-sourced-conformance.md)
  — proposed SearchParameter lifecycle context.

## Verdict

**Pending evaluation.**

Reference-counted graph materialization is the selected design because it
preserves exact FHIR code equivalence while removing the measured query-time
cost from `$lastn` reads. It is not accepted for production until schema and
writer-boundary tests pass, generation readiness prevents stale reads, and the
materialized indexed query demonstrates warm P95 below 100 ms on the recorded
workload.
