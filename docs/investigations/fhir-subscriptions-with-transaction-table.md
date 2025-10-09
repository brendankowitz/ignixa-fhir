# Investigation: FHIR Subscriptions with Transaction Table Integration

## Problem Statement

FHIR Subscriptions enable real-time notifications when resources matching specific criteria are created or updated. The challenge is implementing subscriptions that:

1. **Never miss events**: Process ALL transactions, even during server restarts
2. **Process in order**: Maintain transaction ordering for consistency
3. **Scale horizontally**: Support distributed processing across multiple nodes
4. **Support multiple channels**: rest-hook, websocket, email, storage, etc.
5. **Handle failures gracefully**: Retry logic, dead letter queues
6. **Low latency**: Near real-time notifications (<1s for most cases)

**Key Insight from subscription-engine branch**: Leverage the existing **Transaction Table** as the single source of truth for subscription events!

---

## Legacy Subscription Engine Architecture Analysis

From `https://github.com/microsoft/fhir-server/tree/feature/subscription-engine`:

### Core Components

1. **Watchdog Pattern** (`SubscriptionProcessorWatchdog.cs`):
   - Polls transaction table for new transactions
   - Enqueues subscription jobs to process matched resources
   - Tracks last processed transaction ID
   - Runs every 3 seconds with 20-second lease

2. **Channel Abstraction** (`ISubscriptionChannel`):
   - `PublishAsync` - Send resource notifications
   - `PublishHandShakeAsync` - Initial connection test
   - `PublishHeartBeatAsync` - Keep-alive for long-running subscriptions

3. **Channel Implementations**:
   - `RestHookChannel` - HTTP POST to external endpoint
   - `StorageChannel` - Write to blob storage
   - `DataLakeChannel` - Stream to data lake

4. **Job-Based Processing** (`IQueueClient`):
   - Jobs queued for each transaction
   - Worker processes pick up jobs
   - Parallel processing with backpressure

### Key Workflow

```
1. Transaction committed → TransactionId assigned
2. Watchdog polls: lastProcessed < visibility watermark
3. Jobs created: one per transaction (contains all resources)
4. Workers execute jobs:
   a. Load subscription resources
   b. Match resources against subscription criteria
   c. Publish to channels
5. Update lastProcessed = transactionId
```

---

## V2 Solution: Event-Driven Subscriptions with Transaction Table

### Architecture Overview

Instead of polling, use **event-driven** approach leveraging existing transaction infrastructure:

```csharp
// When transaction commits, publish event
public class TransactionCommittedEvent : IEvent
{
    public required long TransactionId { get; init; }
    public required DateTimeOffset CommittedAt { get; init; }
    public required IReadOnlyList<ResourceReference> Resources { get; init; }
}

public record ResourceReference(
    string ResourceType,
    string ResourceId,
    string VersionId,
    long ResourceSurrogateId);
```

### Core Abstractions

```csharp
// 1. Subscription Matcher
public interface ISubscriptionMatcher
{
    /// <summary>
    /// Get active subscriptions that match this resource
    /// </summary>
    ValueTask<IReadOnlyList<SubscriptionMatch>> GetMatchingSubscriptionsAsync(
        ResourceReference resource,
        CancellationToken ct);
}

public record SubscriptionMatch(
    string SubscriptionId,
    SubscriptionCriteria Criteria,
    SubscriptionChannel Channel,
    SubscriptionContentType ContentType);

public record SubscriptionCriteria(
    string ResourceType,
    string? Criteria, // FHIRPath or search params
    string? CriteriaType); // "query" or "fhirpath"

public record SubscriptionChannel(
    SubscriptionChannelType Type, // rest-hook, websocket, email, etc.
    string Endpoint,
    IReadOnlyDictionary<string, string>? Headers, // For rest-hook
    string? Payload); // For email template

public enum SubscriptionChannelType
{
    RestHook,
    WebSocket,
    Email,
    Storage,
    DataLake,
    Custom
}

public enum SubscriptionContentType
{
    Empty,       // Just notification
    IdOnly,      // Include resource ID
    FullResource // Include full resource
}

// 2. Subscription Channel
public interface ISubscriptionChannel
{
    SubscriptionChannelType ChannelType { get; }

    ValueTask PublishAsync(
        SubscriptionNotification notification,
        CancellationToken ct);

    ValueTask PublishHandshakeAsync(
        string subscriptionId,
        string endpoint,
        CancellationToken ct);

    ValueTask PublishHeartbeatAsync(
        string subscriptionId,
        CancellationToken ct);
}

public record SubscriptionNotification(
    string SubscriptionId,
    long TransactionId,
    DateTimeOffset EventTime,
    string ResourceType,
    string ResourceId,
    string? ResourceVersion,
    ITypedElement? Resource); // null if ContentType = Empty/IdOnly

// 3. Subscription Processor
public interface ISubscriptionProcessor
{
    ValueTask ProcessTransactionAsync(
        long transactionId,
        IReadOnlyList<ResourceReference> resources,
        CancellationToken ct);
}
```

