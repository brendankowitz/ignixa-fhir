// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// ILogger implementation that queues log entries for batched transmission to the sidecar logging service.
/// Fire-and-forget: Never throws exceptions that would break application logging.
/// </summary>
public sealed class SidecarLogger : ILogger
{
    private readonly string _categoryName;
    private readonly SidecarLoggerProvider _provider;
    private readonly LogLevel _minimumLevel;

    /// <summary>
    /// Thread-local scope stack for tracking nested scopes.
    /// </summary>
    private static readonly AsyncLocal<ScopeStack?> CurrentScopeStack = new();

    public SidecarLogger(string categoryName, SidecarLoggerProvider provider, LogLevel minimumLevel)
    {
        _categoryName = categoryName;
        _provider = provider;
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return new LoggerScope<TState>(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= _minimumLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);

        var message = formatter(state, exception);

        // Capture structured state
        var stateProperties = new Dictionary<string, string>();
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
        {
            foreach (var kvp in stateList)
            {
                if (kvp.Value is not null)
                {
                    stateProperties[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
                }
            }
        }

        // Capture scopes
        var scopes = CaptureScopes();

        // Create log entry
        var entry = new SidecarLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = logLevel,
            CategoryName = _categoryName,
            EventId = eventId.Id,
            EventName = eventId.Name,
            Message = message,
            Exception = exception,
            State = stateProperties,
            Scopes = scopes
        };

        // Queue for background processing (fire-and-forget)
        _provider.EnqueueLogEntry(entry);
    }

    private List<ScopeData> CaptureScopes()
    {
        var scopes = new List<ScopeData>();
        var scopeStack = CurrentScopeStack.Value;

        while (scopeStack is not null)
        {
            scopes.Add(new ScopeData
            {
                Message = scopeStack.FormattedMessage,
                State = scopeStack.Properties
            });
            scopeStack = scopeStack.Parent;
        }

        // Reverse to get outer scope first
        scopes.Reverse();
        return scopes;
    }

    /// <summary>
    /// Represents a logging scope that captures state properties.
    /// </summary>
    private sealed class LoggerScope<TState> : IDisposable where TState : notnull
    {
        private readonly ScopeStack? _previousScope;
        private bool _disposed;

        public LoggerScope(TState state)
        {
            var formattedMessage = state?.ToString() ?? string.Empty;
            var properties = new Dictionary<string, string>();

            if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
            {
                foreach (var kvp in stateList)
                {
                    if (kvp.Value is not null)
                    {
                        properties[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
                    }
                }
            }

            _previousScope = CurrentScopeStack.Value;
            CurrentScopeStack.Value = new ScopeStack
            {
                FormattedMessage = formattedMessage,
                Properties = properties,
                Parent = _previousScope
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CurrentScopeStack.Value = _previousScope;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Stack node for tracking nested scopes.
    /// </summary>
    private sealed class ScopeStack
    {
        public required string FormattedMessage { get; init; }
        public required Dictionary<string, string> Properties { get; init; }
        public ScopeStack? Parent { get; init; }
    }
}

/// <summary>
/// Represents a log entry to be sent to the sidecar.
/// </summary>
public sealed class SidecarLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string CategoryName { get; init; }
    public int EventId { get; init; }
    public string? EventName { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
    public Dictionary<string, string> State { get; init; } = [];
    public IList<ScopeData> Scopes { get; init; } = [];
}

/// <summary>
/// Represents scope data captured from ILogger.BeginScope.
/// </summary>
public sealed class ScopeData
{
    public required string Message { get; init; }
    public Dictionary<string, string> State { get; init; } = [];
}
