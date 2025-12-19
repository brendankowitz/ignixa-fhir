# ADR 2510: Background Jobs with DurableTask Framework

## Status
Accepted

## Context
FHIR servers require long-running background operations:
- `$reindex` - Reindex resources when search parameters change
- `$export` - Export large datasets to NDJSON files
- `$import` - Import bulk NDJSON data with validation
- `$bulk-delete` - Delete resources matching criteria
- Subscription processing

**Requirements:**
- Persistent state (survives server restarts)
- Progress monitoring (percentage complete, estimated time)
- Fault tolerance (retry failed tasks, handle crashes)
- Cancellation support
- Scalability (parallel processing, multiple workers)

Legacy microsoft/fhir-server used custom task frameworks with custom state persistence, retry logic, and limited scalability.

## Decision
Use **Azure DurableTask framework** for all background operations:

```
┌─────────────────────────────────────────────┐
│  Orchestration (Long-running workflow)       │
│  - ReindexOrchestration, ExportOrchestration │
├─────────────────────────────────────────────┤
│  Activities (Individual units of work)       │
│  - ReindexBatchActivity, ExportResourcesActivity │
├─────────────────────────────────────────────┤
│  DurableTask Runtime                         │
│  - State persistence, retry, coordination    │
├─────────────────────────────────────────────┤
│  Storage Backend                             │
│  - SQL Server (production), In-Memory (test) │
└─────────────────────────────────────────────┘
```

**Key Benefits:**
- Linear scalability (add more worker machines)
- Multiple persistence backends (Azure Storage, SQL Server, Redis, In-Memory)
- Built-in state management and automatic retry
- Powers Azure Durable Functions (proven at scale)

**Pattern:** Orchestrations coordinate activities. Activities are stateless, retriable, idempotent units of work.

## Consequences

**Positive:**
- Enterprise-grade workflow orchestration
- Persistent state survives crashes/restarts
- Built-in retry policies and fault tolerance
- Progress monitoring via `SetCustomStatus()`
- MIT licensed, Microsoft-maintained

**Negative:**
- Additional dependency (DurableTask NuGet packages)
- Learning curve for orchestration patterns
- Storage backend configuration required

## References
- Investigation: `docs/features/background-jobs/investigations/durabletask.md`
- Investigation: `docs/features/background-jobs/investigations/watchdog-patterns.md`
- Source: https://github.com/Azure/durabletask
