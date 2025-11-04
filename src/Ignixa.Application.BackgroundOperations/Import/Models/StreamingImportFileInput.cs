// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for StreamingImportFileActivity.
/// Streams a single NDJSON file through a Channel-based pipeline.
/// Global tuning parameters (ConsumerCount) are read from IConfiguration in the activity.
/// Per-import tuning parameters (BatchSize, ChannelCapacity) are passed via this input.
/// </summary>
public record StreamingImportFileInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required string FileUrl { get; init; }
    public required string ResourceType { get; init; }
    public required string Mode { get; init; } // "InitialLoad" or "IncrementalLoad"

    /// <summary>
    /// Number of resources per batch for BatchWriteAsync (default: 100).
    /// Larger batches = fewer database round trips but more memory.
    /// Can be tuned per-import via ImportOrchestrationInput.
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Channel capacity for buffering resources (default: 1000).
    /// Provides smooth flow between producer (file reading) and consumers (database writes).
    /// Can be tuned per-import via ImportOrchestrationInput.
    /// </summary>
    public int ChannelCapacity { get; init; } = 1000;
}
