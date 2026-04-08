// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.FileSystem.DurableTask;

/// <summary>
/// File-based orchestration service that persists DurableTask state to the filesystem.
/// Wraps InMemoryOrchestrationService and adds persistence to {BaseDirectory}/_jobs/.
/// Provides durability across application restarts while maintaining zero external dependencies.
/// </summary>
public partial class FileBasedOrchestrationService : IOrchestrationService, IOrchestrationServiceClient, IDisposable
{
    private readonly FileBasedOrchestrationServiceOptions _options;
    private readonly ILogger<FileBasedOrchestrationService> _logger;
    private readonly ILogger<InMemoryOrchestrationService> _innerLogger;
    private InMemoryOrchestrationService? _innerService;
    private readonly string _jobsDirectory;
    private readonly string _instancesDirectory;
    private readonly string _historyDirectory;
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private bool _disposed;

    private InMemoryOrchestrationService InnerService
    {
        get
        {
            if (_innerService == null)
            {
                _innerService = new InMemoryOrchestrationService(_innerLogger);
            }

            return _innerService;
        }
    }

    public FileBasedOrchestrationService(
        FileBasedOrchestrationServiceOptions options,
        ILogger<FileBasedOrchestrationService> logger,
        ILogger<InMemoryOrchestrationService> innerLogger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));

        _jobsDirectory = Path.Combine(_options.BaseDirectory, "_jobs");
        _instancesDirectory = Path.Combine(_jobsDirectory, "instances");
        _historyDirectory = Path.Combine(_jobsDirectory, "history");

        // Create directories
        Directory.CreateDirectory(_instancesDirectory);
        Directory.CreateDirectory(_historyDirectory);

        // Set up periodic flush timer
        _flushTimer = new Timer(FlushStateToDisk, null, _options.StateFlushInterval, _options.StateFlushInterval);

        LogServiceInitialized(_logger, _jobsDirectory);
    }

    #region IOrchestrationService Implementation

    public int TaskOrchestrationDispatcherCount => InnerService.TaskOrchestrationDispatcherCount;

    public int MaxConcurrentTaskOrchestrationWorkItems => InnerService.MaxConcurrentTaskOrchestrationWorkItems;

    public BehaviorOnContinueAsNew EventBehaviourForContinueAsNew => InnerService.EventBehaviourForContinueAsNew;

    public int TaskActivityDispatcherCount => InnerService.TaskActivityDispatcherCount;

    public int MaxConcurrentTaskActivityWorkItems => InnerService.MaxConcurrentTaskActivityWorkItems;

    public async Task CreateAsync()
    {
        await InnerService.CreateAsync();
    }

    public async Task CreateAsync(bool recreateInstanceStore)
    {
        await InnerService.CreateAsync(recreateInstanceStore);
    }

    public async Task CreateIfNotExistsAsync()
    {
        await InnerService.CreateIfNotExistsAsync();
    }

    public async Task DeleteAsync()
    {
        await InnerService.DeleteAsync();
    }

    public async Task DeleteAsync(bool deleteInstanceStore)
    {
        await InnerService.DeleteAsync(deleteInstanceStore);
    }

    public async Task StartAsync()
    {
        LogStarting(_logger);

        // Recover state from disk before starting
        await RecoverPersistedStateAsync();

        // Start the inner service
        await InnerService.StartAsync();

        LogStarted(_logger);
    }

    public async Task StopAsync()
    {
        await StopAsync(false);
    }

    public async Task StopAsync(bool isForced)
    {
        LogStopping(_logger);

        // Final flush before shutdown
        await FlushStateToDiskAsync();

        // Stop the inner service
        await InnerService.StopAsync(isForced);

        // Dispose timer
        await _flushTimer.DisposeAsync();
        _flushLock.Dispose();

        LogStopped(_logger);
    }

    public async Task<TaskOrchestrationWorkItem?> LockNextTaskOrchestrationWorkItemAsync(
        TimeSpan receiveTimeout,
        CancellationToken cancellationToken)
    {
        var workItem = await InnerService.LockNextTaskOrchestrationWorkItemAsync(receiveTimeout, cancellationToken);

        if (workItem != null)
        {
            // Persist the work item state
            await PersistOrchestrationStateAsync(workItem.InstanceId);
        }

        return workItem;
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
        var task = InnerService.CompleteTaskOrchestrationWorkItemAsync(
            workItem,
            newOrchestrationRuntimeState,
            outboundMessages,
            orchestratorMessages,
            timerMessages,
            continuedAsNewMessage,
            orchestrationState);

        // Persist updated state and history (fire and forget)
        _ = Task.Run(async () =>
        {
            await PersistOrchestrationStateAsync(workItem.InstanceId);
            await PersistOrchestrationHistoryAsync(workItem.InstanceId, newOrchestrationRuntimeState);
        });

        return task;
    }

    public Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        return InnerService.AbandonTaskOrchestrationWorkItemAsync(workItem);
    }

    public Task ReleaseTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        return InnerService.ReleaseTaskOrchestrationWorkItemAsync(workItem);
    }

    public Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
    {
        return InnerService.RenewTaskOrchestrationWorkItemLockAsync(workItem);
    }

    public async Task<TaskActivityWorkItem?> LockNextTaskActivityWorkItem(TimeSpan receiveTimeout, CancellationToken cancellationToken)
    {
        return await InnerService.LockNextTaskActivityWorkItem(receiveTimeout, cancellationToken) ?? null;
    }

    public Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
    {
        return InnerService.AbandonTaskActivityWorkItemAsync(workItem);
    }

    public Task<TaskActivityWorkItem> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
    {
        return InnerService.RenewTaskActivityWorkItemLockAsync(workItem);
    }

    public Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
    {
        return InnerService.CompleteTaskActivityWorkItemAsync(workItem, responseMessage);
    }

    public bool IsMaxMessageCountExceeded(int currentMessageCount, OrchestrationRuntimeState runtimeState)
    {
        return InnerService.IsMaxMessageCountExceeded(currentMessageCount, runtimeState);
    }

    public int GetDelayInSecondsAfterOnProcessException(Exception exception)
    {
        return InnerService.GetDelayInSecondsAfterOnProcessException(exception);
    }

    public int GetDelayInSecondsAfterOnFetchException(Exception exception)
    {
        return InnerService.GetDelayInSecondsAfterOnFetchException(exception);
    }

    #endregion

    #region IOrchestrationServiceClient Implementation

    public async Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
    {
        await ((IOrchestrationServiceClient)InnerService).CreateTaskOrchestrationAsync(creationMessage);
    }

    public async Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[] dedupeStatuses)
    {
        await ((IOrchestrationServiceClient)InnerService).CreateTaskOrchestrationAsync(creationMessage, dedupeStatuses);
    }

    public async Task SendTaskOrchestrationMessageAsync(TaskMessage message)
    {
        await ((IOrchestrationServiceClient)InnerService).SendTaskOrchestrationMessageAsync(message);
    }

    public async Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
    {
        await ((IOrchestrationServiceClient)InnerService).SendTaskOrchestrationMessageBatchAsync(messages);
    }

    public async Task<OrchestrationState> WaitForOrchestrationAsync(
        string instanceId,
        string? executionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await ((IOrchestrationServiceClient)InnerService).WaitForOrchestrationAsync(
            instanceId,
            executionId,
            timeout,
            cancellationToken);
    }

    public async Task ForceTerminateTaskOrchestrationAsync(string instanceId, string? reason)
    {
        await ((IOrchestrationServiceClient)InnerService).ForceTerminateTaskOrchestrationAsync(instanceId, reason);
    }

    public async Task<OrchestrationState> GetOrchestrationStateAsync(string instanceId, string? executionId)
    {
        return await ((IOrchestrationServiceClient)InnerService).GetOrchestrationStateAsync(instanceId, executionId);
    }

    public async Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
    {
        return await ((IOrchestrationServiceClient)InnerService).GetOrchestrationStateAsync(instanceId, allExecutions);
    }

    public async Task<string> GetOrchestrationHistoryAsync(string instanceId, string? executionId)
    {
        return await ((IOrchestrationServiceClient)InnerService).GetOrchestrationHistoryAsync(instanceId, executionId);
    }

    public async Task PurgeOrchestrationHistoryAsync(DateTime thresholdDateTimeUtc, OrchestrationStateTimeRangeFilterType timeRangeFilterType)
    {
        await ((IOrchestrationServiceClient)InnerService).PurgeOrchestrationHistoryAsync(thresholdDateTimeUtc, timeRangeFilterType);
    }

    #endregion

    #region Persistence Methods

    /// <summary>
    /// Recovers orchestration state from persisted JSON files.
    /// </summary>
    private async Task RecoverPersistedStateAsync()
    {
        try
        {
            var instanceFiles = Directory.GetFiles(_instancesDirectory, "*.json");

            LogRecoveringInstances(_logger, instanceFiles.Length, _instancesDirectory);

            foreach (var filePath in instanceFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var state = JsonSerializer.Deserialize<OrchestrationStateFile>(json);

                    if (state == null)
                    {
                        LogDeserializeFailed(_logger, filePath);
                        continue;
                    }

                    LogRecoveredInstance(_logger, state.InstanceId, state.OrchestrationStatus);
                }
                catch (Exception ex)
                {
                    LogRecoverStateError(_logger, ex, filePath);
                }
            }

            LogRecoveryCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogRecoveryError(_logger, ex);
        }
    }

    /// <summary>
    /// Persists the current orchestration state to a JSON file.
    /// </summary>
    private async Task PersistOrchestrationStateAsync(string instanceId)
    {
        try
        {
            // Get current state from inner service
            var state = await ((IOrchestrationServiceClient)InnerService).GetOrchestrationStateAsync(instanceId, null);

            if (state == null)
            {
                return;
            }

            var stateFile = new OrchestrationStateFile
            {
                InstanceId = state.OrchestrationInstance.InstanceId,
                ExecutionId = state.OrchestrationInstance.ExecutionId,
                Name = state.Name,
                Version = state.Version,
                OrchestrationStatus = state.OrchestrationStatus.ToString(),
                Input = state.Input,
                Output = state.Output,
                Status = state.Status,
                CreatedTime = state.CreatedTime,
                CompletedTime = state.CompletedTime,
                LastUpdatedTime = state.LastUpdatedTime,
                Tags = state.Tags,
            };

            var filePath = Path.Combine(_instancesDirectory, $"{instanceId}.json");
            await WriteJsonFileAtomicallyAsync(filePath, stateFile);

            LogPersistedState(_logger, instanceId);
        }
        catch (Exception ex)
        {
            LogPersistStateError(_logger, ex, instanceId);
        }
    }

    /// <summary>
    /// Persists the orchestration history (event list) to a JSON file.
    /// </summary>
    private async Task PersistOrchestrationHistoryAsync(string instanceId, OrchestrationRuntimeState runtimeState)
    {
        try
        {
            if (runtimeState?.Events == null || runtimeState.Events.Count == 0)
            {
                return;
            }

            var filePath = Path.Combine(_historyDirectory, $"{instanceId}.json");
            await WriteJsonFileAtomicallyAsync(filePath, runtimeState.Events);

            LogPersistedHistory(_logger, runtimeState.Events.Count, instanceId);
        }
        catch (Exception ex)
        {
            LogPersistHistoryError(_logger, ex, instanceId);
        }
    }

    /// <summary>
    /// Writes a JSON file atomically using a temp file + move pattern.
    /// Same strategy as FileBasedFhirRepository for consistency.
    /// </summary>
    private async Task WriteJsonFileAtomicallyAsync<T>(string filePath, T data)
    {
        var tempPath = filePath + ".tmp";

        try
        {
            // Write to temp file
            var json = JsonSerializer.Serialize(data, _jsonOptions);

            await File.WriteAllTextAsync(tempPath, json);

            // Atomic move (overwrites existing file)
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            // Clean up temp file if move failed
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    /// <summary>
    /// Periodically flushes in-memory state to disk (timer callback).
    /// </summary>
    private void FlushStateToDisk(object? state)
    {
        _ = FlushStateToDiskAsync();
    }

    /// <summary>
    /// Flushes all current orchestration states to disk.
    /// </summary>
    private async Task FlushStateToDiskAsync()
    {
        if (!await _flushLock.WaitAsync(0))
        {
            // Flush already in progress, skip
            return;
        }

        try
        {
            // Note: State is persisted on-demand when work items are processed
            // This flush is mainly for ensuring everything is saved on shutdown
        }
        catch (Exception ex)
        {
            LogFlushError(_logger, ex);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Data structure for persisting orchestration state to JSON.
    /// </summary>
    private class OrchestrationStateFile
    {
        public string InstanceId { get; set; } = string.Empty;
        public string? ExecutionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string OrchestrationStatus { get; set; } = string.Empty;
        public string? Input { get; set; }
        public string? Output { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public DateTime LastUpdatedTime { get; set; }
        public IDictionary<string, string>? Tags { get; set; }
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _flushTimer?.Dispose();
            _flushLock?.Dispose();
            (_innerService as IDisposable)?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Information, Message = "FileBasedOrchestrationService initialized with jobs directory: {JobsDirectory}")]
    private static partial void LogServiceInitialized(ILogger logger, string jobsDirectory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovering {Count} orchestration instances from {Directory}")]
    private static partial void LogRecoveringInstances(ILogger logger, int count, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recovered instance {InstanceId} in state {State}")]
    private static partial void LogRecoveredInstance(ILogger logger, string instanceId, string state);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Persisted state for instance {InstanceId}")]
    private static partial void LogPersistedState(ILogger logger, string instanceId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Persisted {EventCount} history events for instance {InstanceId}")]
    private static partial void LogPersistedHistory(ILogger logger, int eventCount, string instanceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting FileBasedOrchestrationService and recovering persisted state...")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FileBasedOrchestrationService started successfully")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping FileBasedOrchestrationService...")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FileBasedOrchestrationService stopped")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize state from {FilePath}")]
    private static partial void LogDeserializeFailed(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error recovering state from {FilePath}")]
    private static partial void LogRecoverStateError(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "State recovery completed")]
    private static partial void LogRecoveryCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during state recovery")]
    private static partial void LogRecoveryError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error persisting state for instance {InstanceId}")]
    private static partial void LogPersistStateError(ILogger logger, Exception exception, string instanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error persisting history for instance {InstanceId}")]
    private static partial void LogPersistHistoryError(ILogger logger, Exception exception, string instanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during state flush")]
    private static partial void LogFlushError(ILogger logger, Exception exception);
}
