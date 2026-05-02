// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Models;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Abstract base class for de-identification pipeline handlers.
/// Handlers process the de-identification request and optionally delegate to the next handler.
/// </summary>
public abstract class DeIdPipelineHandler
{
    /// <summary>
    /// Processes the de-identification request.
    /// </summary>
    /// <param name="context">The de-identification context.</param>
    /// <param name="nextHandler">The next handler in the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the de-identification result or an error.</returns>
    public abstract ValueTask<Result<DeIdResult>> InvokeAsync(
        DeIdContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken);
}
