// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Configuration options for bundle processing.
/// Controls parallelism, channel capacity, and transaction behavior.
/// </summary>
public record BundleProcessingOptions
{
    /// <summary>
    /// Gets the maximum number of parallel worker tasks processing bundle entries.
    /// Default: 10 workers.
    /// Higher values increase throughput but consume more resources.
    /// </summary>
    public int MaxParallelism { get; init; } = 10;

    /// <summary>
    /// Gets the bounded channel capacity (max in-flight operations).
    /// Default: 100 entries.
    /// Controls memory usage and provides backpressure for large bundles.
    /// </summary>
    public int ChannelCapacity { get; init; } = 100;

    /// <summary>
    /// Gets the bundle type (Transaction or Batch).
    /// Determines transaction semantics and error handling.
    /// </summary>
    public BundleType Type { get; init; } = BundleType.Transaction;
}

/// <summary>
/// FHIR Bundle type enumeration.
/// </summary>
public enum BundleType
{
    /// <summary>
    /// Transaction bundle - all-or-nothing ACID semantics.
    /// Any failure rolls back all changes.
    /// </summary>
    Transaction,

    /// <summary>
    /// Batch bundle - independent execution of entries.
    /// Individual entries can succeed or fail independently.
    /// </summary>
    Batch,
}
