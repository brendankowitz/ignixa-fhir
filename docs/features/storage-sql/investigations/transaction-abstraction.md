# Investigation: Transaction Table Core Abstraction

**Feature**: sql-storage
**Status**: Viable
**Created**: 2025-10-22
**Original ADR**: N/A

This document outlines the provider-agnostic transaction table abstraction based on the SQL Server implementation patterns, designed to work with any data layer including Cosmos DB, SQL Server, and others.

## Core Transaction Concepts

### Transaction Table Schema Analysis

Based on the SQL Server implementation, the transaction table serves as an append-only log with these key properties:

**Transaction States:**
- `IsCompleted`: Transaction has finished processing (success or failure)
- `IsSuccess`: Transaction completed successfully (only valid when IsCompleted=true)
- `IsVisible`: Transaction data is visible to read operations
- `IsHistoryMoved`: Historical data has been processed/archived
- `IsControlledByClient`: Transaction can be managed by client operations

**Transaction Lifecycle:**
1. **Begin**: Create transaction entry with ID range allocation
2. **Heartbeat**: Keep-alive updates during processing
3. **Commit**: Mark transaction as completed (success/failure)
4. **Visibility**: Watchdog advances visibility for completed sequential transactions
5. **Cleanup**: Archive or remove old transaction metadata

### Key Design Insights

**Append-Only Transaction Log:**
- Each transaction gets a unique ID based on timestamp + sequence
- Transaction entries are never deleted, only marked with status flags
- Enables point-in-time recovery and audit trails

**Sequential Visibility Advancement:**
- Transactions become visible only when all prior transactions are completed
- Prevents read inconsistencies from out-of-order transaction completion
- Watchdog process continuously advances the visibility watermark

**Heartbeat-Based Timeout Detection:**
- Active transactions update heartbeat timestamp
- Watchdog identifies stale transactions based on heartbeat timeout
- Enables recovery from crashed or abandoned transactions

## Provider-Agnostic Abstractions

### Core Transaction Types

```csharp
public record TransactionId(long Value)
{
    public static TransactionId Generate(int sequenceValue) =>
        new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 80000 + sequenceValue);

    public static implicit operator long(TransactionId id) => id.Value;
    public static implicit operator TransactionId(long value) => new(value);
}

public record TransactionRange(TransactionId FirstId, TransactionId LastId, int Count)
{
    public bool Contains(TransactionId id) => id >= FirstId && id <= LastId;
}

public record TransactionEntry
{
    public required TransactionId Id { get; init; }
    public required TransactionRange Range { get; init; }
    public string? Definition { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsSuccess { get; init; }
    public bool IsVisible { get; init; }
    public bool IsHistoryMoved { get; init; }
    public DateTimeOffset CreateDate { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndDate { get; init; }
    public DateTimeOffset? VisibleDate { get; init; }
    public DateTimeOffset? HistoryMovedDate { get; init; }
    public DateTimeOffset HeartbeatDate { get; init; } = DateTimeOffset.UtcNow;
    public string? FailureReason { get; init; }
    public bool IsControlledByClient { get; init; } = true;
    public DateTimeOffset? InvisibleHistoryRemovedDate { get; init; }
}

public record TransactionVisibilityState(TransactionId MinVisibleId, TransactionId MaxVisibleId);

public record TransactionTimeoutInfo(TransactionId Id, TimeSpan TimeSinceHeartbeat);
```

### Core Transaction Repository Interface

```csharp
public interface ITransactionRepository
{
    /// <summary>
    /// Begin a new transaction with allocated ID range
    /// </summary>
    ValueTask<TransactionEntry> BeginTransactionAsync(
        int resourceCount,
        string? definition = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update transaction heartbeat to indicate active processing
    /// </summary>
    ValueTask UpdateHeartbeatAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark transaction as completed (success or failure)
    /// </summary>
    ValueTask CommitTransactionAsync(
        TransactionId transactionId,
        bool isSuccess,
        string? failureReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current transaction visibility watermark
    /// </summary>
    ValueTask<TransactionVisibilityState> GetVisibilityStateAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advance transaction visibility for completed sequential transactions
    /// </summary>
    ValueTask<int> AdvanceVisibilityAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find transactions that have exceeded heartbeat timeout
    /// </summary>
    ValueTask<IReadOnlyList<TransactionTimeoutInfo>> GetTimeoutTransactionsAsync(
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transaction by ID
    /// </summary>
    ValueTask<TransactionEntry?> GetTransactionAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get resources associated with a transaction
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetTransactionResourcesAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken = default);
}
```