---

## Implementation: Event-Driven Subscription Processing

### Step 1: Event Handler

```csharp
public class SubscriptionEventHandler : IEventHandler<TransactionCommittedEvent>
{
    private readonly ISubscriptionProcessor _processor;

    public async ValueTask HandleAsync(
        TransactionCommittedEvent evt,
        CancellationToken ct)
    {
        // Process subscriptions for this transaction
        await _processor.ProcessTransactionAsync(
            evt.TransactionId,
            evt.Resources,
            ct);
    }
}
```

### Step 2: Subscription Processor Implementation

```csharp
public class SubscriptionProcessor : ISubscriptionProcessor
{
    private readonly ISubscriptionMatcher _matcher;
    private readonly ISubscriptionChannelFactory _channelFactory;
    private readonly IResourceStore _resourceStore;
    private readonly ILogger<SubscriptionProcessor> _logger;

    public async ValueTask ProcessTransactionAsync(
        long transactionId,
        IReadOnlyList<ResourceReference> resources,
        CancellationToken ct)
    {
        // For each resource in transaction
        foreach (var resourceRef in resources)
        {
            // Find matching subscriptions
            var matches = await _matcher.GetMatchingSubscriptionsAsync(resourceRef, ct);

            if (matches.Count == 0)
            {
                continue; // No subscriptions for this resource
            }

            // Load full resource if needed
            ITypedElement? fullResource = null;
            if (matches.Any(m => m.ContentType == SubscriptionContentType.FullResource))
            {
                var resourcePointer = new ResourcePointer(
                    resourceRef.ResourceType,
                    resourceRef.ResourceId,
                    resourceRef.VersionId,
                    resourceRef.ResourceSurrogateId,
                    Storage: null!); // Loaded from pointer

                var rawResource = await _resourceStore.GetAsync(resourcePointer, ct);
                fullResource = rawResource?.Resource;
            }

            // Publish to each matching subscription's channel
            foreach (var match in matches)
            {
                var notification = new SubscriptionNotification(
                    SubscriptionId: match.SubscriptionId,
                    TransactionId: transactionId,
                    EventTime: DateTimeOffset.UtcNow,
                    ResourceType: resourceRef.ResourceType,
                    ResourceId: resourceRef.ResourceId,
                    ResourceVersion: resourceRef.VersionId,
                    Resource: match.ContentType == SubscriptionContentType.FullResource
                        ? fullResource
                        : null);

                var channel = _channelFactory.GetChannel(match.Channel.Type);

                try
                {
                    await channel.PublishAsync(notification, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to publish subscription notification. SubscriptionId={SubscriptionId}, TransactionId={TransactionId}",
                        match.SubscriptionId, transactionId);

                    // TODO: Retry logic, dead letter queue
                }
            }
        }
    }
}
```

### Step 3: Subscription Matcher with Cached Index

