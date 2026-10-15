// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A clock that only moves when the test says so.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    /// <summary>The wall-clock time the provider reports; 2026-10-15T12:00:00Z unless a test sets it.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => _timestamp += (long)(by.TotalSeconds * TimestampFrequency);
}
