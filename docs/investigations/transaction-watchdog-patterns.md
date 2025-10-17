# Investigation: Background Job Patterns for Transaction Management and Cleanup

**Date**: 2025-10-14
**Status**: Complete
**Related ADRs**: ADR-2500 (Master Roadmap), ADR-2502 (Phase 1.1 Bundle Processing), ADR-2523 (Multi-Tenancy)
**Related Investigations**: `background-jobs-with-durabletask.md`, `bundle-deferred-writes.md`

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

---

## 1. Background Job Architecture Analysis

### Microsoft FHIR Server Watchdog Pattern

#### Base Architecture

```csharp
// Microsoft pattern: Generic Watchdog<T> base class
public abstract class Watchdog<T> : IHostedService
{
    protected abstract Task RunWorkAsync(CancellationToken cancellationToken);

    // Configuration
    protected int LeasePeriodSec { get; set; } = 20;
    protected int PeriodSec { get; set; } = 3;
    protected bool AllowRebalance { get; set; } = true;
}
```

**Key Characteristics**:
- **IHostedService**: ASP.NET Core background service pattern
- **Lease-based execution**: Prevents multiple instances from running simultaneously
- **Configurable intervals**: LeasePeriodSec (lease duration), PeriodSec (polling frequency)
- **Rebalancing**: Dynamic workload distribution across multiple instances

#### TransactionWatchdog Implementation

```csharp
public class TransactionWatchdog : Watchdog<TransactionWatchdog>
{
    protected override async Task RunWorkAsync(CancellationToken cancellationToken)
    {
        // 1. Advance transaction visibility
        await MergeResourcesAdvanceTransactionVisibilityAsync(cancellationToken);

        // 2. Get timed-out transactions (6x heartbeat period)
        var timedOutTransactions = await MergeResourcesGetTimeoutTransactionsAsync(cancellationToken);

        // 3. Process timed-out transactions in parallel (max 2 concurrent)
        await Parallel.ForEachAsync(
            timedOutTransactions,
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken },
            async (txId, ct) =>
            {
                // Commit transaction with zero resources or recover
                await CommitOrRollbackTransactionAsync(txId, ct);
            });
    }
}
```

**Key Patterns**:
1. **Timeout Detection**: 6x heartbeat period (e.g., 6 * 20s = 120s)
2. **Parallel Processing**: Up to 2 concurrent transaction recoveries
3. **Transaction Advancement**: Move transactions through visibility states

#### InvisibleHistoryCleanupWatchdog Implementation

```csharp
public class InvisibleHistoryCleanupWatchdog : Watchdog<InvisibleHistoryCleanupWatchdog>
{
    private int RetentionPeriodDays { get; set; } = 7;

    protected override async Task RunWorkAsync(CancellationToken cancellationToken)
    {
        // 1. Get retention period from configuration
        var retentionPeriod = await GetRetentionPeriodAsync(cancellationToken);

        // 2. Find transactions older than retention period
        var oldTransactions = await GetTransactionsOlderThanAsync(
            DateTimeOffset.UtcNow.AddDays(-retentionPeriod),
            cancellationToken);

        // 3. Delete invisible history for each transaction
        int totalRows = 0;
        foreach (var txId in oldTransactions)
        {
            var deleted = await DeleteInvisibleHistoryAsync(txId, cancellationToken);
            totalRows += deleted;
        }

        _logger.LogInformation(
            "Cleanup complete: {TransactionCount} transactions, {RowCount} rows deleted",
            oldTransactions.Count,
            totalRows);
    }
}
```

**Key Patterns**:
1. **Retention Policy**: Configurable (default 7 days)
2. **Incremental Processing**: Tracks last cleaned transaction ID
3. **Batch Deletion**: Process transactions sequentially, track total rows

---

## 2. Current Transaction System Analysis

### File-Based Transaction Lifecycle

**Storage Structure**:
```
fhir-data/
└── _transactions/
    └── YYYY/MM/DD/
        ├── tx-{transactionId}.lock.ndjson    # Uncommitted (in-progress)
        └── tx-{transactionId}.ndjson         # Committed (complete)
```

**Transaction Lifecycle**:
1. **Create**: `DeferredWriteCoordinator.CreateAsync()` allocates transaction ID from Partition 0
2. **Write**: `BatchWriteAsync()` creates/appends to `.lock.ndjson` file
3. **Commit**: `CommitAsync()` renames `.lock.ndjson` → `.ndjson`
4. **Orphan**: Lock file remains if commit fails or server crashes

### Current Issues Requiring Watchdogs

#### Issue 1: Orphaned Lock Files (Stalled Transactions)

**Scenario**: Server crashes after `BatchWriteAsync()` but before `CommitAsync()`

```
_transactions/2025/10/13/
├── tx-12345.lock.ndjson   # Orphaned (process crashed)
├── tx-12346.ndjson        # Committed (success)
└── tx-12347.lock.ndjson   # In-progress (legitimate)
```

**Detection Criteria**:
- Lock file exists
- No corresponding `.ndjson` file
- Last modified > timeout threshold (e.g., 5 minutes)

**Resolution Strategy**:
- **Option A**: Commit the transaction (rename `.lock.ndjson` → `.ndjson`)
- **Option B**: Roll back (delete `.lock.ndjson` + metadata files)

#### Issue 2: Failed Transaction Cleanup

**Scenario**: Transaction fails validation, partial writes remain

```
_transactions/2025/10/13/
├── tx-99999.lock.ndjson   # Failed validation

_internal/Patient/123/
└── 99999.metadata.json    # Orphaned metadata

Patient/2025/10/13/
└── tx-99999.ndjson        # Orphaned resource file
```

**Cleanup Targets**:
- Orphaned lock files (failed transactions)
- Orphaned metadata files (`_internal/{ResourceType}/{id}/{transactionId}.metadata.json`)
- Orphaned resource files (`{ResourceType}/{YYYY}/{MM}/{DD}/tx-{transactionId}.ndjson`)

---

## 3. Recommended Watcher Implementations

### Phase 1.1 - Simple IHostedService Watchers

For early phases with file-based storage, use simple `IHostedService` pattern:

#### TransactionRecoveryWatcher

