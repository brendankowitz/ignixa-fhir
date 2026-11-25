// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Samples this process's private bytes and working set every 25 ms on a background thread until stopped, so a
/// measurement window can be reported apart from the process-lifetime peak the operating system keeps.
/// </summary>
internal sealed class MemorySampler : IDisposable
{
    private readonly Thread _thread;
    private volatile bool _stopped;
    private long _peakPrivateBytes;
    private long _peakWorkingSetBytes;

    public MemorySampler()
    {
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Stops sampling and returns the window's peaks in bytes.</summary>
    public (long PeakPrivateBytes, long PeakWorkingSetBytes) Stop()
    {
        _stopped = true;
        _thread.Join();
        return (_peakPrivateBytes, _peakWorkingSetBytes);
    }

    public void Dispose()
    {
        _stopped = true;
        _thread.Join();
    }

    private void Run()
    {
        using var process = Process.GetCurrentProcess();
        while (!_stopped)
        {
            process.Refresh();
            _peakPrivateBytes = Math.Max(_peakPrivateBytes, process.PrivateMemorySize64);
            _peakWorkingSetBytes = Math.Max(_peakWorkingSetBytes, process.WorkingSet64);
            Thread.Sleep(25);
        }
    }
}
