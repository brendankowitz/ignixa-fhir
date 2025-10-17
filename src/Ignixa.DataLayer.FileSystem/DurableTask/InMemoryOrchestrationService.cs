// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.FileSystem.DurableTask;

/// <summary>
/// Simple in-memory implementation of IOrchestrationService for development/prototype use.
/// Stores all orchestration state in memory using concurrent collections.
/// </summary>
public class InMemoryOrchestrationService : IOrchestrationService, IOrchestrationServiceClient
{
    private readonly ILogger<InMemoryOrchestrationService> _logger;
    private readonly ConcurrentDictionary<string, OrchestrationState> _instances = new();
    private readonly ConcurrentQueue<TaskMessage> _orchestrationQueue = new();
    private readonly ConcurrentQueue<TaskMessage> _activityQueue = new();
    private readonly ConcurrentDictionary<string, List<HistoryEvent>> _history = new();
    private bool _isStarted;

    public InMemoryOrchestrationService(ILogger<InMemoryOrchestrationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region IOrchestrationService Implementation

    public int TaskOrchestrationDispatcherCount => 1;

    public int MaxConcurrentTaskOrchestrationWorkItems => 10;

    public int TaskActivityDispatcherCount => 1;

    public int MaxConcurrentTaskActivityWorkItems => 10;

    public BehaviorOnContinueAsNew EventBehaviourForContinueAsNew => BehaviorOnContinueAsNew.Carryover;

    public Task CreateAsync() => Task.CompletedTask;

    public Task CreateAsync(bool recreateInstanceStore) => Task.CompletedTask;

    public Task CreateIfNotExistsAsync() => Task.CompletedTask;

    public Task DeleteAsync() => Task.CompletedTask;

    public Task DeleteAsync(bool deleteInstanceStore) => Task.CompletedTask;

    public Task StartAsync()
    {
        _isStarted = true;
        _logger.LogInformation("InMemoryOrchestrationService started");
        return Task.CompletedTask;
    }

    public Task StopAsync() => StopAsync(false);

    public Task StopAsync(bool isForced)
    {
        _isStarted = false;
        _logger.LogInformation("InMemoryOrchestrationService stopped");
        return Task.CompletedTask;
    }

    public async Task<TaskOrchestrationWorkItem?> LockNextTaskOrchestrationWorkItemAsync(
        TimeSpan receiveTimeout,
        CancellationToken cancellationToken)
    {
        if (!_isStarted)
        {
            return null;
        }

        // Simple polling with timeout
        var endTime = DateTime.UtcNow + receiveTimeout;
        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            if (_orchestrationQueue.TryDequeue(out var message))
            {
                var instanceId = message.OrchestrationInstance.InstanceId;
                return new TaskOrchestrationWorkItem
                {
                    InstanceId = instanceId,
                    NewMessages = new List<TaskMessage> { message },
                    OrchestrationRuntimeState = new OrchestrationRuntimeState(),
                };
            }

            await Task.Delay(100, cancellationToken);
        }

        return null;
    }

    public Task CompleteTaskOrchestrationWorkItemAsync(
        TaskOrchestrationWorkItem workItem,
        OrchestrationRuntimeState newOrchestrationRuntimeState,
        IList<TaskMessage> outboundMessages,
        IList<TaskMessage> orchestratorMessages,
        IList<TaskMessage> timerMessages,
        TaskMessage continuedAsNewMessage,
        OrchestrationState orchestrationState)
    {
        // Update state
        if (orchestrationState != null)
        {
            _instances[workItem.InstanceId] = orchestrationState;
        }

        // Store history
        if (newOrchestrationRuntimeState?.Events != null)
        {
            _history[workItem.InstanceId] = newOrchestrationRuntimeState.Events.ToList();
        }

        // Queue outbound messages
        foreach (var msg in outboundMessages ?? Array.Empty<TaskMessage>())
        {
            _activityQueue.Enqueue(msg);
        }

        foreach (var msg in orchestratorMessages ?? Array.Empty<TaskMessage>())
        {
            _orchestrationQueue.Enqueue(msg);
        }

        return Task.CompletedTask;
    }

    public Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        // Re-queue the messages
        if (workItem.NewMessages != null)
        {
            foreach (var msg in workItem.NewMessages)
            {
                _orchestrationQueue.Enqueue(msg);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReleaseTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem) =>
        Task.CompletedTask;

    public Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem) =>
        Task.CompletedTask;