```csharp
// Ignixa.Api/BackgroundServices/TransactionRecoveryWatcher.cs
namespace Ignixa.Api.BackgroundServices;

public class TransactionRecoveryWatcher : BackgroundService
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly ILogger<TransactionRecoveryWatcher> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _stallThreshold = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transaction Advancement Watchdog starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AdvanceStalledTransactionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in transaction recovery watcher");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Transaction Advancement Watchdog stopping");
    }

    private async Task AdvanceStalledTransactionsAsync(CancellationToken cancellationToken)
    {
        // Get all active tenants
        var tenants = await _tenantStore.GetAllTenantsAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            if (tenant.IsSystemPartition)
                continue; // Skip Partition 0

            try
            {
                await AdvanceStalledTransactionsForTenantAsync(tenant.TenantId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to advance stalled transactions for tenant {TenantId}",
                    tenant.TenantId);
            }
        }
    }

    private async Task AdvanceStalledTransactionsForTenantAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);

        // Assume we add this method to IFhirRepository
        var stalledTransactions = await repository.GetStalledTransactionsAsync(
            _stallThreshold,
            cancellationToken);

        if (stalledTransactions.Count == 0)
            return;

        _logger.LogInformation(
            "Found {Count} stalled transactions for tenant {TenantId}",
            stalledTransactions.Count,
            tenantId);

        foreach (var transactionId in stalledTransactions)
        {
            try
            {
                await AdvanceTransactionAsync(repository, transactionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to advance transaction {TransactionId} for tenant {TenantId}",
                    transactionId,
                    tenantId);
            }
        }
    }

    private async Task AdvanceTransactionAsync(
        IFhirRepository repository,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Advancing stalled transaction {TransactionId}",
            transactionId);

        // Option A: Commit the transaction (most common case)
        // If lock file exists and is well-formed, commit it
        try
        {
            await repository.CommitTransactionAsync(transactionId, cancellationToken);

            _logger.LogInformation(
                "Successfully advanced transaction {TransactionId} (committed)",
                transactionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to commit stalled transaction {TransactionId}, will retry",
                transactionId);
        }
    }
}
```

#### Transaction Cleanup Watchdog

```csharp
// Ignixa.Api/BackgroundServices/TransactionCleanupWatcher.cs
namespace Ignixa.Api.BackgroundServices;

public class TransactionCleanupWatcher : BackgroundService
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly ILogger<TransactionCleanupWatcher> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transaction Cleanup Watchdog starting");

        // Wait 5 minutes before first run (let system stabilize)
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupFailedTransactionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in transaction cleanup watcher");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Transaction Cleanup Watchdog stopping");
    }

    private async Task CleanupFailedTransactionsAsync(CancellationToken cancellationToken)
    {
        var tenants = await _tenantStore.GetAllTenantsAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            if (tenant.IsSystemPartition)
                continue;

            try
            {
                await CleanupFailedTransactionsForTenantAsync(tenant.TenantId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to cleanup failed transactions for tenant {TenantId}",
                    tenant.TenantId);
            }
        }
    }

    private async Task CleanupFailedTransactionsForTenantAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);

        // Get failed transactions older than retention period
        var cutoffTime = DateTimeOffset.UtcNow - _retentionPeriod;
        var failedTransactions = await repository.GetFailedTransactionsAsync(
            cutoffTime,
            cancellationToken);

        if (failedTransactions.Count == 0)
            return;

        _logger.LogInformation(
            "Found {Count} failed transactions to clean up for tenant {TenantId}",
            failedTransactions.Count,
            tenantId);

        var totalFilesDeleted = 0;

        foreach (var transactionId in failedTransactions)
        {
            try
            {
                var filesDeleted = await CleanupTransactionAsync(
                    repository,
                    transactionId,
                    cancellationToken);

                totalFilesDeleted += filesDeleted;

                _logger.LogDebug(
                    "Cleaned up transaction {TransactionId}: {FilesDeleted} files deleted",
                    transactionId,
                    filesDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to cleanup transaction {TransactionId}",
                    transactionId);
            }
        }

        _logger.LogInformation(
            "Cleanup complete for tenant {TenantId}: {TransactionCount} transactions, {FilesDeleted} files",
            tenantId,
            failedTransactions.Count,
            totalFilesDeleted);
    }

    private async Task<int> CleanupTransactionAsync(
        IFhirRepository repository,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        // Assume we add this method to IFhirRepository
        return await repository.DeleteTransactionArtifactsAsync(
            transactionId,
            cancellationToken);
    }
}
```

### Required IFhirRepository Extensions

Add these methods to `IFhirRepository`:

```csharp
// Ignixa.Domain/Abstractions/IFhirRepository.cs
public interface IFhirRepository
{
    // Existing methods...
    ValueTask<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken ct = default);
    ValueTask<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default);
    ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(...);

    // NEW: Watchdog support methods

    /// <summary>
    /// Get transactions with lock files older than specified threshold.
    /// Used by TransactionRecoveryWatcher to detect stalled transactions.
    /// </summary>
    ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken ct = default);

    /// <summary>
    /// Get failed transactions (orphaned lock files) older than retention period.
    /// Used by TransactionCleanupWatcher to identify cleanup targets.
    /// </summary>
    ValueTask<IReadOnlyList<TransactionId>> GetFailedTransactionsAsync(
        DateTimeOffset olderThan,
        CancellationToken ct = default);

    /// <summary>
    /// Delete all artifacts for a failed transaction:
    /// - Lock file (_transactions/.../tx-{id}.lock.ndjson)
    /// - Resource files ({ResourceType}/.../tx-{id}.ndjson)
    /// - Metadata files (_internal/{ResourceType}/{id}/{transactionId}.metadata.json)
    /// Returns number of files deleted.
    /// </summary>
    ValueTask<int> DeleteTransactionArtifactsAsync(
        TransactionId transactionId,
        CancellationToken ct = default);
}
```

### FileBasedFhirRepository Implementation

