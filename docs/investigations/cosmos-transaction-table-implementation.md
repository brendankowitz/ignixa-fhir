# Cosmos DB Transaction Table Implementation

This document outlines the Cosmos DB implementation of the transaction table abstraction, leveraging the latest Cosmos DB features and the three-container architecture from the storage proposal.

## Container Architecture Integration

### Transaction Container Design

Building on the existing three-container architecture, we add a dedicated transaction container:

```
┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐
│   Resources         │  │   SearchIndices     │  │   SearchAggregates  │  │   Transactions      │
│   Container         │  │   Container         │  │   Container         │  │   Container         │
├─────────────────────┤  ├─────────────────────┤  ├─────────────────────┤  ├─────────────────────┤
│ • Raw FHIR JSON     │  │ • Compact indices   │  │ • Cross-partition   │  │ • Transaction log   │
│ • 20GB per logical  │  │ • Query-optimized   │  │   search results    │  │ • Append-only       │
│   partition         │  │ • Parameter-keyed   │  │ • Cached counts     │  │ • Heartbeat tracking│
│ • Key-based reads   │  │ • Range queries     │  │ • Statistics        │  │ • Visibility state  │
└─────────────────────┘  └─────────────────────┘  └─────────────────────┘  └─────────────────────┘
```

### Transaction Document Structure

```json
{
  "id": "tx-1730934000000640001",
  "pk": "transactions|2024-11|tenant-1",
  "type": "transaction",
  "transactionId": 1730934000000640001,
  "tenantId": "tenant-1",
  "range": {
    "firstValue": 1730934000000640001,
    "lastValue": 1730934000000640050,
    "count": 50
  },
  "definition": "Bundle transaction with 50 resources",
  "state": {
    "isCompleted": false,
    "isSuccess": false,
    "isVisible": false,
    "isHistoryMoved": false,
    "isControlledByClient": true
  },
  "timestamps": {
    "createDate": "2024-11-07T10:30:00.000Z",
    "endDate": null,
    "visibleDate": null,
    "historyMovedDate": null,
    "heartbeatDate": "2024-11-07T10:30:05.123Z",
    "invisibleHistoryRemovedDate": null
  },
  "failureReason": null,
  "_etag": "\"00000000-0000-0000-0000-000000000000\"",
  "ttl": -1
}
```

### Partitioning Strategy

```csharp
// Partition Key: transactions|Year-Month|TenantId
public static string CreateTransactionPartitionKey(string tenantId, DateTime timestamp)
{
    return $"transactions|{timestamp:yyyy-MM}|{tenantId}";
}
```

**Benefits:**
- Monthly partitions prevent excessive growth
- Tenant isolation at partition level
- Natural cleanup of old transaction data
- Efficient range queries within partitions

## Cosmos DB Implementation

### Core Repository Implementation