```csharp
public class SubscriptionMatcher : ISubscriptionMatcher
{
    // In-memory index: ResourceType -> List<ActiveSubscription>
    private readonly ConcurrentDictionary<string, List<ActiveSubscription>> _subscriptionIndex = new();
    private readonly ISearchService _searchService;

    public async ValueTask<IReadOnlyList<SubscriptionMatch>> GetMatchingSubscriptionsAsync(
        ResourceReference resource,
        CancellationToken ct)
    {
        // Get subscriptions for this resource type
        if (!_subscriptionIndex.TryGetValue(resource.ResourceType, out var subscriptions))
        {
            return Array.Empty<SubscriptionMatch>();
        }

        var matches = new List<SubscriptionMatch>();

        foreach (var subscription in subscriptions)
        {
            // Check if resource matches criteria
            bool isMatch = await EvaluateCriteriaAsync(
                subscription.Criteria,
                resource,
                ct);

            if (isMatch)
            {
                matches.Add(new SubscriptionMatch(
                    subscription.Id,
                    subscription.Criteria,
                    subscription.Channel,
                    subscription.ContentType));
            }
        }

        return matches;
    }

    private async ValueTask<bool> EvaluateCriteriaAsync(
        SubscriptionCriteria criteria,
        ResourceReference resource,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(criteria.Criteria))
        {
            return true; // No criteria = match all
        }

        if (criteria.CriteriaType == "query")
        {
            // Parse FHIR search query: "Observation?code=http://loinc.org|1234-5"
            var searchParams = ParseSearchQuery(criteria.Criteria);

            // Execute search to check if resource matches
            var searchResult = await _searchService.SearchAsync(new SearchRequest
            {
                ResourceType = criteria.ResourceType,
                Parameters = searchParams,
                ResourceIds = new[] { resource.ResourceId } // Only search this resource
            }, ct);

            return searchResult.Results.Any();
        }
        else if (criteria.CriteriaType == "fhirpath")
        {
            // Load resource and evaluate FHIRPath
            var resourceData = await LoadResourceAsync(resource, ct);
            return EvaluateFhirPath(criteria.Criteria, resourceData);
        }

        return false;
    }

    // Reload index when Subscription resource changes
    public async Task ReloadSubscriptionsAsync(CancellationToken ct)
    {
        // Search for active Subscription resources
        var subscriptions = await _searchService.SearchAsync(new SearchRequest
        {
            ResourceType = "Subscription",
            Parameters = new[] { new SearchParameter("status", "active") }
        }, ct);

        var newIndex = new ConcurrentDictionary<string, List<ActiveSubscription>>();

        foreach (var subscription in subscriptions.Results)
        {
            var activeSubscription = ParseSubscription(subscription);

            if (!newIndex.TryGetValue(activeSubscription.Criteria.ResourceType, out var list))
            {
                list = new List<ActiveSubscription>();
                newIndex[activeSubscription.Criteria.ResourceType] = list;
            }

            list.Add(activeSubscription);
        }

        // Atomic swap
        foreach (var kvp in newIndex)
        {
            _subscriptionIndex[kvp.Key] = kvp.Value;
        }
    }
}

internal record ActiveSubscription(
    string Id,
    SubscriptionCriteria Criteria,
    SubscriptionChannel Channel,
    SubscriptionContentType ContentType);
```

---

## Channel Implementations

### RestHook Channel

```csharp
public class RestHookChannel : ISubscriptionChannel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SubscriptionChannelType ChannelType => SubscriptionChannelType.RestHook;

    public async ValueTask PublishAsync(
        SubscriptionNotification notification,
        CancellationToken ct)
    {
        // Build notification bundle
        var bundle = new Bundle
        {
            Type = Bundle.BundleType.History,
            Entry = new List<Bundle.EntryComponent>
            {
                new Bundle.EntryComponent
                {
                    FullUrl = $"urn:uuid:{notification.SubscriptionId}",
                    Resource = new Parameters
                    {
                        Parameter = new List<Parameters.ParameterComponent>
                        {
                            new Parameters.ParameterComponent
                            {
                                Name = "subscription",
                                Value = new FhirUri($"Subscription/{notification.SubscriptionId}")
                            },
                            new Parameters.ParameterComponent
                            {
                                Name = "type",
                                Value = new FhirString("event-notification")
                            },
                            new Parameters.ParameterComponent
                            {
                                Name = "focus",
                                Value = new FhirUri($"{notification.ResourceType}/{notification.ResourceId}")
                            }
                        }
                    }
                }
            }
        };

        // Add full resource if requested
        if (notification.Resource != null)
        {
            bundle.Entry.Add(new Bundle.EntryComponent
            {
                FullUrl = $"{notification.ResourceType}/{notification.ResourceId}",
                Resource = notification.Resource.ToPoco() // Convert ITypedElement to POCO
            });
        }

        // Send HTTP POST
        var httpClient = _httpClientFactory.CreateClient("SubscriptionRestHook");
        var json = JsonSerializer.Serialize(bundle);

        var response = await httpClient.PostAsync(
            endpoint,
            new StringContent(json, Encoding.UTF8, "application/fhir+json"),
            ct);

        response.EnsureSuccessStatusCode();
    }

    public async ValueTask PublishHandshakeAsync(
        string subscriptionId,
        string endpoint,
        CancellationToken ct)
    {
        var handshake = new Parameters
        {
            Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent
                {
                    Name = "subscription",
                    Value = new FhirUri($"Subscription/{subscriptionId}")
                },
                new Parameters.ParameterComponent
                {
                    Name = "type",
                    Value = new FhirString("handshake")
                }
            }
        };

        var httpClient = _httpClientFactory.CreateClient("SubscriptionRestHook");
        var json = JsonSerializer.Serialize(handshake);

        var response = await httpClient.PostAsync(
            endpoint,
            new StringContent(json, Encoding.UTF8, "application/fhir+json"),
            ct);

        response.EnsureSuccessStatusCode();
    }

    public async ValueTask PublishHeartbeatAsync(
        string subscriptionId,
        CancellationToken ct)
    {
        // Similar to handshake but with type="heartbeat"
    }
}
```

