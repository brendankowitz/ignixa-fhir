// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Models;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Terminal handler that formats output JSON and builds the final result.
/// This handler does NOT call the next delegate - it terminates the pipeline.
/// </summary>
internal sealed class OutputFormattingHandler(ILogger<OutputFormattingHandler> logger, IValidationSchemaResolver schemaResolver) : DeIdPipelineHandler
{

    /// <inheritdoc />
    public override ValueTask<Result<DeIdResult>> InvokeAsync(
        DeIdContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Formatting output (pretty={IsPretty}, validateOutput={ValidateOutput})",
            context.Settings.IsPrettyOutput,
            context.Settings.ValidateOutput);

        var result = context.BuildResult();

        if (context.Settings.ValidateOutput)
        {
            var resourceType = context.Resource.ResourceType;
            var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
            var schema = schemaResolver.GetSchema(canonicalUrl);

            if (schema is null)
            {
                logger.LogWarning("Validation schema not found for {CanonicalUrl}", canonicalUrl);
                return ValueTask.FromResult(Result<DeIdResult>.Failure(new DeIdError(
                    "VALIDATION_SCHEMA_NOT_FOUND",
                    $"Validation schema not found for {resourceType}",
                    ErrorSeverity.Error)));
            }

            var settings = new ValidationSettings
            {
                Depth = ValidationDepth.Minimal,  // Fast validation for de-identifier
                SkipTerminologyValidation = true   // Skip terminology checks
            };

            var validationResult = schema.Validate(context.Element, settings);

            if (!validationResult.IsValid)
            {
                var errorCount = validationResult.Issues.Count(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);
                logger.LogWarning("Output validation failed with {ErrorCount} error(s)", errorCount);

                var errorMessages = validationResult.Issues
                    .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
                    .Select(i => $"{i.Path}: {i.Message}")
                    .Take(5);  // Limit to first 5 errors

                return ValueTask.FromResult(Result<DeIdResult>.Failure(new DeIdError(
                    "OUTPUT_VALIDATION_FAILED",
                    $"Output validation failed: {string.Join("; ", errorMessages)}",
                    ErrorSeverity.Error)));
            }
        }

        logger.LogDebug(
            "Pipeline complete: {NodesProcessed} nodes processed in {Duration}ms",
            result.Metrics.NodesProcessed,
            result.Metrics.Duration.TotalMilliseconds);

        return ValueTask.FromResult(Result<DeIdResult>.Success(result));
    }
}