```csharp
// Ignixa.DataLayer.FileSystem/FileSystem/FileBasedFhirRepository.cs
public sealed class FileBasedFhirRepository : IFhirRepository
{
    // Existing methods...

    public async ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken ct = default)
    {
        var stalledTransactions = new List<TransactionId>();
        var cutoffTime = DateTimeOffset.UtcNow - stallThreshold;

        var transactionDir = Path.Combine(_baseDirectory, "_transactions");
        if (!Directory.Exists(transactionDir))
            return stalledTransactions;

        // Scan all date directories for .lock.ndjson files
        foreach (var lockFile in Directory.GetFiles(transactionDir, "*.lock.ndjson", SearchOption.AllDirectories))
        {
            try
            {
                var fileInfo = new FileInfo(lockFile);

                // Check if lock file is older than threshold
                if (fileInfo.LastWriteTimeUtc < cutoffTime.UtcDateTime)
                {
                    // Check if committed file doesn't exist
                    var committedFile = lockFile.Replace(".lock.ndjson", ".ndjson");
                    if (!File.Exists(committedFile))
                    {
                        // Extract transaction ID from filename
                        var fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(lockFile));
                        if (fileName.StartsWith("tx-"))
                        {
                            var txIdString = fileName.Substring(3);
                            if (TransactionId.TryParse(txIdString, out var txId))
                            {
                                stalledTransactions.Add(txId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing lock file: {LockFile}", lockFile);
            }
        }

        return stalledTransactions;
    }

    public async ValueTask<IReadOnlyList<TransactionId>> GetFailedTransactionsAsync(
        DateTimeOffset olderThan,
        CancellationToken ct = default)
    {
        var failedTransactions = new List<TransactionId>();

        var transactionDir = Path.Combine(_baseDirectory, "_transactions");
        if (!Directory.Exists(transactionDir))
            return failedTransactions;

        // Find lock files older than retention period with no committed file
        foreach (var lockFile in Directory.GetFiles(transactionDir, "*.lock.ndjson", SearchOption.AllDirectories))
        {
            try
            {
                var fileInfo = new FileInfo(lockFile);

                if (fileInfo.LastWriteTimeUtc < olderThan.UtcDateTime)
                {
                    var committedFile = lockFile.Replace(".lock.ndjson", ".ndjson");
                    if (!File.Exists(committedFile))
                    {
                        // Extract transaction ID
                        var fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(lockFile));
                        if (fileName.StartsWith("tx-"))
                        {
                            var txIdString = fileName.Substring(3);
                            if (TransactionId.TryParse(txIdString, out var txId))
                            {
                                failedTransactions.Add(txId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing lock file: {LockFile}", lockFile);
            }
        }

        return failedTransactions;
    }

    public async ValueTask<int> DeleteTransactionArtifactsAsync(
        TransactionId transactionId,
        CancellationToken ct = default)
    {
        var filesDeleted = 0;

        // 1. Delete lock file
        var lockFiles = Directory.GetFiles(
            Path.Combine(_baseDirectory, "_transactions"),
            $"tx-{transactionId}.lock.ndjson",
            SearchOption.AllDirectories);

        foreach (var lockFile in lockFiles)
        {
            try
            {
                File.Delete(lockFile);
                filesDeleted++;
                _logger.LogDebug("Deleted lock file: {LockFile}", lockFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete lock file: {LockFile}", lockFile);
            }
        }

        // 2. Delete resource files (ResourceType/YYYY/MM/DD/tx-{transactionId}.ndjson)
        var resourceFiles = Directory.GetFiles(
            _baseDirectory,
            $"tx-{transactionId}.ndjson",
            SearchOption.AllDirectories);

        foreach (var resourceFile in resourceFiles)
        {
            try
            {
                File.Delete(resourceFile);
                filesDeleted++;
                _logger.LogDebug("Deleted resource file: {ResourceFile}", resourceFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete resource file: {ResourceFile}", resourceFile);
            }
        }

        // 3. Delete metadata files (_internal/{ResourceType}/{id}/{transactionId}.metadata.json)
        var metadataFiles = Directory.GetFiles(
            Path.Combine(_baseDirectory, "_internal"),
            $"{transactionId}.metadata.json",
            SearchOption.AllDirectories);

        foreach (var metadataFile in metadataFiles)
        {
            try
            {
                File.Delete(metadataFile);
                filesDeleted++;
                _logger.LogDebug("Deleted metadata file: {MetadataFile}", metadataFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete metadata file: {MetadataFile}", metadataFile);
            }
        }

        _logger.LogInformation(
            "Deleted {FilesDeleted} artifacts for transaction {TransactionId}",
            filesDeleted,
            transactionId);

        return filesDeleted;
    }
}
```

---

## 4. Configuration and Registration

### appsettings.json Configuration

```json
{
  "BackgroundServices": {
    "TransactionAdvancement": {
      "Enabled": true,
      "PollingIntervalMinutes": 1,
      "StallThresholdMinutes": 5
    },
    "TransactionCleanup": {
      "Enabled": true,
      "PollingIntervalHours": 1,
      "RetentionPeriodDays": 7,
      "InitialDelayMinutes": 5
    }
  }
}
```

### Program.cs Registration

```csharp
// Ignixa.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register background services
var backgroundServicesConfig = builder.Configuration.GetSection("BackgroundServices");

if (backgroundServicesConfig.GetValue<bool>("TransactionAdvancement:Enabled", true))
{
    builder.Services.AddHostedService<TransactionRecoveryWatcher>();
}

if (backgroundServicesConfig.GetValue<bool>("TransactionCleanup:Enabled", true))
{
    builder.Services.AddHostedService<TransactionCleanupWatcher>();
}

var app = builder.Build();
```

---

## 5. Multi-Tenant Coordination

### Challenge: Multiple Partitions

With multi-tenancy (ADR-2523), watchdogs must process transactions across all tenant partitions:

**Partition Structure**:
```
fhir-data/
├── tenants/
│   ├── 0/ (System Partition - skip)
│   │   └── _transactions/...
│   ├── 1/ (Mayo Clinic)
│   │   └── _transactions/...
│   ├── 2/ (Cedars-Sinai)
│   │   └── _transactions/...
│   └── 3/ (Johns Hopkins)
│       └── _transactions/...
```

### Solution: Iterate All Tenants

Both watchers iterate all active tenants:

```csharp
private async Task ProcessAllTenantsAsync(CancellationToken cancellationToken)
{
    var tenants = await _tenantStore.GetAllTenantsAsync(cancellationToken);

    foreach (var tenant in tenants)
    {
        if (tenant.IsSystemPartition)
            continue; // Skip Partition 0

        try
        {
            await ProcessTenantAsync(tenant.TenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process tenant {TenantId}",
                tenant.TenantId);
        }
    }
}
```

### Distributed Locking (Phase 8+)

**Critical**: Distributed locking is required **before** moving to SQL Server or Cosmos DB (Phase 8/9), not just Phase 7. Multi-instance deployments require coordination to prevent concurrent watcher execution.

#### IDistributedLockManager Abstraction

Create a data-layer-agnostic locking interface:

```csharp
// Ignixa.Domain/Abstractions/IDistributedLockManager.cs
namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Manages distributed locks for coordinating background workers across multiple instances.
/// </summary>
public interface IDistributedLockManager
{
    /// <summary>
    /// Attempts to acquire a distributed lock.
    /// </summary>
    /// <param name="lockName">Unique lock identifier</param>
    /// <param name="leaseDuration">How long to hold the lock</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Lock handle (null if failed to acquire)</returns>
    ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockName,
        TimeSpan leaseDuration,
        CancellationToken ct = default);
}

/// <summary>
/// Represents an acquired distributed lock.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    string LockName { get; }
    DateTimeOffset AcquiredAt { get; }
    DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Extends the lock lease (if supported by implementation).
    /// </summary>
    ValueTask<bool> RenewAsync(CancellationToken ct = default);
}
```

