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

## Observation $lastn prototype

`LastNSearchExecutor` implements `ILastNSearchExecutor` over the existing
`ISqlExecutionService`. It accepts a compiled `ResultShape.LastN`, binds explicit
SQL parameter types, and returns rows through the supplied reader callback.
The normal tenant connection resolution, retries, timeout, and cancellation
apply; errors propagate.

The compiler emits one read-only CTE statement using existing search tables.
No schema deployment, temporary tables, materialization readiness, backfill, or
special writer is required. Code equivalence is computed only within the
filtered candidates. Recursive graph traversal is deliberately a small-dataset
prototype and may be very slow for interconnected codes.

Construct the executor with your `ISqlExecutionService`; no production HTTP
route or capability advertisement is installed by this library.
