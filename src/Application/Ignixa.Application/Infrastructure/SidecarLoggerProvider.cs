// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ignixa.Sidecar.Logging;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// ILoggerProvider implementation that sends logs to the sidecar logging service via gRPC.
/// Uses a background queue with batching for performance.
/// Fire-and-forget: Never blocks application logging if sidecar is unavailable.
/// </summary>
public sealed class SidecarLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SidecarLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly LoggingService.LoggingServiceClient _client;
    private readonly SidecarOptions _options;
    private readonly Channel<SidecarLogEntry> _channel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly LogLevel _minimumLevel;
    private bool _disposed;

    public SidecarLoggerProvider(
        LoggingService.LoggingServiceClient client,
        SidecarOptions options)
    {
        _client = client;
        _options = options;
        _minimumLevel = ParseLogLevel(options.MinimumLogLevel);

        // Create bounded channel to provide backpressure
        _channel = Channel.CreateBounded<SidecarLogEntry>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest logs if queue is full
            SingleReader = true,
            SingleWriter = false
        });

        // Start background processing task
        _processingTask = ProcessLogEntriesAsync(_disposeCts.Token);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new SidecarLogger(name, this, _minimumLevel));
    }

    /// <summary>
    /// Enqueues a log entry for background processing.
    /// Fire-and-forget: Never throws, silently drops if queue is full.
    /// </summary>
    public void EnqueueLogEntry(SidecarLogEntry entry)
    {
        // Try to write without blocking - drop if channel is full
        _channel.Writer.TryWrite(entry);
    }

    private async Task ProcessLogEntriesAsync(CancellationToken cancellationToken)
    {
        var batch = new List<SidecarLogEntry>(_options.LogBatchSize);
        var flushInterval = TimeSpan.FromMilliseconds(_options.LogFlushIntervalMs);
        var lastFlush = DateTime.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for entries or timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                try
                {
                    timeoutCts.CancelAfter(flushInterval);

                    while (batch.Count < _options.LogBatchSize)
                    {
                        if (_channel.Reader.TryRead(out var entry))
                        {
                            batch.Add(entry);
                        }
                        else
                        {
                            // Wait for more entries
                            var available = await _channel.Reader.WaitToReadAsync(timeoutCts.Token);
                            if (!available)
                            {
                                break; // Channel completed
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Timeout expired - flush what we have
                }

                // Flush batch if we have entries
                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, cancellationToken);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception)
        {
            // Log processing error - but we can't log it (we ARE the logger)
            // In production, might want to write to Console.Error as a fallback
        }

        // Final flush on shutdown
        while (_channel.Reader.TryRead(out var remainingEntry))
        {
            batch.Add(remainingEntry);
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(List<SidecarLogEntry> batch, CancellationToken cancellationToken)
    {
        try
        {
            var request = new LogBatchRequest();

            foreach (var entry in batch)
            {
                var logEntry = ConvertToProto(entry);
                request.Entries.Add(logEntry);
            }

            await _client.LogEntryBatchAsync(request, cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            // Sidecar unavailable - logs are lost but we don't break the application
            // Fire-and-forget pattern: silently discard
        }
        catch (Exception)
        {
            // Any other error - silently discard to avoid breaking application logging
        }
    }

    private static LogEntryRequest ConvertToProto(SidecarLogEntry entry)
    {
        var request = new LogEntryRequest
        {
            Timestamp = Timestamp.FromDateTimeOffset(entry.Timestamp),
            Level = ConvertLogLevel(entry.Level),
            Category = entry.CategoryName,
            EventId = entry.EventId,
            EventName = entry.EventName ?? string.Empty,
            Message = entry.Message
        };

        // Add exception info
        if (entry.Exception is not null)
        {
            request.Exception = ConvertException(entry.Exception);
        }

        // Add state properties
        foreach (var (key, value) in entry.State)
        {
            request.State.Add(key, value);
        }

        // Add scope info
        foreach (var scope in entry.Scopes)
        {
            var scopeInfo = new ScopeInfo
            {
                Message = scope.Message
            };

            foreach (var (key, value) in scope.State)
            {
                scopeInfo.State.Add(key, value);
            }

            request.Scopes.Add(scopeInfo);
        }

        // Add trace context if available
        var activity = Activity.Current;
        if (activity is not null)
        {
            request.TraceId = activity.TraceId.ToString();
            request.SpanId = activity.SpanId.ToString();
        }

        return request;
    }

    private static Ignixa.Sidecar.Logging.LogLevel ConvertLogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => Ignixa.Sidecar.Logging.LogLevel.Trace,
            LogLevel.Debug => Ignixa.Sidecar.Logging.LogLevel.Debug,
            LogLevel.Information => Ignixa.Sidecar.Logging.LogLevel.Information,
            LogLevel.Warning => Ignixa.Sidecar.Logging.LogLevel.Warning,
            LogLevel.Error => Ignixa.Sidecar.Logging.LogLevel.Error,
            LogLevel.Critical => Ignixa.Sidecar.Logging.LogLevel.Critical,
            LogLevel.None => Ignixa.Sidecar.Logging.LogLevel.None,
            _ => Ignixa.Sidecar.Logging.LogLevel.Unspecified
        };
    }

    private static ExceptionInfo ConvertException(Exception ex)
    {
        var info = new ExceptionInfo
        {
            Type = ex.GetType().FullName ?? ex.GetType().Name,
            Message = ex.Message,
            StackTrace = ex.StackTrace ?? string.Empty
        };

        if (ex.InnerException is not null)
        {
            info.Inner = ConvertException(ex.InnerException);
        }

        return info;
    }

    private static LogLevel ParseLogLevel(string levelString)
    {
        return System.Enum.TryParse<LogLevel>(levelString, ignoreCase: true, out var level)
            ? level
            : LogLevel.Information;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();

        try
        {
            // Wait briefly for the processing task to complete
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Task cancelled
        }

        _channel.Writer.Complete();
        _disposeCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _disposeCts.CancelAsync();

        try
        {
            // Wait briefly for the processing task to complete
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (TimeoutException)
        {
            // Timeout waiting for shutdown
        }

        _channel.Writer.Complete();
        _disposeCts.Dispose();
    }
}