```csharp
public class CosmosTransactionRepository : ITransactionRepository
{
    private readonly Container _transactionContainer;
    private readonly ISequenceProvider _sequenceProvider;
    private readonly CosmosTransactionOptions _options;
    private readonly ILogger<CosmosTransactionRepository> _logger;

    public CosmosTransactionRepository(
        Container transactionContainer,
        ISequenceProvider sequenceProvider,
        IOptions<CosmosTransactionOptions> options,
        ILogger<CosmosTransactionRepository> logger)
    {
        _transactionContainer = transactionContainer;
        _sequenceProvider = sequenceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<TransactionEntry> BeginTransactionAsync(
        int resourceCount,
        string? definition = null,
        CancellationToken cancellationToken = default)
    {
        // Get sequence range for resource IDs
        var sequenceFirst = await _sequenceProvider.GetNextRangeAsync(resourceCount, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;

        // Generate transaction ID: timestamp (milliseconds) * 80000 + sequence
        var transactionId = new TransactionId(timestamp.ToUnixTimeMilliseconds() * 80000 + sequenceFirst);

        var document = new CosmosTransactionDocument
        {
            Id = $"tx-{transactionId}",
            PartitionKey = CreateTransactionPartitionKey(_options.TenantId, timestamp.DateTime),
            Type = "transaction",
            TransactionId = transactionId,
            TenantId = _options.TenantId,
            Range = new TransactionRangeDocument
            {
                FirstValue = transactionId,
                LastValue = transactionId + resourceCount - 1,
                Count = resourceCount
            },
            Definition = definition,
            State = new TransactionStateDocument
            {
                IsCompleted = false,
                IsSuccess = false,
                IsVisible = false,
                IsHistoryMoved = false,
                IsControlledByClient = true
            },
            Timestamps = new TransactionTimestampsDocument
            {
                CreateDate = timestamp,
                HeartbeatDate = timestamp
            }
        };

        try
        {
            var response = await _transactionContainer.CreateItemAsync(
                document,
                new PartitionKey(document.PartitionKey),
                cancellationToken: cancellationToken);

            return document.ToTransactionEntry();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Transaction ID collision - retry with new sequence
            _logger.LogWarning("Transaction ID collision for {TransactionId}, retrying", transactionId);
            return await BeginTransactionAsync(resourceCount, definition, cancellationToken);
        }
    }

    public async ValueTask UpdateHeartbeatAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken = default)
    {
        var documentId = $"tx-{transactionId}";
        var partitionKey = await GetPartitionKeyForTransactionAsync(transactionId, cancellationToken);

        // Use patch operation for efficient heartbeat update
        var patchOperations = new[]
        {
            PatchOperation.Set("/timestamps/heartbeatDate", DateTimeOffset.UtcNow)
        };

        try
        {
            await _transactionContainer.PatchItemAsync<CosmosTransactionDocument>(
                documentId,
                new PartitionKey(partitionKey),
                patchOperations,
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Transaction {TransactionId} not found for heartbeat update", transactionId);
            throw new TransactionNotFoundException(transactionId);
        }
    }

    public async ValueTask CommitTransactionAsync(
        TransactionId transactionId,
        bool isSuccess,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var documentId = $"tx-{transactionId}";
        var partitionKey = await GetPartitionKeyForTransactionAsync(transactionId, cancellationToken);
        var endDate = DateTimeOffset.UtcNow;

        var patchOperations = new List<PatchOperation>
        {
            PatchOperation.Set("/state/isCompleted", true),
            PatchOperation.Set("/state/isSuccess", isSuccess),
            PatchOperation.Set("/timestamps/endDate", endDate)
        };

        if (!string.IsNullOrEmpty(failureReason))
        {
            patchOperations.Add(PatchOperation.Set("/failureReason", failureReason));
        }

        try
        {
            await _transactionContainer.PatchItemAsync<CosmosTransactionDocument>(
                documentId,
                new PartitionKey(partitionKey),
                patchOperations,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Transaction {TransactionId} committed with success={Success}",
                transactionId, isSuccess);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TransactionNotFoundException(transactionId);
        }
    }

    public async ValueTask<TransactionVisibilityState> GetVisibilityStateAsync(
        CancellationToken cancellationToken = default)
    {
        // Query across all partitions for the minimum visible transaction
        // Use cross-partition query with continuation token for efficiency
        var query = new QueryDefinition(
            "SELECT MIN(c.transactionId) as minVisible, MAX(c.transactionId) as maxVisible " +
            "FROM c WHERE c.type = 'transaction' AND c.state.isVisible = true");

        using var iterator = _transactionContainer.GetItemQueryIterator<dynamic>(query);

        var minVisible = long.MaxValue;
        var maxVisible = long.MinValue;

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);

            foreach (var item in response)
            {
                if (item.minVisible != null)
                {
                    minVisible = Math.Min(minVisible, (long)item.minVisible);
                }
                if (item.maxVisible != null)
                {
                    maxVisible = Math.Max(maxVisible, (long)item.maxVisible);
                }
            }
        }

        return new TransactionVisibilityState(
            minVisible == long.MaxValue ? new TransactionId(0) : new TransactionId(minVisible),
            maxVisible == long.MinValue ? new TransactionId(0) : new TransactionId(maxVisible));
    }

    public async ValueTask<int> AdvanceVisibilityAsync(CancellationToken cancellationToken = default)
    {
        var visibilityState = await GetVisibilityStateAsync(cancellationToken);
        var nextVisibleId = visibilityState.MaxVisibleId + 1;

        // Find the highest contiguous completed transaction sequence
        var query = new QueryDefinition(
            "SELECT c.transactionId, c.state.isCompleted " +
            "FROM c WHERE c.type = 'transaction' " +
            "AND c.transactionId >= @nextId " +
            "ORDER BY c.transactionId")
            .WithParameter("@nextId", (long)nextVisibleId);

        using var iterator = _transactionContainer.GetItemQueryIterator<dynamic>(query);

        var transactionsToMakeVisible = new List<TransactionId>();
        var expectedNextId = nextVisibleId;

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);

            foreach (var item in response)
            {
                var transactionId = new TransactionId((long)item.transactionId);
                var isCompleted = (bool)item.isCompleted;

                if (transactionId == expectedNextId && isCompleted)
                {
                    transactionsToMakeVisible.Add(transactionId);
                    expectedNextId = transactionId + 1;
                }
                else
                {
                    // Found gap or incomplete transaction - stop processing
                    goto ProcessVisibility;
                }
            }
        }

    ProcessVisibility:
        // Use bulk operations to update visibility efficiently
        if (transactionsToMakeVisible.Count > 0)
        {
            return await BulkUpdateVisibilityAsync(transactionsToMakeVisible, cancellationToken);
        }

        return 0;
    }

    private async ValueTask<int> BulkUpdateVisibilityAsync(
        IReadOnlyList<TransactionId> transactionIds,
        CancellationToken cancellationToken)
    {
        var visibleDate = DateTimeOffset.UtcNow;
        var updateTasks = new List<Task>();

        // Process in batches to respect RU limits
        const int batchSize = 100;
        var batches = transactionIds.Chunk(batchSize);

        foreach (var batch in batches)
        {
            var batchTasks = batch.Select(async transactionId =>
            {
                try
                {
                    var documentId = $"tx-{transactionId}";
                    var partitionKey = await GetPartitionKeyForTransactionAsync(transactionId, cancellationToken);

                    var patchOperations = new[]
                    {
                        PatchOperation.Set("/state/isVisible", true),
                        PatchOperation.Set("/timestamps/visibleDate", visibleDate)
                    };

                    await _transactionContainer.PatchItemAsync<CosmosTransactionDocument>(
                        documentId,
                        new PartitionKey(partitionKey),
                        patchOperations,
                        cancellationToken: cancellationToken);
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Transaction {TransactionId} not found during visibility update", transactionId);
                }
            });

            updateTasks.AddRange(batchTasks);
        }

        await Task.WhenAll(updateTasks);

        _logger.LogInformation("Advanced visibility for {Count} transactions", transactionIds.Count);
        return transactionIds.Count;
    }

    public async ValueTask<IReadOnlyList<TransactionTimeoutInfo>> GetTimeoutTransactionsAsync(
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken = default)
    {
        var timeoutThreshold = DateTimeOffset.UtcNow - timeoutDuration;

        var query = new QueryDefinition(
            "SELECT c.transactionId, c.timestamps.heartbeatDate " +
            "FROM c WHERE c.type = 'transaction' " +
            "AND c.state.isCompleted = false " +
            "AND c.timestamps.heartbeatDate < @threshold")
            .WithParameter("@threshold", timeoutThreshold);

        using var iterator = _transactionContainer.GetItemQueryIterator<dynamic>(query);

        var timeoutTransactions = new List<TransactionTimeoutInfo>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);

            foreach (var item in response)
            {
                var transactionId = new TransactionId((long)item.transactionId);
                var heartbeatDate = (DateTimeOffset)item.heartbeatDate;
                var timeSinceHeartbeat = DateTimeOffset.UtcNow - heartbeatDate;

                timeoutTransactions.Add(new TransactionTimeoutInfo(transactionId, timeSinceHeartbeat));
            }
        }

        return timeoutTransactions;
    }

    private async ValueTask<string> GetPartitionKeyForTransactionAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        // Extract timestamp from transaction ID to determine partition
        var timestampMs = (long)transactionId / 80000;
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);

        return CreateTransactionPartitionKey(_options.TenantId, timestamp.DateTime);
    }
}
```

