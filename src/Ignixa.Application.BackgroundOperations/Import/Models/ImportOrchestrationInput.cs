// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for import orchestration.
/// </summary>
public record ImportOrchestrationInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required IReadOnlyList<InputFileInfo> InputFiles { get; init; }
    public required string Mode { get; init; } // "InitialLoad" or "IncrementalLoad"
    public ParametersJsonNode? StorageDetail { get; init; } // SAS tokens, etc.

    // Per-import performance tuning
    /// <summary>
    /// Resources per batch (default: 100).
    /// Consumers accumulate this many resources before calling BatchWriteAsync.
    /// Larger batches = fewer database round trips, smaller batches = lower latency.
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Channel capacity between producer and consumers (default: 1000).
    /// Provides backpressure: producer waits if channel is full.
    /// Prevents unbounded memory growth from queued resources.
    /// </summary>
    public int ChannelCapacity { get; init; } = 1000;
}
