# Ignixa.Application.BackgroundOperations

This library contains background operation handlers for the Ignixa FHIR Server using the DurableTask Framework.

## Description

It includes DurableTask orchestrations and activities for long-running tasks:

### Bulk Data Operations
- **$export** (Bulk Data Export) - Export FHIR resources to NDJSON/Parquet format
- **$import** (Bulk Data Import) - Import FHIR resources from NDJSON files

### System Maintenance
- **TTL Cleanup** - Automatic deletion of expired resources based on time-to-live
- **Transaction Watcher** - Monitors and cleans up stale transaction allocations

## Architecture

All background operations follow the DurableTask orchestration pattern:

```
Orchestration (coordinator)
  ├─> Activity 1 (worker task)
  ├─> Activity 2 (worker task)
  └─> Activity N (worker task)
```

### TTL Cleanup Orchestration

Removes resources that have exceeded their configured TTL (time-to-live).

**Files:**
- `TtlCleanup/Orchestrations/TtlCleanupOrchestration.cs` - Coordinates cleanup across tenants
- `TtlCleanup/Activities/TtlCleanupActivity.cs` - Processes batches of expired resources
- `TtlCleanup/Models/` - Input/output models

**Flow:**
1. Orchestration starts on configured schedule (default: every 15 minutes)
2. For each tenant, spawn activity to find and delete expired resources
3. Activity queries `ResourceTtl` table for resources past expiration
4. Hard deletes resources in batches (default: 500 per batch)
5. Logs deletions to audit trail

**Configuration:**
```json
{
  "TtlCleanup": {
    "Enabled": true,
    "CheckIntervalMinutes": 15,
    "BatchSize": 500,
    "MaxConcurrentBatches": 4
  }
}
```

### Transaction Watcher Orchestration

Monitors system transaction allocation and cleans up stale entries to prevent ID exhaustion.

**Files:**
- `TransactionWatcher/Orchestrations/TransactionWatcherOrchestration.cs` - Monitors transaction state
- `TransactionWatcher/Activities/TransactionWatcherActivity.cs` - Queries and reports on allocations
- `TransactionWatcher/Models/` - Input/output models

**Flow:**
1. Orchestration runs periodically to check transaction allocation health
2. Activity queries system partition (partition 0) for transaction state
3. Reports on allocation usage and stale entries
4. Prevents transaction ID exhaustion

## Eternal Orchestrations

Background operations are started automatically on server startup via `EternalOrchestrationStarter`:

- **TTL Cleanup** - Runs every 15 minutes (configurable)
- **Transaction Watcher** - Runs every 5 minutes (configurable)

These orchestrations use DurableTask's "eternal orchestration" pattern - they continue indefinitely, sleeping between runs.

## DurableTask Benefits

Using DurableTask Framework provides:

- **Fault tolerance** - Automatic retry on activity failures
- **Scalability** - Parallel processing across workers
- **Observability** - Query orchestration state and history
- **Pause/Resume** - Administrative control over running jobs
- **Persistence** - State survives server restarts

## Dependencies

- `DurableTask.Core` - Orchestration framework
- `DurableTask.SqlServer` - SQL-based persistence backend
- `Ignixa.Domain` - Repository interfaces and models
- `Ignixa.Application` - Core application services

**Note:** This is an internal component of the Ignixa FHIR Server and is not intended to be used directly by external applications.
