// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Models;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Represents a pipeline that orchestrates anonymization processing through a chain of middleware.
/// </summary>
public interface IAnonymizerPipeline
{
    /// <summary>
    /// Executes the anonymization pipeline for the given context.
    /// </summary>
    /// <param name="context">The anonymization context containing the resource and settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anonymization result or an error.</returns>
    ValueTask<Result<AnonymizationResult>> ExecuteAsync(
        AnonymizerContext context,
        CancellationToken cancellationToken);
}