### Storage Channel (Blob/File)

```csharp
public class StorageChannel : ISubscriptionChannel
{
    private readonly IBlobStorageClient _blobClient;

    public SubscriptionChannelType ChannelType => SubscriptionChannelType.Storage;

    public async ValueTask PublishAsync(
        SubscriptionNotification notification,
        CancellationToken ct)
    {
        // Write resource to blob storage
        var blobPath = $"subscriptions/{notification.SubscriptionId}/{notification.TransactionId:D19}/{notification.ResourceType}_{notification.ResourceId}.json";

        var json = notification.Resource != null
            ? SerializeResource(notification.Resource)
            : JsonSerializer.Serialize(new
            {
                subscriptionId = notification.SubscriptionId,
                resourceType = notification.ResourceType,
                resourceId = notification.ResourceId,
                resourceVersion = notification.ResourceVersion,
                eventTime = notification.EventTime
            });

        await _blobClient.UploadAsync(blobPath, json, ct);
    }
}
```

---

## Failover Pattern: Watchdog as Backup

Even with event-driven approach, implement watchdog as **failover**:

```csharp
public class SubscriptionWatchdog : IHostedService
{
    private readonly ISubscriptionProcessor _processor;
    private readonly ITransactionStore _transactionStore;
    private Timer? _timer;

    // Tracks last processed transaction
    private long _lastProcessedTransactionId = 0;

    public Task StartAsync(CancellationToken ct)
    {
        // Run every 30 seconds (less frequent than event-driven)
        _timer = new Timer(async _ => await ProcessPendingTransactionsAsync(ct), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }

    private async Task ProcessPendingTransactionsAsync(CancellationToken ct)
    {
        // Get current visibility watermark
        var visibility = await _transactionStore.GetVisibilityWatermarkAsync(ct);

        // Get unprocessed transactions
        var transactions = await _transactionStore.GetTransactionsAsync(
            _lastProcessedTransactionId,
            visibility,
            ct);

        foreach (var transaction in transactions.OrderBy(t => t.TransactionId))
        {
            // Extract resources from transaction
            var resources = await _transactionStore.GetResourcesInTransactionAsync(
                transaction.TransactionId,
                ct);

            // Process subscriptions (same logic as event handler)
            await _processor.ProcessTransactionAsync(
                transaction.TransactionId,
                resources,
                ct);

            _lastProcessedTransactionId = transaction.TransactionId;
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

**Why Both Event + Watchdog?**
- **Event-driven**: Fast, near real-time (<100ms)
- **Watchdog**: Safety net for missed events (server restart, event bus failure)
- **Result**: Never miss a subscription notification!

---

## Subscription Lifecycle

### 1. Create Subscription

```csharp
POST /Subscription
{
  "resourceType": "Subscription",
  "status": "requested",
  "reason": "Monitor patient vital signs",
  "criteria": {
    "resourceType": "Observation",
    "searchCriteria": "code=http://loinc.org|8867-4&_lastUpdated=gt2024-01-01"
  },
  "channel": {
    "type": "rest-hook",
    "endpoint": "https://example.com/fhir/subscription",
    "payload": "application/fhir+json",
    "header": [
      "Authorization: Bearer secret-token"
    ]
  },
  "contentType": "full-resource"
}
```

### 2. Activate Subscription (Handshake)

```csharp
public class SubscriptionActivationHandler : ICommandHandler<ActivateSubscriptionCommand>
{
    private readonly ISubscriptionChannel _channel;
    private readonly IFhirRepository _repository;

