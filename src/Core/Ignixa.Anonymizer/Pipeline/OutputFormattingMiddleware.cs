// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Validation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Terminal middleware that formats output JSON and builds the final result.
/// This middleware does NOT call the next delegate - it terminates the pipeline.
/// </summary>
public sealed class OutputFormattingMiddleware(ILogger<OutputFormattingMiddleware> logger) : AnonymizerMiddleware
{
    private readonly ResourceValidator _validator = new();

    /// <inheritdoc />
    public override ValueTask<Result<AnonymizationResult>> InvokeAsync(
        AnonymizerContext context,
        AnonymizerDelegate nextMiddleware,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Formatting output (pretty={IsPretty}, validateOutput={ValidateOutput})",
            context.Settings.IsPrettyOutput,
            context.Settings.ValidateOutput);

        var result = context.BuildResult();

        if (context.Settings.ValidateOutput)
        {
            try
            {
                _validator.ValidateOutput(result.AnonymizedJson);
            }
            catch (ResourceNotValidException ex)
            {
                logger.LogWarning(ex, "Output validation failed");
                return ValueTask.FromResult(Result<AnonymizationResult>.Failure(new AnonymizerError(
                    "OUTPUT_VALIDATION_FAILED",
                    $"Output validation failed: {ex.Message}",
                    ErrorSeverity.Error,
                    ex)));
            }
        }

        logger.LogDebug(
            "Pipeline complete: {NodesProcessed} nodes processed in {Duration}ms",
            result.Metrics.NodesProcessed,
            result.Metrics.Duration.TotalMilliseconds);

        return ValueTask.FromResult(Result<AnonymizationResult>.Success(result));
    }
}