### Transaction Watchdog Service

```csharp
public interface ITransactionWatchdog
{
    /// <summary>
    /// Start the watchdog background service
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the watchdog background service
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Force visibility advancement check
    /// </summary>
    ValueTask<int> ProcessVisibilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Process timeout transactions
    /// </summary>
    ValueTask<int> ProcessTimeoutsAsync(CancellationToken cancellationToken = default);
}

public class TransactionWatchdogService : BackgroundService, ITransactionWatchdog
{
    private readonly ITransactionRepository _repository;
    private readonly ILogger<TransactionWatchdogService> _logger;
    private readonly TransactionWatchdogOptions _options;

    public TransactionWatchdogService(
        ITransactionRepository repository,
        IOptions<TransactionWatchdogOptions> options,
        ILogger<TransactionWatchdogService> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessVisibilityAsync(stoppingToken);
                await ProcessTimeoutsAsync(stoppingToken);

                await Task.Delay(_options.CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction watchdog encountered an error");
                await Task.Delay(_options.ErrorRetryDelay, stoppingToken);
            }
        }
    }

    public async ValueTask<int> ProcessVisibilityAsync(CancellationToken cancellationToken = default)
    {
        var advancedCount = await _repository.AdvanceVisibilityAsync(cancellationToken);

        if (advancedCount > 0)
        {
            _logger.LogInformation("Advanced visibility for {Count} transactions", advancedCount);
        }

        return advancedCount;
    }

    public async ValueTask<int> ProcessTimeoutsAsync(CancellationToken cancellationToken = default)
    {
        var timeouts = await _repository.GetTimeoutTransactionsAsync(_options.TransactionTimeout, cancellationToken);

        var processedCount = 0;
        foreach (var timeout in timeouts)
        {
            try
            {
                await _repository.CommitTransactionAsync(
                    timeout.Id,
                    false,
                    $"Transaction timed out after {timeout.TimeSinceHeartbeat}",
                    cancellationToken);

                processedCount++;
                _logger.LogWarning("Timed out transaction {TransactionId} after {Duration}",
                    timeout.Id, timeout.TimeSinceHeartbeat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to timeout transaction {TransactionId}", timeout.Id);
            }
        }

        return processedCount;
    }
}

public class TransactionWatchdogOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan TransactionTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan ErrorRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseTimeout { get; set; } = TimeSpan.FromSeconds(20);
}
```

### Transaction Context Provider

