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
