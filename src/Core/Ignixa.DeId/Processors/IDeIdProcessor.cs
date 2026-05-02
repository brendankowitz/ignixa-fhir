// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.DeId.Models;

namespace Ignixa.DeId.Processors;

/// <summary>
/// Interface for FHIR resource de-identification processors.
/// </summary>
public interface IDeIdProcessor
{
    /// <summary>
    /// Processes a FHIR resource node asynchronously, applying de-identification transformations.
    /// </summary>
    /// <param name="resource">The resource JSON node to modify.</param>
    /// <param name="node">The current element being processed.</param>
    /// <param name="context">Processing context with settings and state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the processor result or an error.</returns>
    ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken);
}