    public async ValueTask HandleAsync(
        ActivateSubscriptionCommand command,
        CancellationToken ct)
    {
        // Load subscription
        var subscription = await _repository.GetAsync(
            new ResourceKey("Subscription", command.SubscriptionId),
            ct);

        // Send handshake
        await _channel.PublishHandshakeAsync(
            command.SubscriptionId,
            subscription.Channel.Endpoint,
            ct);

        // Update status to "active"
        subscription.Status = "active";
        await _repository.UpdateAsync(subscription, ct);

        // Reload subscription index
        await _matcher.ReloadSubscriptionsAsync(ct);
    }
}
```

### 3. Deactivate Subscription

```csharp
// When Subscription status changed to "off" or deleted
public class SubscriptionDeactivationHandler : IEventHandler<ResourceUpdatedEvent>
{
    public async ValueTask HandleAsync(ResourceUpdatedEvent evt, CancellationToken ct)
    {
        if (evt.ResourceType == "Subscription")
        {
            // Reload subscription index (remove this subscription)
            await _matcher.ReloadSubscriptionsAsync(ct);
        }
    }
}
```

---

## Performance Optimizations

### 1. Subscription Index Caching

```csharp
// In-memory index grouped by resource type
// Updated when Subscription resources change
ConcurrentDictionary<string, List<ActiveSubscription>> _subscriptionIndex;

// O(1) lookup by resource type
// O(n) evaluation where n = subscriptions for that type (typically small)
```

### 2. Criteria Pre-Compilation

```csharp
public record CompiledSubscriptionCriteria
{
    public string ResourceType { get; init; }
    public Expression<Func<ITypedElement, bool>>? FhirPathExpression { get; init; } // Pre-compiled
    public SearchRequest? SearchRequest { get; init; } // Pre-parsed
}

// Compile once when subscription activated, reuse for every match
```

### 3. Batch Processing

```csharp
// Process multiple resources from same transaction in batch
public async ValueTask ProcessTransactionAsync(
    long transactionId,
    IReadOnlyList<ResourceReference> resources,
    CancellationToken ct)
{
    // Group by subscription to batch channel publishes
    var notificationsBySubscription = new Dictionary<string, List<SubscriptionNotification>>();

    foreach (var resource in resources)
    {
        var matches = await _matcher.GetMatchingSubscriptionsAsync(resource, ct);

        foreach (var match in matches)
        {
            if (!notificationsBySubscription.TryGetValue(match.SubscriptionId, out var notifications))
            {
                notifications = new List<SubscriptionNotification>();
                notificationsBySubscription[match.SubscriptionId] = notifications;
            }

            notifications.Add(BuildNotification(match, resource));
        }
    }

    // Publish batches to channels
    foreach (var (subscriptionId, notifications) in notificationsBySubscription)
    {
        await PublishBatchAsync(subscriptionId, notifications, ct);
    }
}
```

---

## Distributed Processing

For high-volume subscriptions, distribute across multiple workers:

```csharp
// Use existing job queue infrastructure
public class SubscriptionEventHandler : IEventHandler<TransactionCommittedEvent>
{
    private readonly IQueueClient _queueClient;

    public async ValueTask HandleAsync(TransactionCommittedEvent evt, CancellationToken ct)
    {
        // Enqueue job for background processing
        await _queueClient.EnqueueAsync(
            QueueType.Subscriptions,
            new SubscriptionJobDefinition
            {
                TransactionId = evt.TransactionId,
                Resources = evt.Resources
            },
            ct);
    }
}

// Worker picks up job
public class SubscriptionJobWorker : IJobWorker<SubscriptionJobDefinition>
{
    private readonly ISubscriptionProcessor _processor;

