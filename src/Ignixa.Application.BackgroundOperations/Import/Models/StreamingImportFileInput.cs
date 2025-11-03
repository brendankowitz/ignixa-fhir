// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for StreamingImportFileActivity.
/// Streams a single NDJSON file through a Channel-based pipeline with configurable parallelism.
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
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Number of parallel consumer threads processing batches (default: 8).
    /// Higher parallelism = better throughput for fast networks/storage.
    /// Target: 8 consumers × 100 resources/batch × 2.5 batches/sec ≈ 2,000 resources/sec per file.
    /// </summary>
    public int ConsumerCount { get; init; } = 8;

    /// <summary>
    /// Channel capacity for buffering resources (default: 1000).
    /// Provides smooth flow between producer (file reading) and consumers (database writes).
    /// Should be at least 2× (ConsumerCount × BatchSize) for optimal throughput.
    /// </summary>
    public int ChannelCapacity { get; init; } = 1000;
}