#### Usage in Watchers

Update watchers to use distributed locking:

```csharp
public class TransactionRecoveryWatcher : BackgroundService
{
    private readonly IDistributedLockManager _lockManager;
    // ... other fields

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transaction Recovery Watcher starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Try to acquire lock (prevents multiple workers from running)
            await using var lockHandle = await _lockManager.TryAcquireLockAsync(
                "transaction-recovery-watcher",
                TimeSpan.FromMinutes(1),
                stoppingToken);

            if (lockHandle != null)
            {
                // We have the lock - do the work
                _logger.LogDebug("Acquired lock, processing stalled transactions");

                try
                {
                    await RecoverStalledTransactionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing stalled transactions");
                }
            }
            else
            {
                // Another instance has the lock - skip this run
                _logger.LogDebug("Lock held by another instance, skipping run");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Transaction Recovery Watcher stopping");
    }
}
```

#### Phase 1.1: File-Based Locking (Single Instance)

For Phase 1.1, use simple file-based locking (single instance only):

```csharp
// Ignixa.DataLayer.FileSystem/Locking/FileSystemDistributedLockManager.cs
namespace Ignixa.DataLayer.FileSystem.Locking;

public class FileSystemDistributedLockManager : IDistributedLockManager
{
    private readonly string _lockDirectory;
    private readonly ILogger<FileSystemDistributedLockManager> _logger;

    public FileSystemDistributedLockManager(string baseDirectory, ILogger<FileSystemDistributedLockManager> logger)
    {
        _lockDirectory = Path.Combine(baseDirectory, "_locks");
        Directory.CreateDirectory(_lockDirectory);
        _logger = logger;
    }

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockName,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var lockFile = Path.Combine(_lockDirectory, $"{lockName}.lock");

        try
        {
            // Check if lock file exists and is expired
            if (File.Exists(lockFile))
            {
                var fileInfo = new FileInfo(lockFile);
                var expiresAt = fileInfo.LastWriteTimeUtc.Add(leaseDuration);

                if (DateTimeOffset.UtcNow < expiresAt)
                {
                    // Lock still held by another process
                    return null;
                }

                // Lock expired, delete it
                File.Delete(lockFile);
            }

            // Create lock file
            var acquiredAt = DateTimeOffset.UtcNow;
            await File.WriteAllTextAsync(
                lockFile,
                $"{{\"AcquiredAt\":\"{acquiredAt:O}\",\"ProcessId\":{Environment.ProcessId}}}",
                ct);

            return new FileSystemDistributedLock(lockFile, acquiredAt, leaseDuration, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire lock {LockName}", lockName);
            return null;
        }
    }
}

internal class FileSystemDistributedLock : IDistributedLock
{
    private readonly string _lockFilePath;
    private readonly ILogger _logger;

    public string LockName => Path.GetFileNameWithoutExtension(_lockFilePath);
    public DateTimeOffset AcquiredAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public FileSystemDistributedLock(
        string lockFilePath,
        DateTimeOffset acquiredAt,
        TimeSpan leaseDuration,
        ILogger logger)
    {
        _lockFilePath = lockFilePath;
        AcquiredAt = acquiredAt;
        ExpiresAt = acquiredAt.Add(leaseDuration);
        _logger = logger;
    }

    public ValueTask<bool> RenewAsync(CancellationToken ct = default)
    {
        try
        {
            // Update file modified time to extend lease
            File.SetLastWriteTimeUtc(_lockFilePath, DateTime.UtcNow);
            return ValueTask.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to renew lock {LockName}", LockName);
            return ValueTask.FromResult(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(_lockFilePath))
            {
                File.Delete(_lockFilePath);
                _logger.LogDebug("Released lock {LockName}", LockName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release lock {LockName}", LockName);
        }
    }
}
```

#### Phase 8: SQL Server Locking

For SQL Server data layer, use built-in `sp_getapplock`:

```csharp
// Ignixa.DataLayer.SqlServer/Locking/SqlServerDistributedLockManager.cs
namespace Ignixa.DataLayer.SqlServer.Locking;

public class SqlServerDistributedLockManager : IDistributedLockManager
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerDistributedLockManager> _logger;

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockName,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(ct);

            using var command = new SqlCommand("sp_getapplock", connection);
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", lockName);
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", (int)leaseDuration.TotalMilliseconds);

            var returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            await command.ExecuteNonQueryAsync(ct);

            var result = (int)returnValue.Value;

            if (result >= 0)
            {
                // Lock acquired (0 = granted immediately, 1 = granted after waiting)
                _logger.LogDebug("Acquired SQL Server lock {LockName}", lockName);
                return new SqlServerDistributedLock(lockName, connection, DateTimeOffset.UtcNow, leaseDuration, _logger);
            }
            else
            {
                // Lock not acquired (-1 = timeout, -2 = canceled, -3 = deadlock victim, -999 = error)
                await connection.DisposeAsync();
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire SQL Server lock {LockName}", lockName);
            await connection.DisposeAsync();
            return null;
        }
    }
}

internal class SqlServerDistributedLock : IDistributedLock
{
    private readonly SqlConnection _connection;
    private readonly ILogger _logger;

    public string LockName { get; }
    public DateTimeOffset AcquiredAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public SqlServerDistributedLock(
        string lockName,
        SqlConnection connection,
        DateTimeOffset acquiredAt,
        TimeSpan leaseDuration,
        ILogger logger)
    {
        LockName = lockName;
        _connection = connection;
        AcquiredAt = acquiredAt;
        ExpiresAt = acquiredAt.Add(leaseDuration);
        _logger = logger;
    }

    public async ValueTask<bool> RenewAsync(CancellationToken ct = default)
    {
        // SQL Server locks are session-based and don't need explicit renewal
        // as long as the connection stays open
        return _connection.State == ConnectionState.Open;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Release the lock explicitly
            using var command = new SqlCommand("sp_releaseapplock", _connection);
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", LockName);
            command.Parameters.AddWithValue("@LockOwner", "Session");

            if (_connection.State == ConnectionState.Open)
            {
                await command.ExecuteNonQueryAsync();
            }

            await _connection.DisposeAsync();

            _logger.LogDebug("Released SQL Server lock {LockName}", LockName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release SQL Server lock {LockName}", LockName);
        }
    }
}
```

#### Phase 9: Cosmos DB Locking

For Cosmos DB, use lease containers (built into Change Feed):