    public async Task<TaskActivityWorkItem?> LockNextTaskActivityWorkItem(
        TimeSpan receiveTimeout,
        CancellationToken cancellationToken)
    {
        if (!_isStarted)
        {
            return null;
        }

        var endTime = DateTime.UtcNow + receiveTimeout;
        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            if (_activityQueue.TryDequeue(out var message))
            {
                return new TaskActivityWorkItem
                {
                    Id = Guid.NewGuid().ToString(),
                    TaskMessage = message,
                };
            }

            await Task.Delay(100, cancellationToken);
        }

        return null;
    }

    public Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
    {
        if (workItem.TaskMessage != null)
        {
            _activityQueue.Enqueue(workItem.TaskMessage);
        }

        return Task.CompletedTask;
    }

    public Task<TaskActivityWorkItem> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem) =>
        Task.FromResult(workItem);

    public Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
    {
        if (responseMessage != null)
        {
            _orchestrationQueue.Enqueue(responseMessage);
        }

        return Task.CompletedTask;
    }

    public bool IsMaxMessageCountExceeded(int currentMessageCount, OrchestrationRuntimeState runtimeState) =>
        currentMessageCount > 1000;

    public int GetDelayInSecondsAfterOnProcessException(Exception exception) => 5;

    public int GetDelayInSecondsAfterOnFetchException(Exception exception) => 5;

    #endregion

    #region IOrchestrationServiceClient Implementation

    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
    {
        var instanceId = creationMessage.OrchestrationInstance.InstanceId;

        // Create initial state
        var state = new OrchestrationState
        {
            OrchestrationInstance = creationMessage.OrchestrationInstance,
            Name = creationMessage.OrchestrationInstance.InstanceId,
            OrchestrationStatus = OrchestrationStatus.Pending,
            CreatedTime = DateTime.UtcNow,
            LastUpdatedTime = DateTime.UtcNow,
            Input = null,
        };

        _instances[instanceId] = state;
        _orchestrationQueue.Enqueue(creationMessage);

        _logger.LogDebug("Created orchestration {InstanceId}", instanceId);
        return Task.CompletedTask;
    }

    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[] dedupeStatuses) =>
        CreateTaskOrchestrationAsync(creationMessage);

    public Task SendTaskOrchestrationMessageAsync(TaskMessage message)
    {
        _orchestrationQueue.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
    {
        foreach (var msg in messages)
        {
            _orchestrationQueue.Enqueue(msg);
        }

        return Task.CompletedTask;
    }

    public async Task<OrchestrationState> WaitForOrchestrationAsync(
        string instanceId,
        string? executionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var endTime = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            if (_instances.TryGetValue(instanceId, out var state))
            {
                if (state.OrchestrationStatus == OrchestrationStatus.Completed ||
                    state.OrchestrationStatus == OrchestrationStatus.Failed ||
                    state.OrchestrationStatus == OrchestrationStatus.Terminated)
                {
                    return state;
                }
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"Orchestration {instanceId} did not complete within {timeout}");
    }

    public Task ForceTerminateTaskOrchestrationAsync(string instanceId, string? reason)
    {
        if (_instances.TryGetValue(instanceId, out var state))
        {
            state.OrchestrationStatus = OrchestrationStatus.Terminated;
            state.LastUpdatedTime = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task<OrchestrationState?> GetOrchestrationStateAsync(string instanceId, string? executionId)
    {
        _instances.TryGetValue(instanceId, out var state);
        return Task.FromResult(state);
    }

    public Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
    {
        var result = new List<OrchestrationState>();
        if (_instances.TryGetValue(instanceId, out var state))
        {
            result.Add(state);
        }

        return Task.FromResult<IList<OrchestrationState>>(result);
    }

    public Task<string> GetOrchestrationHistoryAsync(string instanceId, string? executionId)
    {
        if (_history.TryGetValue(instanceId, out var events))
        {
            return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(events));
        }

        return Task.FromResult("[]");
    }

    public Task PurgeOrchestrationHistoryAsync(
        DateTime thresholdDateTimeUtc,
        OrchestrationStateTimeRangeFilterType timeRangeFilterType)
    {
        var toRemove = _instances
            .Where(kvp => kvp.Value.CreatedTime < thresholdDateTimeUtc)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _instances.TryRemove(key, out _);
            _history.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    #endregion
}
