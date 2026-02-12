// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Models;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Abstract base class for anonymization pipeline handlers.
/// Handlers process the anonymization request and optionally delegate to the next handler.
/// </summary>
public abstract class AnonymizerPipelineHandler
{
    /// <summary>
    /// Processes the anonymization request.
    /// </summary>
    /// <param name="context">The anonymization context.</param>
    /// <param name="nextHandler">The next handler in the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anonymization result or an error.</returns>
    public abstract ValueTask<Result<AnonymizationResult>> InvokeAsync(
        AnonymizerContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken);
}
