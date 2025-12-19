# Background Jobs Feature

Long-running background operations and transaction management for FHIR server operations.

## Overview

This feature covers background job patterns for:
- Long-running FHIR operations ($reindex, $export, $import)
- Transaction recovery and cleanup
- Distributed coordination across multiple instances
- Fault tolerance and state persistence

## Investigations

| Investigation | Status | Date | Description |
|--------------|--------|------|-------------|
| [DurableTask](investigations/durabletask.md) | Complete | 2025-10-14 | Azure DurableTask framework for long-running background operations |
| [Watchdog Patterns](investigations/watchdog-patterns.md) | Complete | 2025-10-14 | Transaction recovery and cleanup patterns with distributed locking |

## Decision

See [ADR-2510: Background Jobs with DurableTask Framework](../../adr/adr-2510-background-jobs.md)

## Key Decisions

1. **DurableTask for Background Operations**: Use Azure DurableTask framework for all long-running operations requiring persistence, monitoring, and fault tolerance
2. **IHostedService for Watchers**: Simple background services for transaction recovery and cleanup
3. **Distributed Locking Abstraction**: `IDistributedLockManager` interface supports multiple data layers (FileSystem, SQL Server, Cosmos DB)
4. **Phase-Based Implementation**:
   - Phase 1-7: In-Memory emulator + file-based locks
   - Phase 8+: SQL Server/Cosmos DB with distributed locking
   - Phase 13+: Production DurableTask orchestrations

## Architecture

### Background Operations Stack

```
┌─────────────────────────────────────────────────────────┐
│  Long-Running Operations (DurableTask)                  │
│  - $reindex, $export, $import, $bulk-delete             │
│  - Orchestrations + Activities pattern                  │
├─────────────────────────────────────────────────────────┤
│  Transaction Watchers (IHostedService)                  │
│  - TransactionRecoveryWatcher (stalled transactions)    │
│  - TransactionCleanupWatcher (failed transactions)      │
├─────────────────────────────────────────────────────────┤
│  Distributed Locking (IDistributedLockManager)          │
│  - FileSystem (Phase 1-7)                               │
│  - SQL Server sp_getapplock (Phase 8+)                  │
│  - Cosmos DB lease containers (Phase 9+)                │
└─────────────────────────────────────────────────────────┘
```

## Related Features

- [Bundle Processing](../bundle-processing/readme.md) - Two-phase transaction architecture
- [Export](../export/readme.md) - Bulk data export operations
- [Multi-Tenancy](../multi-tenancy/readme.md) - Multi-tenant watcher coordination

## Implementation Phases

- **Phase 1.1**: Basic IHostedService watchers with file-based locking
- **Phase 8**: SQL Server distributed locking (sp_getapplock)
- **Phase 9**: Cosmos DB distributed locking (lease containers)
- **Phase 12**: $reindex operation with DurableTask
- **Phase 13**: Bulk operations ($export, $import) with DurableTask
