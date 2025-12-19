# Investigation: Background Job Patterns for Transaction Management and Cleanup

**Feature**: Background Jobs
**Date**: 2025-10-14
**Status**: Complete
**Related ADRs**: ADR-2500 (Master Roadmap), ADR-2502 (Phase 1.1 Bundle Processing), ADR-2523 (Multi-Tenancy)
**Related Investigations**: `durabletask.md`, `bundle-processing/deferred-writes.md`

## Executive Summary

Based on analysis of the current FHIR Server v2 transaction system and Microsoft FHIR Server's watchdog implementations, this investigation recommends background job patterns for:

1. **TransactionRecoveryWatcher**: Detect and recover stalled transactions (orphaned lock files)
2. **TransactionCleanupWatcher**: Clean up failed/orphaned transaction artifacts
3. **Multi-Tenant Coordination**: Handle watchers across multiple partitions
4. **Distributed Locking**: Coordinate watchers across multiple instances (Phase 8+)

**Key Recommendations**:
- Use **IHostedService** pattern for Phase 1.1 (simple, file-based, single instance)
- Implement **IDistributedLockManager** abstraction for Phase 8+ (SQL Server, Cosmos DB multi-instance)
- Migrate to **DurableTask orchestrations** in Phase 13+ (production-grade fault tolerance)

[Rest of content from transaction-watchdog-patterns.md lines 23-1909]
