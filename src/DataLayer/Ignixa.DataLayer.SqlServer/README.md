# Ignixa.DataLayer.SqlServer

This library provides the raw ADO.NET connection and retry layer used by the Ignixa FHIR
Server's SQL Server-backed tenant storage.

## Description

It implements `ISqlExecutionService`, a tenant-scoped SQL execution service that resolves a
tenant's connection string via `ITenantConfigurationStore`, opens and disposes its own
`SqlConnection` per call, and retries transient `SqlException`s (deadlocks, timeouts, Azure SQL
throttling/failover) with exponential backoff via Polly. It intentionally uses raw ADO.NET only --
no Entity Framework Core or other ORM -- per the data-layer migration's architectural constraints.

**Note:** This is an internal component of the Ignixa FHIR Server and is not intended to be used
directly by external applications.

## Materialized Observation `$lastn`

SQL Server owns the materialized Observation code-group schema, generation state,
and graph maintenance. `SqlResourceIndexWriter` is the direct mutation boundary:
its `MergeAsync`, `ReindexAsync`, and `HardDeleteAsync` methods call
`MergeResourcesAndMaintainLastNGroups`,
`UpdateResourceSearchParamsAndMaintainLastNGroups`, and
`HardDeleteResourceAndMaintainLastNGroups`, respectively. The wrappers preserve
the base procedure and TVP contracts while making the base write and group
maintenance one transaction. No independent graph-table mutation API is exposed.

For every affected `(ResourceTypeId, SearchParamId)` scope, wrappers acquire the
transaction-owned exclusive application lock
`LastNCodeGroup:{ResourceTypeId}:{SearchParamId}` in lexicographic scope order.
The lock timeout is 15 seconds. Lock, base-write, or maintenance failure rolls
back the complete wrapper transaction; callers must make a fresh write call after
a deadlock rather than assuming an ambiguous transaction was replayed.

### Generation and recovery

Use `ILastNCodeGroupBackfillService.EnableScopeAsync` to create the scope's
`Pending` generation row, then call `BuildAsync` with a positive batch size. A
build starts a distinct attempt, increments the generation, changes the state to
`Building`, records its snapshot high-water surrogate id, and takes a one-minute
lease. Each committed batch atomically advances the durable high-water progress
and renews that lease. The same attempt can resume immediately; a different
attempt is rejected while the lease is live. After expiry, one new `BuildAsync`
caller atomically takes ownership of the same generation and resumes at the first
uncommitted range. It does not replay committed ranges or capture a new snapshot.

Writes that occur while a generation is `Building` are deduplicated in
`LastNCodeGroupDirtyObservation`. Completion holds the same exclusive scope lock,
replays dirty resources until empty, repairs the full scope, validates invariants,
and atomically marks the generation `Ready`. Cancellation or failure records
`Failed` with a bounded reason. Batch, completion, and failure operations match
both the active generation and its current attempt, so an expired owner cannot
mutate a generation after takeover. `BuildAsync` obtains lease timestamps from
its `TimeProvider` (system time by default), allowing deterministic recovery
tests. If starting a generation has an ambiguous
connectivity outcome, the service reconciles only its own attempt id before
recording failure; reconciliation remains best-effort while SQL Server is
unreachable.

`ILastNSearchExecutor` executes only compiled `ResultShape.LastN` SQL. Reads hold
the compiler-emitted shared scope lock while readiness and materialized rows are
read. Missing, `Pending`, `Building`, and `Failed` generations return SQL error
`50403`, mapped to `LastNUnavailableException`; there is no query-time graph or
Entity Framework fallback. The direct executor is available for integration, but
the application has not added an HTTP `$lastn` route or capability statement.

### Measured acceptance benchmark

The acceptance fixture is opt-in and uses a fresh deployed database:

```powershell
$env:TEST_SQL_CONNECTION_STRING = '<live SQL Server connection string>'
$env:RUN_LASTN_BENCHMARK = '1'
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastNMaterializedSqlBenchmarkTests" --framework net10.0 --no-restore --logger "console;verbosity=detailed"
```

The final Task 7 run used SQL Server `17.0.1125.2`, compatibility level `170`,
16 logical CPUs, and 65,484 MB visible memory. For five warm-ups and 30 measured
compiled reads of 10,000 candidates across 400 groups, P50 was `37.122 ms`, P95
was `43.667 ms`, and maximum was `44.892 ms`. It returned 400 results with 19,999
materialized memberships, 1,600 identities, and 400 components; no
`SpillToTempDb` marker or positive `SpillLevel` was observed. This environment-
specific result passes the sub-100 ms P95 gate but does not replace production
monitoring for skew, contention, or rebuild duration.
