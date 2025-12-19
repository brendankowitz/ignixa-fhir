# Investigation: Distributed Messaging Architecture

**Feature**: architecture
**Status**: Viable
**Created**: 2024-12-23

This document outlines the bus abstraction design that supports both in-process Medino messaging and distributed Redis transport for web farm scenarios, while maintaining the same CQRS interfaces.

## Core Messaging Abstractions

### Bus Interface Abstraction

```csharp
public interface IMessageBus
{
    /// <summary>
    /// Send a command and wait for response
    /// </summary>
    ValueTask<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a command without expecting response
    /// </summary>
    ValueTask SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a query and wait for response
    /// </summary>
    ValueTask<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish an event (fire and forget)
    /// </summary>
    ValueTask PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// Subscribe to events (for distributed scenarios)
    /// </summary>
    ValueTask SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default) where TEvent : IEvent;
}

/// <summary>
/// Tenant-aware message bus that automatically includes tenant context
/// </summary>
public interface ITenantMessageBus
{
    /// <summary>
    /// Get bus scoped to specific tenant
    /// </summary>
    IMessageBus GetTenantBus(string tenantId);

    /// <summary>
    /// Send message with explicit tenant context
    /// </summary>
    ValueTask<TResponse> SendAsync<TResponse>(string tenantId, ICommand<TResponse> command, CancellationToken cancellationToken = default);
}
```

### Message Base Types

```csharp
/// <summary>
/// Base interface for all messages
/// </summary>
public interface IMessage
{
    string MessageId { get; }
    string TenantId { get; }
    DateTimeOffset Timestamp { get; }
    string? CorrelationId { get; }
}

/// <summary>
/// Command that returns a response
/// </summary>
public interface ICommand<TResponse> : IMessage;

/// <summary>
/// Command without response
/// </summary>
public interface ICommand : IMessage;

/// <summary>
/// Query that returns data
/// </summary>
public interface IQuery<TResponse> : IMessage;

/// <summary>
/// Event notification
/// </summary>
public interface IEvent : IMessage;

/// <summary>
/// Base message implementation with memory efficiency
/// </summary>
public abstract record MessageBase : IMessage
{
    public string MessageId { get; init; } = Ulid.NewUlid().ToString();
    public required string TenantId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}
```

### FHIR-Specific Message Types

```csharp
/// <summary>
/// FHIR Resource Commands
/// </summary>
public record CreateResourceCommand(
    string TenantId,
    string ResourceType,
    ReadOnlyMemory<byte> ResourceJson,
    string? VersionId = null) : MessageBase, ICommand<ResourceCreatedResponse>
{
    public required string TenantId { get; init; } = TenantId;
}

public record UpdateResourceCommand(
    string TenantId,
    string ResourceType,
    string ResourceId,
    ReadOnlyMemory<byte> ResourceJson,
    string? IfMatch = null) : MessageBase, ICommand<ResourceUpdatedResponse>
{
    public required string TenantId { get; init; } = TenantId;
}

public record DeleteResourceCommand(
    string TenantId,
    string ResourceType,
    string ResourceId) : MessageBase, ICommand<ResourceDeletedResponse>
{
    public required string TenantId { get; init; } = TenantId;
}

/// <summary>
/// FHIR Search Queries
/// </summary>
public record SearchResourcesQuery(
    string TenantId,
    string ResourceType,
    IReadOnlyDictionary<string, StringValues> Parameters,
    int? Count = null,
    string? ContinuationToken = null) : MessageBase, IQuery<SearchResult>
{
    public required string TenantId { get; init; } = TenantId;
}

public record GetResourceQuery(
    string TenantId,
    string ResourceType,
    string ResourceId,
    string? VersionId = null) : MessageBase, IQuery<ResourceWrapper?>
{
    public required string TenantId { get; init; } = TenantId;
}

/// <summary>
/// FHIR Events
/// </summary>
public record ResourceCreatedEvent(
    string TenantId,
    string ResourceType,
    string ResourceId,
    string VersionId) : MessageBase, IEvent
{
    public required string TenantId { get; init; } = TenantId;
}

public record ResourceUpdatedEvent(
    string TenantId,
    string ResourceType,
    string ResourceId,
    string VersionId,
    string? PreviousVersionId) : MessageBase, IEvent
{
    public required string TenantId { get; init; } = TenantId;
}

public record ResourceDeletedEvent(
    string TenantId,
    string ResourceType,
    string ResourceId) : MessageBase, IEvent
{
    public required string TenantId { get; init; } = TenantId;
}

/// <summary>
/// Response Types
/// </summary>
public record ResourceCreatedResponse(
    string ResourceId,
    string VersionId,
    DateTimeOffset CreatedAt);

public record ResourceUpdatedResponse(
    string ResourceId,
    string VersionId,
    DateTimeOffset UpdatedAt);

public record ResourceDeletedResponse(
    string ResourceId,
    DateTimeOffset DeletedAt);
```