```csharp
// Ignixa.DataLayer.CosmosDB/Locking/CosmosDbDistributedLockManager.cs
namespace Ignixa.DataLayer.CosmosDB.Locking;

/// <summary>
/// Cosmos DB locking using lease containers.
/// Note: Cosmos DB Change Feed automatically handles distributed coordination,
/// so this implementation is primarily for non-Change-Feed scenarios.
/// </summary>
public class CosmosDbDistributedLockManager : IDistributedLockManager
{
    private readonly Container _leaseContainer;
    private readonly ILogger<CosmosDbDistributedLockManager> _logger;

    public CosmosDbDistributedLockManager(
        CosmosClient cosmosClient,
        string databaseName,
        ILogger<CosmosDbDistributedLockManager> logger)
    {
        _leaseContainer = cosmosClient.GetContainer(databaseName, "leases");
        _logger = logger;
    }

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockName,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var leaseDocument = new
        {
            id = lockName,
            partitionKey = lockName,
            acquiredAt = DateTimeOffset.UtcNow,
            expiresAt = DateTimeOffset.UtcNow.Add(leaseDuration),
            instanceId = $"{Environment.MachineName}-{Environment.ProcessId}",
            ttl = (int)leaseDuration.TotalSeconds + 60 // Cosmos DB TTL for auto-cleanup
        };

        try
        {
            // Try to create the lease document (will fail if already exists)
            var response = await _leaseContainer.CreateItemAsync(
                leaseDocument,
                new PartitionKey(lockName),
                cancellationToken: ct);

            _logger.LogDebug("Acquired Cosmos DB lock {LockName}", lockName);

            return new CosmosDbDistributedLock(
                lockName,
                _leaseContainer,
                response.Resource.acquiredAt,
                leaseDuration,
                _logger);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Lease already exists, check if expired
            try
            {
                var existingLease = await _leaseContainer.ReadItemAsync<dynamic>(
                    lockName,
                    new PartitionKey(lockName),
                    cancellationToken: ct);

                if (DateTimeOffset.Parse(existingLease.Resource.expiresAt.ToString()) < DateTimeOffset.UtcNow)
                {
                    // Lease expired, delete and retry
                    await _leaseContainer.DeleteItemAsync<dynamic>(
                        lockName,
                        new PartitionKey(lockName),
                        cancellationToken: ct);

                    return await TryAcquireLockAsync(lockName, leaseDuration, ct);
                }
            }
            catch
            {
                // Ignore read/delete errors
            }

            return null; // Lock held by another instance
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire Cosmos DB lock {LockName}", lockName);
            return null;
        }
    }
}

internal class CosmosDbDistributedLock : IDistributedLock
{
    private readonly Container _leaseContainer;
    private readonly ILogger _logger;

    public string LockName { get; }
    public DateTimeOffset AcquiredAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public CosmosDbDistributedLock(
        string lockName,
        Container leaseContainer,
        DateTimeOffset acquiredAt,
        TimeSpan leaseDuration,
        ILogger logger)
    {
        LockName = lockName;
        _leaseContainer = leaseContainer;
        AcquiredAt = acquiredAt;
        ExpiresAt = acquiredAt.Add(leaseDuration);
        _logger = logger;
    }

    public async ValueTask<bool> RenewAsync(CancellationToken ct = default)
    {
        try
        {
            // Update expiresAt to extend lease
            var patchOperations = new[]
            {
                PatchOperation.Replace("/expiresAt", DateTimeOffset.UtcNow.Add(ExpiresAt - AcquiredAt))
            };

            await _leaseContainer.PatchItemAsync<dynamic>(
                LockName,
                new PartitionKey(LockName),
                patchOperations,
                cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to renew Cosmos DB lock {LockName}", LockName);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _leaseContainer.DeleteItemAsync<dynamic>(
                LockName,
                new PartitionKey(LockName));

            _logger.LogDebug("Released Cosmos DB lock {LockName}", LockName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release Cosmos DB lock {LockName}", LockName);
        }
    }
}
```

#### Configuration

Register the appropriate lock manager based on data layer:

```csharp
// Ignixa.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register distributed lock manager based on data layer
var dataLayerType = builder.Configuration.GetValue<string>("DataLayer:Type");

switch (dataLayerType)
{
    case "FileSystem":
        var baseDirectory = builder.Configuration.GetValue<string>("DataLayer:FileSystem:BaseDirectory");
        builder.Services.AddSingleton<IDistributedLockManager>(sp =>
            new FileSystemDistributedLockManager(
                baseDirectory,
                sp.GetRequiredService<ILogger<FileSystemDistributedLockManager>>()));
        break;

    case "SqlServer":
        var connectionString = builder.Configuration.GetConnectionString("SqlServer");
        builder.Services.AddSingleton<IDistributedLockManager>(sp =>
            new SqlServerDistributedLockManager(
                connectionString,
                sp.GetRequiredService<ILogger<SqlServerDistributedLockManager>>()));
        break;

    case "CosmosDB":
        var cosmosClient = builder.Services.BuildServiceProvider().GetRequiredService<CosmosClient>();
        var databaseName = builder.Configuration.GetValue<string>("DataLayer:CosmosDB:DatabaseName");
        builder.Services.AddSingleton<IDistributedLockManager>(sp =>
            new CosmosDbDistributedLockManager(
                cosmosClient,
                databaseName,
                sp.GetRequiredService<ILogger<CosmosDbDistributedLockManager>>()));
        break;

    default:
        throw new InvalidOperationException($"Unknown data layer type: {dataLayerType}");
}

// Register watchers
builder.Services.AddHostedService<TransactionRecoveryWatcher>();
builder.Services.AddHostedService<TransactionCleanupWatcher>();
```

---

## 6. Testing Strategy

### Unit Tests

```csharp
// Ignixa.Api.Tests/BackgroundServices/TransactionRecoveryWatcherTests.cs
public class TransactionRecoveryWatcherTests
{
    [Fact]
    public async Task GivenStalledTransaction_WhenWatchdogRuns_ThenCommitsTransaction()
    {
        // Arrange
        var mockRepository = new Mock<IFhirRepository>();
        mockRepository
            .Setup(r => r.GetStalledTransactionsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionId> { TransactionId.Parse("12345") });

        var mockFactory = new Mock<IFhirRepositoryFactory>();
        mockFactory
            .Setup(f => f.GetRepositoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRepository.Object);

        var watchdog = new TransactionRecoveryWatcher(
            mockFactory.Object,
            Mock.Of<ITenantConfigurationStore>(),
            Mock.Of<ILogger<TransactionRecoveryWatcher>>());

        // Act
        await watchdog.AdvanceStalledTransactionsAsync(CancellationToken.None);

        // Assert
        mockRepository.Verify(
            r => r.CommitTransactionAsync(It.IsAny<TransactionId>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenNoStalledTransactions_WhenWatchdogRuns_ThenNoCommits()
    {
        // Arrange
        var mockRepository = new Mock<IFhirRepository>();
        mockRepository
            .Setup(r => r.GetStalledTransactionsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionId>());

        // Act & Assert
        // (Similar setup, verify CommitTransactionAsync never called)
    }
}
```

