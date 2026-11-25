// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>A clock whose timers never fire, so a delay on it ends only through its cancellation token.</summary>
internal sealed class StoppedClock : TimeProvider
{
    private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a delay has started waiting on this clock.</summary>
    public Task TimerCreated => _timerCreated.Task;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _timerCreated.TrySetResult();
        return new StoppedTimer();
    }

    private sealed class StoppedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