### Document Models

```csharp
public class CosmosTransactionDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; } = "transaction";

    [JsonPropertyName("transactionId")]
    public required TransactionId TransactionId { get; init; }

    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    [JsonPropertyName("range")]
    public required TransactionRangeDocument Range { get; init; }

    [JsonPropertyName("definition")]
    public string? Definition { get; init; }

    [JsonPropertyName("state")]
    public required TransactionStateDocument State { get; init; }

    [JsonPropertyName("timestamps")]
    public required TransactionTimestampsDocument Timestamps { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }

    [JsonPropertyName("ttl")]
    public int? TimeToLive { get; init; } = -1; // Never expire by default

    public TransactionEntry ToTransactionEntry() => new()
    {
        Id = TransactionId,
        Range = new TransactionRange(Range.FirstValue, Range.LastValue, Range.Count),
        Definition = Definition,
        IsCompleted = State.IsCompleted,
        IsSuccess = State.IsSuccess,
        IsVisible = State.IsVisible,
        IsHistoryMoved = State.IsHistoryMoved,
        CreateDate = Timestamps.CreateDate,
        EndDate = Timestamps.EndDate,
        VisibleDate = Timestamps.VisibleDate,
        HistoryMovedDate = Timestamps.HistoryMovedDate,
        HeartbeatDate = Timestamps.HeartbeatDate,
        FailureReason = FailureReason,
        IsControlledByClient = State.IsControlledByClient,
        InvisibleHistoryRemovedDate = Timestamps.InvisibleHistoryRemovedDate
    };
}

public record TransactionRangeDocument(
    [property: JsonPropertyName("firstValue")] TransactionId FirstValue,
    [property: JsonPropertyName("lastValue")] TransactionId LastValue,
    [property: JsonPropertyName("count")] int Count);

public record TransactionStateDocument(
    [property: JsonPropertyName("isCompleted")] bool IsCompleted,
    [property: JsonPropertyName("isSuccess")] bool IsSuccess,
    [property: JsonPropertyName("isVisible")] bool IsVisible,
    [property: JsonPropertyName("isHistoryMoved")] bool IsHistoryMoved,
    [property: JsonPropertyName("isControlledByClient")] bool IsControlledByClient);

public record TransactionTimestampsDocument(
    [property: JsonPropertyName("createDate")] DateTimeOffset CreateDate,
    [property: JsonPropertyName("endDate")] DateTimeOffset? EndDate = null,
    [property: JsonPropertyName("visibleDate")] DateTimeOffset? VisibleDate = null,
    [property: JsonPropertyName("historyMovedDate")] DateTimeOffset? HistoryMovedDate = null,
    [property: JsonPropertyName("heartbeatDate")] DateTimeOffset HeartbeatDate = default,
    [property: JsonPropertyName("invisibleHistoryRemovedDate")] DateTimeOffset? InvisibleHistoryRemovedDate = null);
```