### Integration Tests

```csharp
// Ignixa.Api.Tests/BackgroundServices/TransactionCleanupIntegrationTests.cs
public class TransactionCleanupIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GivenFailedTransaction_WhenWatchdogRuns_ThenCleansUpArtifacts()
    {
        // Arrange: Create fake failed transaction
        var baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(baseDir);

        var transactionId = TransactionId.Generate();
        var lockFile = Path.Combine(baseDir, "_transactions", "2025", "10", "13", $"tx-{transactionId}.lock.ndjson");
        Directory.CreateDirectory(Path.GetDirectoryName(lockFile));
        File.WriteAllText(lockFile, "{}");

        // Set last modified to 8 days ago (beyond retention period)
        File.SetLastWriteTimeUtc(lockFile, DateTime.UtcNow.AddDays(-8));

        var repository = new FileBasedFhirRepository(baseDir, Mock.Of<ILogger<FileBasedFhirRepository>>());

        // Act
        var failedTransactions = await repository.GetFailedTransactionsAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            CancellationToken.None);

        var filesDeleted = await repository.DeleteTransactionArtifactsAsync(
            failedTransactions[0],
            CancellationToken.None);

        // Assert
        Assert.Single(failedTransactions);
        Assert.True(filesDeleted > 0);
        Assert.False(File.Exists(lockFile));
    }
}
```

### Time-Based Testing

Use `ISystemClock` abstraction to test time-dependent behavior:

```csharp
// Ignixa.Domain/Abstractions/ISystemClock.cs
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

// Production implementation
public class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

// Test implementation
public class TestSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}

// Usage in watchdog
public class TransactionRecoveryWatcher
{
    private readonly ISystemClock _clock;

    private Task<List<TransactionId>> GetStalledTransactionsAsync(...)
    {
        var cutoffTime = _clock.UtcNow - _stallThreshold; // Testable!
        // ...
    }
}
```

---

## 7. Observability and Monitoring

### Logging Standards

Use structured logging with consistent log levels:

```csharp
// Information: Normal operational events
_logger.LogInformation(
    "Transaction Advancement Watchdog: Found {Count} stalled transactions for tenant {TenantId}",
    stalledTransactions.Count,
    tenantId);

// Warning: Recoverable issues
_logger.LogWarning(
    "Failed to commit stalled transaction {TransactionId}, will retry on next run",
    transactionId);

// Error: Failed operations requiring attention
_logger.LogError(
    ex,
    "Failed to cleanup transaction {TransactionId} for tenant {TenantId}",
    transactionId,
    tenantId);

// Debug: Detailed diagnostic information
_logger.LogDebug(
    "Cleaned up transaction {TransactionId}: {FilesDeleted} files deleted",
    transactionId,
    filesDeleted);
```

### Metrics (Future: Phase 18 - Production Readiness)

Emit metrics for watcher operations:

```csharp
// Using OpenTelemetry Metrics API (Phase 18)
private readonly Counter<long> _stalledTransactionsFound;
private readonly Counter<long> _transactionsAdvanced;
private readonly Counter<long> _transactionsCleaned;
private readonly Histogram<double> _watchdogDuration;

public TransactionRecoveryWatcher(...)
{
    var meter = new Meter("Ignixa.BackgroundServices");
    _stalledTransactionsFound = meter.CreateCounter<long>("stalled_transactions_found");
    _transactionsAdvanced = meter.CreateCounter<long>("transactions_advanced");
    _watchdogDuration = meter.CreateHistogram<double>("watchdog_duration_seconds");
}

private async Task AdvanceStalledTransactionsAsync(...)
{
    var stopwatch = Stopwatch.StartNew();

    var stalledTransactions = await GetStalledTransactionsAsync(...);
    _stalledTransactionsFound.Add(stalledTransactions.Count);

    foreach (var txId in stalledTransactions)
    {
        await AdvanceTransactionAsync(txId);
        _transactionsAdvanced.Add(1);
    }

    _watchdogDuration.Record(stopwatch.Elapsed.TotalSeconds);
}
```

### Health Checks (Future: Phase 7+)

Register health checks for watcher services:

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<TransactionRecoveryWatcherHealthCheck>("transaction-advancement")
    .AddCheck<TransactionCleanupWatcherHealthCheck>("transaction-cleanup");

// Ignixa.Api/HealthChecks/TransactionRecoveryWatcherHealthCheck.cs
public class TransactionRecoveryWatcherHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check if watcher has run recently (e.g., within last 5 minutes)
        // Check for excessive stalled transactions (indicates system problem)
        // Return Healthy, Degraded, or Unhealthy
    }
}
```

---

## 8. ADR Recommendations

### Should We Create a New ADR?

**Recommendation**: **NO - Update existing ADRs instead**

**Rationale**:
1. **ADR-2502 (Phase 1.1)**: Bundle processing already covers transaction management
2. **Background Jobs Investigation**: Already covered in `background-jobs-with-durabletask.md`
3. **Phase 1.1 Scope**: Watchdogs are simple IHostedService implementations (not complex enough for separate ADR)

### Recommended ADR Updates

#### Update ADR-2502 (Phase 1.1 - Bundle Processing)

Add section "Transaction Lifecycle and Watchers":

```markdown
### Transaction Lifecycle and Watchers

#### Transaction States
1. **In-Progress**: Lock file exists (`tx-{id}.lock.ndjson`)
2. **Committed**: Committed file exists (`tx-{id}.ndjson`)
3. **Stalled**: Lock file older than 5 minutes with no commit
4. **Failed**: Lock file with validation errors or partial writes

#### Background Watchers

**TransactionRecoveryWatcher**:
- Detects stalled transactions (orphaned lock files > 5 minutes old)
- Commits valid stalled transactions (rename lock file → committed file)
- Runs every 1 minute
- Uses IDistributedLockManager for multi-instance coordination

**TransactionCleanupWatcher**:
- Cleans up failed transactions older than 7 days (retention period)
- Deletes lock files, resource files, and metadata files
- Runs every 1 hour
- Uses IDistributedLockManager for multi-instance coordination

