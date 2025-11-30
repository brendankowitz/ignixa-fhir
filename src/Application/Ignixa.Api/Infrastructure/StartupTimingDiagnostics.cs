// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Tracks timing of startup phases for performance diagnostics.
/// Enable via Diagnostics:StartupTiming:Enabled = true
/// </summary>
public class StartupTimingDiagnostics
{
    private readonly ConcurrentDictionary<string, TimingEntry> _timings = new();
    private readonly Stopwatch _overallStopwatch = new();
    private readonly ILogger<StartupTimingDiagnostics> _logger;
    private readonly bool _enabled;

    public StartupTimingDiagnostics(IConfiguration configuration, ILogger<StartupTimingDiagnostics> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enabled = configuration.GetValue<bool>("Diagnostics:StartupTiming:Enabled", true);

        if (_enabled)
        {
            _overallStopwatch.Start();
        }
    }

    /// <summary>
    /// Starts timing a named phase.
    /// </summary>
    public IDisposable StartPhase(string phaseName)
    {
        if (!_enabled)
        {
            return NullDisposable.Instance;
        }

        var entry = _timings.GetOrAdd(phaseName, _ => new TimingEntry(phaseName));
        entry.Stopwatch.Start();
        entry.StartCount++;

        _logger.LogDebug("[STARTUP] Starting: {Phase}", phaseName);

        return new PhaseScope(entry, _logger);
    }

    /// <summary>
    /// Logs final timing summary.
    /// </summary>
    public void LogSummary()
    {
        if (!_enabled)
        {
            return;
        }

        _overallStopwatch.Stop();

        _logger.LogInformation("========== STARTUP TIMING SUMMARY ==========");
        _logger.LogInformation("Total startup time: {TotalMs:N0}ms", _overallStopwatch.ElapsedMilliseconds);
        _logger.LogInformation("");

        var sortedTimings = _timings.Values
            .OrderByDescending(t => t.Stopwatch.ElapsedMilliseconds)
            .ToList();

        foreach (var timing in sortedTimings)
        {
            var percentage = _overallStopwatch.ElapsedMilliseconds > 0
                ? (timing.Stopwatch.ElapsedMilliseconds * 100.0 / _overallStopwatch.ElapsedMilliseconds)
                : 0;

            var invocations = timing.StartCount > 1 ? $" ({timing.StartCount}x)" : "";

            _logger.LogInformation(
                "  {Phase,-50} {Elapsed,8:N0}ms ({Percentage,5:F1}%){Invocations}",
                timing.PhaseName,
                timing.Stopwatch.ElapsedMilliseconds,
                percentage,
                invocations);
        }

        _logger.LogInformation("=============================================");

        // Log warnings for slow phases
        var threshold = TimeSpan.FromSeconds(1);
        var slowPhases = sortedTimings.Where(t => t.Stopwatch.Elapsed > threshold).ToList();

        if (slowPhases.Any())
        {
            _logger.LogWarning("Slow startup phases (>{ThresholdMs}ms):", threshold.TotalMilliseconds);
            foreach (var phase in slowPhases)
            {
                _logger.LogWarning("  - {Phase}: {Elapsed:N0}ms", phase.PhaseName, phase.Stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private class TimingEntry
    {
        public string PhaseName { get; }
        public Stopwatch Stopwatch { get; } = new();
        public int StartCount { get; set; }

        public TimingEntry(string phaseName)
        {
            PhaseName = phaseName;
        }
    }

    private class PhaseScope : IDisposable
    {
        private readonly TimingEntry _entry;
        private readonly ILogger _logger;
        private bool _disposed;

        public PhaseScope(TimingEntry entry, ILogger logger)
        {
            _entry = entry;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _entry.Stopwatch.Stop();
            _logger.LogDebug("[STARTUP] Completed: {Phase} ({Elapsed:N0}ms)", _entry.PhaseName, _entry.Stopwatch.ElapsedMilliseconds);
            _disposed = true;
        }
    }

    private class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Extension methods for startup timing.
/// </summary>
public static class StartupTimingExtensions
{
    /// <summary>
    /// Adds startup timing diagnostics to the service collection.
    /// </summary>
    public static IServiceCollection AddStartupTimingDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<StartupTimingDiagnostics>();
        return services;
    }
}