### Sequence Provider for Cosmos DB

```csharp
public interface ISequenceProvider
{
    ValueTask<int> GetNextRangeAsync(int count, CancellationToken cancellationToken = default);
}

public class CosmosSequenceProvider : ISequenceProvider
{
    private readonly Container _sequenceContainer;
    private readonly string _tenantId;
    private readonly SemaphoreSlim _sequenceLock = new(1, 1);

    public CosmosSequenceProvider(Container sequenceContainer, string tenantId)
    {
        _sequenceContainer = sequenceContainer;
        _tenantId = tenantId;
    }

    public async ValueTask<int> GetNextRangeAsync(int count, CancellationToken cancellationToken = default)
    {
        await _sequenceLock.WaitAsync(cancellationToken);
        try
        {
            var sequenceId = $"sequence-{_tenantId}";
            var partitionKey = new PartitionKey(_tenantId);

            // Try to get existing sequence
            CosmosSequenceDocument? sequence;
            try
            {
                var response = await _sequenceContainer.ReadItemAsync<CosmosSequenceDocument>(
                    sequenceId, partitionKey, cancellationToken: cancellationToken);
                sequence = response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Create new sequence starting at 1
                sequence = new CosmosSequenceDocument
                {
                    Id = sequenceId,
                    TenantId = _tenantId,
                    CurrentValue = 0
                };

                await _sequenceContainer.CreateItemAsync(sequence, partitionKey, cancellationToken: cancellationToken);
            }

            // Update sequence with new range
            var firstValue = sequence.CurrentValue + 1;
            var newCurrentValue = sequence.CurrentValue + count;

            var updatedSequence = sequence with { CurrentValue = newCurrentValue };

            await _sequenceContainer.ReplaceItemAsync(
                updatedSequence,
                sequenceId,
                partitionKey,
                cancellationToken: cancellationToken);

            return firstValue;
        }
        finally
        {
            _sequenceLock.Release();
        }
    }
}

public record CosmosSequenceDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("currentValue")] int CurrentValue);
```