**Multi-Tenant Support**:
- Both watchers iterate all active tenants (skip Partition 0)
- Each tenant's transactions processed independently
- Errors in one tenant don't block others

**Distributed Locking**:
- Phase 1-7: File-based locks (single instance only)
- Phase 8+: Data-layer-specific distributed locks (multi-instance)
  - SQL Server: `sp_getapplock`
  - Cosmos DB: Lease containers

**Configuration**:
```json
{
  "BackgroundServices": {
    "TransactionRecovery": {
      "Enabled": true,
      "PollingIntervalMinutes": 1,
      "StallThresholdMinutes": 5
    },
    "TransactionCleanup": {
      "Enabled": true,
      "PollingIntervalHours": 1,
      "RetentionPeriodDays": 7
    }
  }
}
```
```

#### Update ADR-2500 (Master Roadmap)

Add to "Key Architectural Decisions":

```markdown
30. **Transaction Watcher Pattern**: IHostedService-based watchers with IDistributedLockManager abstraction for transaction recovery and cleanup (Phase 8+ uses data-layer-specific distributed locking, Phase 13+ may migrate to DurableTask)
```

---

## 9. Implementation Phases

### Phase 1.1 (Current) - Simple IHostedService Watchers

**Deliverables**:
1. `TransactionRecoveryWatcher` (IHostedService)
2. `TransactionCleanupWatcher` (IHostedService)
3. `IFhirRepository.GetStalledTransactionsAsync()` method
4. `IFhirRepository.GetFailedTransactionsAsync()` method
5. `IFhirRepository.DeleteTransactionArtifactsAsync()` method
6. `IDistributedLockManager` abstraction (Domain layer)
7. `FileSystemDistributedLockManager` implementation (single instance only)
8. Configuration in `appsettings.json`
9. Unit tests + integration tests

**Estimated Effort**: 20 hours (Week 2 of Phase 1.1)
- Day 1-2: Implement watcher classes + IFhirRepository methods
- Day 2-3: IDistributedLockManager abstraction + FileSystem implementation
- Day 3: Configuration + registration
- Day 4: Unit tests
- Day 5: Integration tests + manual testing

**Implementation Order**:
1. Add `IDistributedLockManager` and `IDistributedLock` interfaces (Domain layer)
2. Implement `FileSystemDistributedLockManager` (FileSystem data layer)
3. Add `GetStalledTransactionsAsync()` to `IFhirRepository` and `FileBasedFhirRepository`
4. Implement `TransactionRecoveryWatcher` with lock manager
5. Add `GetFailedTransactionsAsync()` and `DeleteTransactionArtifactsAsync()` to repository
6. Implement `TransactionCleanupWatcher` with lock manager
7. Add configuration support
8. Write tests
9. Manual testing with simulated failures

### Phase 8 (SQL Server Data Layer) - SQL Server Distributed Locking

**Deliverables**:
1. `SqlServerDistributedLockManager` implementation using `sp_getapplock`
2. Update watcher registration to use SQL Server lock manager
3. Health checks for watchers
4. Metrics for watcher operations

**Estimated Effort**: 8 hours (part of Phase 8 SQL Server implementation)

**Implementation Order**:
1. Implement `SqlServerDistributedLockManager` using `sp_getapplock`
2. Update `Program.cs` to register SQL Server lock manager
3. Test multi-instance coordination
4. Add health checks
5. Add metrics collection

### Phase 9 (Cosmos DB Data Layer) - Cosmos DB Distributed Locking

**Deliverables**:
1. `CosmosDbDistributedLockManager` implementation using lease containers
2. Update watcher registration to use Cosmos DB lock manager
3. Cosmos DB-specific optimizations

**Estimated Effort**: 8 hours (part of Phase 9 Cosmos DB implementation)

**Implementation Order**:
1. Implement `CosmosDbDistributedLockManager` using lease containers
2. Update `Program.cs` to register Cosmos DB lock manager
3. Test multi-instance coordination
4. Optimize lease renewal strategy

### Phase 13+ (Bulk Operations) - Migrate to DurableTask

**Deliverables**:
1. `TransactionRecoveryOrchestration` (DurableTask)
2. `TransactionCleanupOrchestration` (DurableTask)
3. Replace IHostedService watchers with DurableTask orchestrations
4. Persistent state management
5. Fault tolerance and retry logic

**Estimated Effort**: 24 hours (part of Phase 13 bulk operations)

**Benefits of Migration**:
- Persistent state (survives server restarts)
- Automatic retry and fault tolerance
- Built-in monitoring and observability
- Scalability across multiple workers
- DurableTask-native distributed locking

---

## 10. Risk Analysis and Mitigation

### Risk 1: False Positive Stalled Transactions

**Scenario**: Watchdog commits a transaction that's actually still in-progress (slow bundle)

**Likelihood**: Low (5-minute threshold should be sufficient)

**Impact**: Medium (potential data corruption)

**Mitigation**:
1. Use conservative stall threshold (5 minutes minimum)
2. Log all advancement actions with transaction details
3. Add safeguard: Check if process holding lock is still alive (future enhancement)

### Risk 2: Race Condition During Cleanup

**Scenario**: Cleanup watchdog deletes transaction artifacts while commit is in-progress

**Likelihood**: Very Low (requires precise timing)

**Impact**: High (data loss)

**Mitigation**:
1. Cleanup only processes transactions older than retention period (7 days)
2. Transaction advancement happens every 1 minute (cleanup happens every 1 hour)
3. File system operations are atomic (delete succeeds or fails completely)

### Risk 3: Multi-Instance Concurrent Watchers

**Scenario**: Multiple API instances run watchers simultaneously (duplicate work or conflicts)

**Likelihood**: High (in Phase 8+ multi-instance deployments with SQL/Cosmos)

**Impact**: Medium (wasted resources, potential conflicts)

**Mitigation**:
1. Phase 1-7: Single instance deployment (no issue with file-based locking)
2. Phase 8+: Implement distributed locking per data layer
   - SQL Server: `sp_getapplock` (session-based exclusive locks)
   - Cosmos DB: Lease containers (document-based distributed locks)
3. Alternative: Run watchers on dedicated worker service (not in API process)

### Risk 4: Cleanup Deletes Wrong Files

**Scenario**: Watcher deletes files from active transactions due to filesystem lag or bugs

**Likelihood**: Very Low (multiple safeguards in place)

**Impact**: High (data loss)

**Mitigation**:
1. Multi-step validation: Check lock file exists, no committed file, older than retention
2. Extensive logging before each deletion
3. Dry-run mode for testing (future enhancement)
4. Regular backups (operational concern)

---

## 11. Future Enhancements

### Enhancement 1: Dry-Run Mode

Add configuration to log actions without executing:

```json
{
  "BackgroundServices": {
    "TransactionCleanup": {
      "DryRun": true  // Log what would be deleted, don't actually delete
    }
  }
}
```

### Enhancement 2: Process Liveness Check

Check if process holding lock is still alive:

```csharp
private bool IsProcessAlive(string lockFileContent)
{
    // Lock file contains: {"ProcessId": 12345, "Timestamp": "..."}
    var lockInfo = JsonSerializer.Deserialize<LockInfo>(lockFileContent);

    try
    {
        var process = Process.GetProcessById(lockInfo.ProcessId);
        return !process.HasExited;
    }
    catch (ArgumentException)
    {
        return false; // Process doesn't exist
    }
}
```

### Enhancement 3: Transaction Health Dashboard

Create dashboard endpoint to monitor transaction health:

```csharp
// GET /_admin/transaction-health
app.MapGet("/_admin/transaction-health", async (IFhirRepositoryFactory factory) =>
{
    var tenants = await GetAllTenantsAsync();
    var health = new List<TenantTransactionHealth>();

    foreach (var tenant in tenants)
    {
        var repository = await factory.GetRepositoryAsync(tenant.TenantId);
        var stalled = await repository.GetStalledTransactionsAsync(TimeSpan.FromMinutes(5));
        var failed = await repository.GetFailedTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-7));

        health.Add(new TenantTransactionHealth
        {
            TenantId = tenant.TenantId,
            StalledCount = stalled.Count,
            FailedCount = failed.Count
        });
    }

    return Results.Ok(health);
});
```

### Enhancement 4: Alert Thresholds

Emit alerts when thresholds exceeded:

```csharp
if (stalledTransactions.Count > 10)
{
    _logger.LogWarning(
        "ALERT: Excessive stalled transactions for tenant {TenantId}: {Count} stalled (threshold: 10)",
        tenantId,
        stalledTransactions.Count);

    // Future: Emit alert to monitoring system (PagerDuty, Slack, etc.)
}
```

---

## 12. Summary and Recommendations

### Key Recommendations

1. ✅ **Use IHostedService pattern for all phases** (simple, testable, data-layer agnostic)
2. ✅ **Implement both watchers**: Transaction Recovery + Transaction Cleanup
3. ✅ **Conservative thresholds**: 5-minute stall threshold, 7-day retention period
4. ✅ **Multi-tenant aware**: Iterate all tenants, skip Partition 0
5. ✅ **Comprehensive logging**: Log all actions with transaction IDs and file paths
6. ✅ **Test with time mocking**: Use `ISystemClock` abstraction for time-based tests
7. ✅ **IDistributedLockManager abstraction**: Implement in Phase 1.1 (enables future data layers)
8. ⚠️ **SQL Server locking required in Phase 8** (sp_getapplock for multi-instance coordination)
9. ⚠️ **Cosmos DB locking required in Phase 9** (lease containers for multi-instance coordination)
10. ⚠️ **Defer DurableTask migration to Phase 13+** (when bulk operations require persistent workflows)

### Implementation Priorities

**Phase 1.1 (Now - 20 hours)**:
1. `TransactionRecoveryWatcher` (HIGH priority - prevents data loss)
2. `TransactionCleanupWatcher` (MEDIUM priority - prevents disk bloat)
3. `IDistributedLockManager` abstraction + FileSystem implementation
4. Unit + integration tests

**Phase 8 (SQL Server - 8 hours)**:
1. `SqlServerDistributedLockManager` using `sp_getapplock`
2. Health checks and metrics for watchers
3. Multi-instance testing

**Phase 9 (Cosmos DB - 8 hours)**:
1. `CosmosDbDistributedLockManager` using lease containers
2. Cosmos DB-specific optimizations

**Phase 13+ (Bulk Operations - 24 hours)**:
1. Migrate to DurableTask orchestrations (if needed for bulk operations)

### Architectural Impact

**ADR-2500 Update**:
- Add architectural decision #30: Transaction Watcher Pattern (IHostedService with distributed locking)

**ADR-2502 Update**:
- Add "Transaction Lifecycle and Watchers" section

**No new ADR needed**: Watchers are implementation details of existing transaction system.

### Data Layer Implementations

| Data Layer | Watcher Pattern | Locking Implementation | Multi-Instance |
|------------|----------------|------------------------|----------------|
| FileSystem (Phase 1) | IHostedService | File-based locks | ❌ Single instance only |
| SQL Server (Phase 8) | IHostedService | sp_getapplock | ✅ Multi-instance supported |
| Cosmos DB (Phase 9) | IHostedService | Lease containers | ✅ Multi-instance supported |
| DurableTask (Phase 13+) | Orchestrations | Built-in coordination | ✅ Multi-instance native |

---

## References

1. **Microsoft FHIR Server Watchdogs**:
   - [TransactionWatchdog.cs](https://github.com/microsoft/fhir-server/blob/main/src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/TransactionWatchdog.cs)
   - [InvisibleHistoryCleanupWatchdog.cs](https://github.com/microsoft/fhir-server/blob/main/src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/InvisibleHistoryCleanupWatchdog.cs)

2. **Existing Investigations**:
   - `docs/investigations/background-jobs-with-durabletask.md` - DurableTask framework analysis
   - `docs/investigations/bundle-deferred-writes.md` - Two-phase channel architecture

3. **Existing ADRs**:
   - `docs/adr/adr-2500-master-implementation-roadmap.md` - Master roadmap
   - `docs/adr/adr-2502-phase1.1-bundle-processing.md` - Bundle processing and transactions
   - `docs/adr/adr-2523-phase20-multi-tenancy-data-partitioning.md` - Multi-tenancy architecture

4. **ASP.NET Core Background Services**:
   - [IHostedService documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
   - [BackgroundService base class](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.backgroundservice)

---

This investigation provides a complete analysis of background job patterns for transaction management and cleanup, tailored specifically to the FHIR Server v2 architecture with multi-tenancy support.

**Key Outcomes**:
1. **Naming**: "Watcher" pattern (TransactionRecoveryWatcher, TransactionCleanupWatcher) for clarity
2. **Architecture**: IHostedService-based background services with IDistributedLockManager abstraction
3. **Phases**: Phase 1.1 (file-based), Phase 8 (SQL Server sp_getapplock), Phase 9 (Cosmos DB leases)
4. **Multi-Instance**: Distributed locking required before SQL/Cosmos adoption (Phase 8+)

The recommendations prioritize simplicity for Phase 1.1 while providing a clear path to production-grade fault tolerance and multi-instance coordination in later phases.