```csharp
public interface ITransactionContext
{
    TransactionId? CurrentTransactionId { get; }
    TransactionEntry? CurrentTransaction { get; }

    /// <summary>
    /// Begin a new transaction scope
    /// </summary>
    ValueTask<ITransactionScope> BeginTransactionAsync(
        int resourceCount,
        string? definition = null,
        CancellationToken cancellationToken = default);
}

public interface ITransactionScope : IAsyncDisposable
{
    TransactionId TransactionId { get; }
    TransactionEntry Transaction { get; }

    /// <summary>
    /// Update heartbeat for active transaction
    /// </summary>
    ValueTask UpdateHeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit transaction as successful
    /// </summary>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit transaction as failed
    /// </summary>
    ValueTask FailAsync(string reason, CancellationToken cancellationToken = default);
}

public class TransactionContextProvider : ITransactionContext
{
    private readonly ITransactionRepository _repository;
    private readonly AsyncLocal<TransactionEntry?> _currentTransaction = new();

    public TransactionId? CurrentTransactionId => _currentTransaction.Value?.Id;
    public TransactionEntry? CurrentTransaction => _currentTransaction.Value;

    public TransactionContextProvider(ITransactionRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<ITransactionScope> BeginTransactionAsync(
        int resourceCount,
        string? definition = null,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _repository.BeginTransactionAsync(resourceCount, definition, cancellationToken);
        _currentTransaction.Value = transaction;

        return new TransactionScope(_repository, transaction, () => _currentTransaction.Value = null);
    }
}

internal class TransactionScope : ITransactionScope
{
    private readonly ITransactionRepository _repository;
    private readonly Action _onDispose;
    private bool _isCommitted;
    private bool _isDisposed;

    public TransactionId TransactionId { get; }
    public TransactionEntry Transaction { get; private set; }

    public TransactionScope(ITransactionRepository repository, TransactionEntry transaction, Action onDispose)
    {
        _repository = repository;
        Transaction = transaction;
        TransactionId = transaction.Id;
        _onDispose = onDispose;
    }

    public async ValueTask UpdateHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(TransactionScope));

        await _repository.UpdateHeartbeatAsync(TransactionId, cancellationToken);
        Transaction = Transaction with { HeartbeatDate = DateTimeOffset.UtcNow };
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(TransactionScope));
        if (_isCommitted) return;

        await _repository.CommitTransactionAsync(TransactionId, true, cancellationToken: cancellationToken);
        _isCommitted = true;
        Transaction = Transaction with
        {
            IsCompleted = true,
            IsSuccess = true,
            EndDate = DateTimeOffset.UtcNow
        };
    }

    public async ValueTask FailAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(TransactionScope));
        if (_isCommitted) return;

        await _repository.CommitTransactionAsync(TransactionId, false, reason, cancellationToken);
        _isCommitted = true;
        Transaction = Transaction with
        {
            IsCompleted = true,
            IsSuccess = false,
            EndDate = DateTimeOffset.UtcNow,
            FailureReason = reason
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        try
        {
            if (!_isCommitted)
            {
                await FailAsync("Transaction scope disposed without explicit commit");
            }
        }
        finally
        {
            _onDispose();
            _isDisposed = true;
        }
    }
}
```

## Usage Patterns

### Basic Transaction Usage

```csharp
public class FhirResourceService
{
    private readonly ITransactionContext _transactionContext;
    private readonly IFhirRepository _repository;

    public async ValueTask<ResourceWrapper> CreateResourceAsync(
        ResourceWrapper resource,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await _transactionContext.BeginTransactionAsync(1, "CreateResource", cancellationToken);

        try
        {
            var result = await _repository.CreateAsync(resource, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.FailAsync(ex.Message, cancellationToken);
            throw;
        }
    }
}
```

### Bundle Transaction Usage

```csharp
public class BundleProcessor
{
    private readonly ITransactionContext _transactionContext;
    private readonly IFhirRepository _repository;

    public async ValueTask<Bundle> ProcessTransactionBundleAsync(
        Bundle bundle,
        CancellationToken cancellationToken = default)
    {
        var entryCount = bundle.Entry?.Count ?? 0;
        using var transaction = await _transactionContext.BeginTransactionAsync(entryCount, "BundleTransaction", cancellationToken);

        try
        {
            var results = new List<BundleEntry>();

            foreach (var entry in bundle.Entry ?? [])
            {
                // Update heartbeat periodically for long operations
                await transaction.UpdateHeartbeatAsync(cancellationToken);

                var result = await ProcessBundleEntryAsync(entry, cancellationToken);
                results.Add(result);
            }

            await transaction.CommitAsync(cancellationToken);

            return new Bundle
            {
                Type = Bundle.BundleType.TransactionResponse,
                Entry = results
            };
        }
        catch (Exception ex)
        {
            await transaction.FailAsync(ex.Message, cancellationToken);
            throw;
        }
    }
}
```

This abstraction provides a clean, provider-agnostic foundation that can be implemented for any storage backend while maintaining the same transaction semantics and consistency guarantees as the original SQL Server implementation.