## Performance Optimizations

### 1. Efficient Heartbeat Updates

```csharp
public class OptimizedHeartbeatService : BackgroundService
{
    private readonly ITransactionRepository _repository;
    private readonly ConcurrentDictionary<TransactionId, DateTimeOffset> _pendingHeartbeats = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingHeartbeatsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public void QueueHeartbeat(TransactionId transactionId)
    {
        _pendingHeartbeats.TryAdd(transactionId, DateTimeOffset.UtcNow);
    }

    private async Task ProcessPendingHeartbeatsAsync(CancellationToken cancellationToken)
    {
        if (_pendingHeartbeats.IsEmpty) return;

        var heartbeats = _pendingHeartbeats.ToArray();
        _pendingHeartbeats.Clear();

        var tasks = heartbeats.Select(async kvp =>
        {
            try
            {
                await _repository.UpdateHeartbeatAsync(kvp.Key, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log error but don't fail the batch
                // Transaction will timeout naturally if heartbeat fails
            }
        });

        await Task.WhenAll(tasks);
    }
}
```

### 2. Visibility State Caching

```csharp
public class CachedTransactionRepository : ITransactionRepository
{
    private readonly ITransactionRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _visibilityLock = new(1, 1);

    private static readonly MemoryCacheEntryOptions VisibilityCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5),
        SlidingExpiration = TimeSpan.FromSeconds(2)
    };

    public async ValueTask<TransactionVisibilityState> GetVisibilityStateAsync(
        CancellationToken cancellationToken = default)
    {
        const string cacheKey = "transaction_visibility_state";

        if (_cache.TryGetValue(cacheKey, out TransactionVisibilityState? cached))
        {
            return cached!;
        }

        await _visibilityLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(cacheKey, out cached))
            {
                return cached!;
            }

            var state = await _inner.GetVisibilityStateAsync(cancellationToken);
            _cache.Set(cacheKey, state, VisibilityCacheOptions);
            return state;
        }
        finally
        {
            _visibilityLock.Release();
        }
    }

    public async ValueTask<int> AdvanceVisibilityAsync(CancellationToken cancellationToken = default)
    {
        var result = await _inner.AdvanceVisibilityAsync(cancellationToken);

        // Invalidate cache after advancing visibility
        if (result > 0)
        {
            _cache.Remove("transaction_visibility_state");
        }

        return result;
    }

    // Delegate other methods to inner repository...
}
```

## Container Configuration

### Transaction Container Setup

```csharp
public static class CosmosTransactionContainerConfiguration
{
    public static async Task<Container> ConfigureTransactionContainerAsync(
        Database database,
        string tenantId,
        int throughputRU = 1000)
    {
        var containerProperties = new ContainerProperties
        {
            Id = $"transactions-{tenantId}",
            PartitionKeyPath = "/pk",
            DefaultTimeToLive = -1, // No automatic expiration

            // Indexing policy optimized for transaction queries
            IndexingPolicy = new IndexingPolicy
            {
                IndexingMode = IndexingMode.Consistent,
                Automatic = true,
                IncludedPaths = { new IncludedPath { Path = "/*" } },
                ExcludedPaths =
                {
                    new ExcludedPath { Path = "/definition/*" },
                    new ExcludedPath { Path = "/failureReason/*" }
                },
                CompositeIndexes =
                {
                    new Collection<CompositePath>
                    {
                        new() { Path = "/type", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/state/isVisible", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/transactionId", Order = CompositePathSortOrder.Ascending }
                    },
                    new Collection<CompositePath>
                    {
                        new() { Path = "/type", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/state/isCompleted", Order = CompositePathSortOrder.Ascending },
                        new() { Path = "/timestamps/heartbeatDate", Order = CompositePathSortOrder.Ascending }
                    }
                }
            }
        };

        var response = await database.CreateContainerIfNotExistsAsync(
            containerProperties,
            throughputRU);

        return response.Container;
    }
}
```

This Cosmos DB implementation provides the same transaction semantics as the SQL Server version while leveraging Cosmos DB's global distribution, automatic scaling, and optimized indexing capabilities. The design ensures consistency, performance, and maintainability while supporting the multi-tenant architecture.