    public async ValueTask ExecuteAsync(
        SubscriptionJobDefinition job,
        CancellationToken ct)
    {
        await _processor.ProcessTransactionAsync(
            job.TransactionId,
            job.Resources,
            ct);
    }
}
```

---

## Testing Strategy

### Unit Tests

```csharp
[Fact]
public async Task SubscriptionMatcher_MatchesCriteria()
{
    // Arrange
    var subscription = new ActiveSubscription(
        Id: "sub-1",
        Criteria: new SubscriptionCriteria(
            ResourceType: "Observation",
            Criteria: "code=http://loinc.org|8867-4",
            CriteriaType: "query"),
        Channel: ...,
        ContentType: ...);

    var matcher = new SubscriptionMatcher(...);
    await matcher.AddSubscriptionAsync(subscription);

    var resource = new ResourceReference(
        ResourceType: "Observation",
        ResourceId: "obs-123",
        VersionId: "1",
        ResourceSurrogateId: 456);

    // Act
    var matches = await matcher.GetMatchingSubscriptionsAsync(resource, CancellationToken.None);

    // Assert
    Assert.Single(matches);
    Assert.Equal("sub-1", matches[0].SubscriptionId);
}
```

### Integration Tests

```csharp
[Fact]
public async Task Subscription_NotifiesOnResourceCreate()
{
    // 1. Create subscription
    var subscription = await CreateSubscription(
        criteria: "Observation?code=http://loinc.org|8867-4",
        endpoint: _mockServer.Url);

    // 2. Activate subscription (handshake)
    await ActivateSubscription(subscription.Id);

    // 3. Create matching resource
    var observation = await CreateObservation(code: "8867-4");

    // 4. Wait for notification
    await _mockServer.WaitForRequest(timeout: TimeSpan.FromSeconds(5));

    // 5. Verify notification received
    var notification = _mockServer.GetLastRequest();
    Assert.Equal("POST", notification.Method);
    var bundle = ParseBundle(notification.Body);
    Assert.Contains("Observation/obs-123", bundle.Entry[1].FullUrl);
}
```

---

## Implementation Phases

### Phase 1.2 (Search Implementation)
**Status**: Foundation only

- Basic Subscription CRUD (no processing)
- Store Subscription resources like any other resource

### Phase 7 (Distributed Infrastructure)
**Status**: Add subscription processing

Deliverables:
- TransactionCommittedEvent published when transactions commit
- SubscriptionEventHandler processes events
- SubscriptionMatcher with in-memory index
- RestHookChannel implementation
- SubscriptionWatchdog as failover

### Phase 12 (Bulk Operations)
**Status**: Add storage channels

Deliverables:
- StorageChannel (blob/file)
- DataLakeChannel
- Batch processing optimizations

---

## Benefits

### ✅ Compared to Legacy

| Aspect | Legacy (Polling) | V2 (Event-Driven) |
|--------|------------------|-------------------|
| **Latency** | 3-6 seconds (poll interval) | <100ms (event-driven) |
| **Resource usage** | Constant polling overhead | Only processes when events occur |
| **Scalability** | Single watchdog instance | Distributed event handlers |
| **Reliability** | Watchdog only (single point) | Event + Watchdog (dual safety) |
| **Transaction ordering** | Guaranteed (sequential) | Guaranteed (transactionId ordering) |

### ✅ Key Improvements

1. **Near Real-Time**: Event-driven delivers <100ms latency vs 3-6s polling
2. **Never Miss Events**: Dual approach (event + watchdog failover)
3. **Horizontal Scaling**: Distribute across worker nodes
4. **Lower Resource Usage**: Only process when events occur (not constant polling)
5. **Reuses Infrastructure**: Leverages existing transaction table + event bus

---

## Consequences

### Positive

1. **Low Latency**: <100ms notification delivery
2. **Guaranteed Delivery**: Dual approach ensures no missed events
3. **Scalable**: Distributed processing across workers
4. **Reuses Patterns**: Transaction table, event bus, job queue all already exist
5. **Channel Extensibility**: Easy to add new channel types (WebSocket, SMS, etc.)

### Negative

1. **Complexity**: Dual processing (event + watchdog) adds code
2. **Resource Loading**: Full-resource subscriptions require extra reads
3. **Criteria Evaluation**: Complex queries can be expensive

### Mitigation

1. **Simplify Gradually**: Start with event-only, add watchdog if needed
2. **Cache Resources**: If multiple subscriptions need same resource, load once
3. **Pre-Compile Criteria**: Parse/compile criteria when subscription activated

---

## References

- Legacy: `https://github.com/microsoft/fhir-server/tree/feature/subscription-engine`
- Investigation: `transaction-table-core-abstraction.md` (transaction visibility)
- Investigation: `distributed-messaging-architecture.md` (event bus)
- Investigation: `bulk-import-export-with-channels.md` (channel pattern)
- FHIR Spec: http://hl7.org/fhir/subscription.html
- FHIR Subscriptions Backport IG: http://hl7.org/fhir/uv/subscriptions-backport/

---

## Next Steps

1. **Phase 7**: Implement basic subscription processing with event-driven approach
2. **Phase 12**: Add storage channels for bulk subscription delivery
3. **Future**: Add WebSocket channel for real-time browser notifications
