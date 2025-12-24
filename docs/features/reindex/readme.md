# Feature: Reindex

**Status**: Exploring
**Created**: 2025-12-19

## Problem Statement
When search parameters are added, modified, or disabled, existing FHIR resources need their search index entries updated to reflect the new parameter definitions. Without a reindex mechanism, newly-enabled search parameters won't return results for existing resources, and disabled parameters will continue to exist in the index, wasting storage and potentially causing incorrect search results.

## Constraints
- Must work with SQL Server data provider (primary target)
- Must handle multi-tenant architecture (Tenant 0 = system partition, reserved)
- Must integrate with existing Durable Task Framework infrastructure
- Must ensure index consistency - all resources either fully indexed or clearly marked as incomplete
- Must support large datasets (millions of resources) without blocking normal FHIR operations
- Must be resilient to failures, restarts, and cancellations
- Must track progress and allow monitoring of reindex operations

## Investigations
| Investigation | Status | Summary |
|--------------|--------|---------|
| [durable-task-orchestration](investigations/durable-task-orchestration.md) | ✅ Recommended | Use DurableTask Framework with Orchestrator/Worker pattern (same as Export/Import). Handles 1M-1B+ rows via surrogate ID partitioning, built-in pause/resume/cancel, 24-48 parallel workers, fault-tolerant |
| [event-driven-triggering](investigations/event-driven-triggering.md) | In Progress | Leverage event-sourced conformance to trigger reindex with precision: know exact SPs, exact resource types, and exact cutoff time (only resources BEFORE activation need reindexing) |
| [background-service-simple](investigations/background-service-simple.md) | ❌ Not Recommended | Simple BackgroundService polling SQL. Good for <1M rows but no horizontal scale-out, manual checkpointing, inconsistent with existing patterns |
| [sql-queue-state-machine](investigations/sql-queue-state-machine.md) | ⚠️ Acceptable | Custom SQL queue (src-old pattern). Scales well but high complexity, polling overhead, deprecated in favor of DurableTask |

## Decision
**WINNER: DurableTask Orchestration** (20/21 points in decision matrix)

See [comparison.md](comparison.md) for detailed analysis.

**Key Reasons**:
1. Proven at scale (Export achieves >10K resources/sec using identical pattern)
2. Built-in pause/resume/cancel (no custom implementation)
3. Architectural consistency (matches Export/Import)
4. Lowest code complexity (~650 LOC vs 850-1750 for alternatives)
5. Already integrated (DurableTask.SqlServer configured)

**Next Steps**: Create ADR → Implement proof-of-concept → Performance test → Full implementation