## In-Process Medino Implementation

### Medino Bus Adapter

```csharp
public class MedinoMessageBus : IMessageBus
{
    private readonly IMediator _mediator;
    private readonly ILogger<MedinoMessageBus> _logger;

    public MedinoMessageBus(IMediator mediator, ILogger<MedinoMessageBus> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async ValueTask<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending command {CommandType} for tenant {TenantId}",
            command.GetType().Name, command.TenantId);

        // Medino handles the command directly
        return await _mediator.SendAsync(command, cancellationToken);
    }

    public async ValueTask SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending command {CommandType} for tenant {TenantId}",
            command.GetType().Name, command.TenantId);

        await _mediator.SendAsync(command, cancellationToken);
    }

    public async ValueTask<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing query {QueryType} for tenant {TenantId}",
            query.GetType().Name, query.TenantId);

        return await _mediator.SendAsync(query, cancellationToken);
    }

    public ValueTask PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        _logger.LogDebug("Publishing event {EventType} for tenant {TenantId}",
            eventMessage.GetType().Name, eventMessage.TenantId);

        // For in-process, events are handled immediately
        return _mediator.PublishAsync(eventMessage, cancellationToken);
    }

    public ValueTask SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        // In-process subscriptions are handled by Medino's registration system
        // This is typically done at startup via dependency injection
        _logger.LogInformation("Event subscription registered for {EventType}", typeof(TEvent).Name);
        return ValueTask.CompletedTask;
    }
}
```

### Medino Handler Implementations

