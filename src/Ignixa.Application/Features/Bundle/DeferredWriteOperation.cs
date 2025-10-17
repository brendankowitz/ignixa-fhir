// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Represents a deferred write operation queued for batch processing.
/// Uses TaskCompletionSource to enable handlers to queue writes and await completion asynchronously.
/// </summary>
public class DeferredWriteOperation
{
    /// <summary>
    /// Gets the resource wrapper containing all resource data (type, ID, resource, JSON, metadata).
    /// Storing the wrapper directly avoids redundant field extraction and reconstruction.
    /// </summary>
    public required ResourceWrapper Wrapper { get; init; }

    /// <summary>
    /// Gets the TaskCompletionSource that will be completed when the write finishes.
    /// Handlers await this Task to get the result of the write operation.
    /// </summary>
    public required TaskCompletionSource<ResourceKey> CompletionSource { get; init; }

    /// <summary>
    /// Gets the entry index (for logging and error reporting).
    /// </summary>
    public int EntryIndex { get; init; }
}
