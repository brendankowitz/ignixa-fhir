// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Models;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Represents a pipeline that orchestrates de-identification processing through a chain of middleware.
/// </summary>
public interface IDeIdPipeline
{
    /// <summary>
    /// Executes the de-identification pipeline for the given context.
    /// </summary>
    /// <param name="context">The de-identification context containing the resource and settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the de-identification result or an error.</returns>
    ValueTask<Result<DeIdResult>> ExecuteAsync(
        DeIdContext context,
        CancellationToken cancellationToken);
}