```csharp
/// <summary>
/// Example FHIR resource command handlers using Medino
/// </summary>
public class CreateResourceHandler : ICommandHandler<CreateResourceCommand, ResourceCreatedResponse>
{
    private readonly IFhirRepository _repository;
    private readonly IFhirCacheService _cache;
    private readonly IMessageBus _bus;
    private readonly ILogger<CreateResourceHandler> _logger;

    public CreateResourceHandler(
        IFhirRepository repository,
        IFhirCacheService cache,
        IMessageBus bus,
        ILogger<CreateResourceHandler> logger)
    {
        _repository = repository;
        _cache = cache;
        _bus = bus;
        _logger = logger;
    }

    public async ValueTask<ResourceCreatedResponse> HandleAsync(CreateResourceCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {ResourceType} resource for tenant {TenantId}",
            command.ResourceType, command.TenantId);

        // Parse and create resource
        var resource = await ParseResourceAsync(command.ResourceJson, command.ResourceType, cancellationToken);
        var createdResource = await _repository.CreateAsync(resource, cancellationToken);

        // Update cache
        await _cache.SetResourceAsync(createdResource, cancellationToken: cancellationToken);

        // Publish event
        var resourceCreatedEvent = new ResourceCreatedEvent(
            command.TenantId,
            createdResource.ResourceType,
            createdResource.ResourceId,
            createdResource.VersionId);

        await _bus.PublishAsync(resourceCreatedEvent, cancellationToken);

        return new ResourceCreatedResponse(
            createdResource.ResourceId,
            createdResource.VersionId,
            createdResource.LastModified);
    }

    private async ValueTask<ResourceWrapper> ParseResourceAsync(ReadOnlyMemory<byte> json, string resourceType, CancellationToken cancellationToken)
    {
        // Use memory-efficient parsing
        using var stream = FhirStreamManager.GetStream(json.Span, "ParseResource");
        var sourceNode = JsonSourceNodeFactory.Parse(json.Span);

        // Create resource wrapper
        return new ResourceWrapper(
            resourceType,
            Guid.NewGuid().ToString(),
            "1",
            DateTimeOffset.UtcNow,
            sourceNode);
    }
}

public class SearchResourcesHandler : IQueryHandler<SearchResourcesQuery, SearchResult>
{
    private readonly ISearchService _searchService;
    private readonly IFhirCacheService _cache;
    private readonly ILogger<SearchResourcesHandler> _logger;

    public SearchResourcesHandler(
        ISearchService searchService,
        IFhirCacheService cache,
        ILogger<SearchResourcesHandler> logger)
    {
        _searchService = searchService;
        _cache = cache;
        _logger = logger;
    }

    public async ValueTask<SearchResult> HandleAsync(SearchResourcesQuery query, CancellationToken cancellationToken)
    {
        // Build cache key from query parameters
        var queryString = BuildQueryString(query.Parameters);
        var cachedResult = await _cache.GetSearchResultAsync(query.TenantId, queryString, cancellationToken);

        if (cachedResult != null)
        {
            _logger.LogDebug("Returning cached search result for tenant {TenantId}", query.TenantId);
            return cachedResult;
        }

        // Execute search
        var result = await _searchService.SearchAsync(
            query.ResourceType,
            query.Parameters,
            query.Count,
            query.ContinuationToken,
            cancellationToken);

        // Cache result
        await _cache.SetSearchResultAsync(query.TenantId, queryString, result, cancellationToken: cancellationToken);

        return result;
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, StringValues> parameters)
    {
        return string.Join("&", parameters.Select(kvp => $"{kvp.Key}={string.Join(",", kvp.Value)}"));
    }
}
```

## Distributed Redis Implementation

### Redis Message Bus

```csharp
public class RedisMessageBus : IMessageBus, IDisposable
{
    private readonly IDatabase _database;
    private readonly ISubscriber _subscriber;
    private readonly ISerializer _serializer;
    private readonly ILogger<RedisMessageBus> _logger;
    private readonly RedisMessageBusOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ReadOnlyMemory<byte>>> _pendingRequests = new();

    public RedisMessageBus(
        IConnectionMultiplexer redis,
        ISerializer serializer,
        IOptions<RedisMessageBusOptions> options,
        ILogger<RedisMessageBus> logger)
    {
        _database = redis.GetDatabase();
        _subscriber = redis.GetSubscriber();
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;

        // Subscribe to response channel
        _ = Task.Run(async () => await SubscribeToResponsesAsync());
    }

    public async ValueTask<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        var messageId = command.MessageId;
        var requestChannel = GetCommandChannel(command.GetType());
        var responseChannel = GetResponseChannel(messageId);

        // Serialize command
        using var stream = FhirStreamManager.GetStream("SendCommand");
        await _serializer.SerializeAsync(stream, command, cancellationToken);
        var serializedCommand = stream.ToArray();

        // Create response awaiter
        var responseTask = new TaskCompletionSource<ReadOnlyMemory<byte>>();
        _pendingRequests[messageId] = responseTask;

        try
        {
            // Send command to processing queue
            await _database.ListLeftPushAsync(requestChannel, serializedCommand);

            _logger.LogDebug("Sent command {CommandType} with ID {MessageId} to channel {Channel}",
                command.GetType().Name, messageId, requestChannel);

            // Wait for response with timeout
            using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var responseBytes = await responseTask.Task.WaitAsync(combinedCts.Token);

            // Deserialize response
            using var responseStream = FhirStreamManager.GetStream(responseBytes.Span, "ReceiveResponse");
            return await _serializer.DeserializeAsync<TResponse>(responseStream, cancellationToken) ?? throw new InvalidOperationException("Null response received");
        }
        finally
        {
            _pendingRequests.TryRemove(messageId, out _);
        }
    }

    public async ValueTask SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        var requestChannel = GetCommandChannel(command.GetType());

        // Serialize command
        using var stream = FhirStreamManager.GetStream("SendCommand");
        await _serializer.SerializeAsync(stream, command, cancellationToken);
        var serializedCommand = stream.ToArray();

        // Send to queue (fire and forget)
        await _database.ListLeftPushAsync(requestChannel, serializedCommand);

        _logger.LogDebug("Sent fire-and-forget command {CommandType} to channel {Channel}",
            command.GetType().Name, requestChannel);
    }

    public async ValueTask<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        // Queries are handled similarly to commands with responses
        return await SendAsync(query, cancellationToken);
    }

    public async ValueTask PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        var eventChannel = GetEventChannel(eventMessage.GetType());

        // Serialize event
        using var stream = FhirStreamManager.GetStream("PublishEvent");
        await _serializer.SerializeAsync(stream, eventMessage, cancellationToken);
        var serializedEvent = stream.ToArray();

        // Publish to subscribers
        await _subscriber.PublishAsync(eventChannel, serializedEvent);

        _logger.LogDebug("Published event {EventType} to channel {Channel}",
            eventMessage.GetType().Name, eventChannel);
    }

    public async ValueTask SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        var eventChannel = GetEventChannel(typeof(TEvent));

        await _subscriber.SubscribeAsync(eventChannel, async (channel, message) =>
        {
            try
            {
                // Deserialize event
                using var stream = FhirStreamManager.GetStream(message, "SubscribeEvent");
                var eventMessage = await _serializer.DeserializeAsync<TEvent>(stream, cancellationToken);

                if (eventMessage != null)
                {
                    await handler(eventMessage, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event {EventType} from channel {Channel}",
                    typeof(TEvent).Name, channel);
            }
        });

        _logger.LogInformation("Subscribed to event {EventType} on channel {Channel}",
            typeof(TEvent).Name, eventChannel);
    }

    private async Task SubscribeToResponsesAsync()
    {
        var responsePattern = $"{_options.KeyPrefix}:response:*";

        await _subscriber.SubscribeAsync(new RedisChannel(responsePattern, RedisChannel.PatternMode.Pattern),
            (channel, message) =>
            {
                try
                {
                    // Extract message ID from channel name
                    var channelStr = channel.ToString();
                    var messageId = channelStr.Substring(channelStr.LastIndexOf(':') + 1);

                    if (_pendingRequests.TryRemove(messageId, out var tcs))
                    {
                        tcs.SetResult(message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing response from channel {Channel}", channel);
                }
            });
    }

    private string GetCommandChannel(Type commandType) => $"{_options.KeyPrefix}:cmd:{commandType.Name}";
    private string GetQueryChannel(Type queryType) => $"{_options.KeyPrefix}:qry:{queryType.Name}";
    private string GetEventChannel(Type eventType) => $"{_options.KeyPrefix}:evt:{eventType.Name}";
    private string GetResponseChannel(string messageId) => $"{_options.KeyPrefix}:response:{messageId}";

    public void Dispose()
    {
        // Cancel all pending requests
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }
}

public class RedisMessageBusOptions
{
    public string KeyPrefix { get; set; } = "fhir:bus";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
```

### Redis Message Processor

```csharp
/// <summary>
/// Background service that processes messages from Redis queues
/// </summary>
public class RedisMessageProcessor : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMediator _mediator;
    private readonly ISerializer _serializer;
    private readonly ILogger<RedisMessageProcessor> _logger;
    private readonly RedisMessageProcessorOptions _options;

    public RedisMessageProcessor(
        IConnectionMultiplexer redis,
        IMediator mediator,
        ISerializer serializer,
        IOptions<RedisMessageProcessorOptions> options,
        ILogger<RedisMessageProcessor> logger)
    {
        _redis = redis;
        _mediator = mediator;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var database = _redis.GetDatabase();
        var subscriber = _redis.GetSubscriber();

        // Start multiple processors for different message types
        var tasks = new List<Task>
        {
            ProcessCommandsAsync(database, stoppingToken),
            ProcessQueriesAsync(database, stoppingToken)
        };

        await Task.WhenAll(tasks);
    }

    private async Task ProcessCommandsAsync(IDatabase database, CancellationToken cancellationToken)
    {
        var commandQueues = _options.CommandTypes.Select(type => $"{_options.KeyPrefix}:cmd:{type.Name}").ToArray();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Block pop from any command queue
                var result = await database.ListRightPopLeftPushAsync(
                    commandQueues,
                    $"{_options.KeyPrefix}:processing",
                    TimeSpan.FromSeconds(1));

                if (result.HasValue)
                {
                    await ProcessMessageAsync(result.Value, database, cancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Error processing command queue");
                await Task.Delay(_options.ErrorRetryDelay, cancellationToken);
            }
        }
    }

    private async Task ProcessQueriesAsync(IDatabase database, CancellationToken cancellationToken)
    {
        var queryQueues = _options.QueryTypes.Select(type => $"{_options.KeyPrefix}:qry:{type.Name}").ToArray();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await database.ListRightPopLeftPushAsync(
                    queryQueues,
                    $"{_options.KeyPrefix}:processing",
                    TimeSpan.FromSeconds(1));

                if (result.HasValue)
                {
                    await ProcessMessageAsync(result.Value, database, cancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Error processing query queue");
                await Task.Delay(_options.ErrorRetryDelay, cancellationToken);
            }
        }
    }

    private async Task ProcessMessageAsync(RedisValue messageBytes, IDatabase database, CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize message envelope
            using var stream = FhirStreamManager.GetStream(messageBytes, "ProcessMessage");
            var messageEnvelope = await _serializer.DeserializeAsync<MessageEnvelope>(stream, cancellationToken);

            if (messageEnvelope == null) return;

            // Process with Medino
            var response = await _mediator.SendAsync(messageEnvelope.Message, cancellationToken);

            // Send response if needed
            if (response != null && !string.IsNullOrEmpty(messageEnvelope.ResponseChannel))
            {
                using var responseStream = FhirStreamManager.GetStream("SendResponse");
                await _serializer.SerializeAsync(responseStream, response, cancellationToken);
                var responseBytes = responseStream.ToArray();

                var subscriber = _redis.GetSubscriber();
                await subscriber.PublishAsync(messageEnvelope.ResponseChannel, responseBytes);
            }

            // Remove from processing queue
            await database.ListRemoveAsync($"{_options.KeyPrefix}:processing", messageBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            // Message remains in processing queue for retry or manual intervention
        }
    }
}

public record MessageEnvelope(
    object Message,
    string MessageType,
    string? ResponseChannel = null);

public class RedisMessageProcessorOptions
{
    public string KeyPrefix { get; set; } = "fhir:bus";
    public Type[] CommandTypes { get; set; } = Array.Empty<Type>();
    public Type[] QueryTypes { get; set; } = Array.Empty<Type>();
    public TimeSpan ErrorRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
```

## Tenant-Aware Message Bus

```csharp
public class TenantMessageBus : ITenantMessageBus
{
    private readonly ConcurrentDictionary<string, IMessageBus> _tenantBuses = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantMessageBus> _logger;

    public TenantMessageBus(IServiceProvider serviceProvider, ILogger<TenantMessageBus> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IMessageBus GetTenantBus(string tenantId)
    {
        return _tenantBuses.GetOrAdd(tenantId, CreateTenantBus);
    }

    public async ValueTask<TResponse> SendAsync<TResponse>(string tenantId, ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        var tenantBus = GetTenantBus(tenantId);
        return await tenantBus.SendAsync(command, cancellationToken);
    }

    private IMessageBus CreateTenantBus(string tenantId)
    {
        _logger.LogInformation("Creating message bus for tenant {TenantId}", tenantId);

        // Create tenant-scoped message bus
        return new TenantScopedMessageBus(
            _serviceProvider.GetRequiredService<IMessageBus>(),
            tenantId);
    }
}

internal class TenantScopedMessageBus : IMessageBus
{
    private readonly IMessageBus _innerBus;
    private readonly string _tenantId;

    public TenantScopedMessageBus(IMessageBus innerBus, string tenantId)
    {
        _innerBus = innerBus;
        _tenantId = tenantId;
    }

    public ValueTask<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(command);
        return _innerBus.SendAsync(command, cancellationToken);
    }

    public ValueTask SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(command);
        return _innerBus.SendAsync(command, cancellationToken);
    }

    public ValueTask<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(query);
        return _innerBus.QueryAsync(query, cancellationToken);
    }

    public ValueTask PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        EnsureTenantContext(eventMessage);
        return _innerBus.PublishAsync(eventMessage, cancellationToken);
    }

    public ValueTask SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        return _innerBus.SubscribeAsync(handler, cancellationToken);
    }

    private void EnsureTenantContext(IMessage message)
    {
        if (message.TenantId != _tenantId)
        {
            throw new InvalidOperationException($"Message tenant ID '{message.TenantId}' does not match bus tenant ID '{_tenantId}'");
        }
    }
}
```

## Dependency Injection Configuration

```csharp
public static class MessageBusServiceCollectionExtensions
{
    public static IServiceCollection AddFhirMessageBus(this IServiceCollection services, IConfiguration configuration)
    {
        var messagingSection = configuration.GetSection("Messaging");
        var messagingType = messagingSection.GetValue<string>("Type", "Medino");

        return messagingType.ToLowerInvariant() switch
        {
            "redis" => services.AddRedisMessageBus(messagingSection),
            "medino" => services.AddMedinoMessageBus(messagingSection),
            _ => throw new InvalidOperationException($"Unsupported messaging type: {messagingType}")
        };
    }

    private static IServiceCollection AddMedinoMessageBus(this IServiceCollection services, IConfigurationSection config)
    {
        services.AddMedino(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CreateResourceHandler).Assembly);
        });

        services.AddSingleton<IMessageBus, MedinoMessageBus>();
        services.AddSingleton<ITenantMessageBus, TenantMessageBus>();

        return services;
    }

    private static IServiceCollection AddRedisMessageBus(this IServiceCollection services, IConfigurationSection config)
    {
        services.Configure<RedisMessageBusOptions>(config.GetSection("Redis"));
        services.Configure<RedisMessageProcessorOptions>(config.GetSection("Processor"));

        var connectionString = config.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis connection string is required");
        services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(connectionString));

        services.AddSingleton<ISerializer, SystemTextJsonSerializer>();
        services.AddSingleton<IMessageBus, RedisMessageBus>();
        services.AddSingleton<ITenantMessageBus, TenantMessageBus>();

        services.AddHostedService<RedisMessageProcessor>();

        return services;
    }
}
```

This messaging architecture provides:

1. **Unified Interface**: Same CQRS API for in-process and distributed scenarios
2. **Seamless Migration**: Switch between Medino and Redis without code changes
3. **Tenant Isolation**: Automatic tenant context management
4. **Memory Efficiency**: RecyclableMemoryStream and span-based serialization
5. **Reliability**: Redis queues with processing acknowledgment
6. **Scalability**: Horizontal scaling across web farm nodes
7. **Performance**: Async/await throughout with minimal allocations

The design allows the FHIR server to start with in-process Medino messaging for simplicity and scale to distributed Redis messaging when needed for web farm scenarios.