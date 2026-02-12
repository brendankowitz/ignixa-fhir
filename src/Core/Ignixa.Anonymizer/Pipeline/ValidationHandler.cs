// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Models;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Handler that validates input FHIR resources before anonymization.
/// Only performs validation when enabled in settings.
/// </summary>
internal sealed class ValidationHandler(ILogger<ValidationHandler> logger, IValidationSchemaResolver schemaResolver) : AnonymizerPipelineHandler
{

    /// <inheritdoc />
    public override async ValueTask<Result<AnonymizationResult>> InvokeAsync(
        AnonymizerContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken)
    {
        if (context.Settings.ValidateInput)
        {
            logger.LogDebug("Validating input resource");

            var resourceType = context.Resource.ResourceType;
            var schema = schemaResolver.GetSchema(resourceType);

            if (schema is null)
            {
                logger.LogWarning("Validation schema not found for {CanonicalUrl}", resourceType);
                return Result<AnonymizationResult>.Failure(new AnonymizerError(
                    "VALIDATION_SCHEMA_NOT_FOUND",
                    $"Validation schema not found for {resourceType}",
                    ErrorSeverity.Error));
            }

            var settings = new ValidationSettings
            {
                Depth = ValidationDepth.Minimal,  // Fast validation for anonymizer
                SkipTerminologyValidation = true   // Skip terminology checks
            };

            var validationResult = schema.Validate(context.Element, settings);

            if (!validationResult.IsValid)
            {
                var errorCount = validationResult.Issues.Count(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);
                logger.LogWarning("Input validation failed with {ErrorCount} error(s)", errorCount);

                var errorMessages = validationResult.Issues
                    .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
                    .Select(i => $"{i.Path}: {i.Message}")
                    .Take(5);  // Limit to first 5 errors

                return Result<AnonymizationResult>.Failure(new AnonymizerError(
                    "VALIDATION_FAILED",
                    $"Input validation failed: {string.Join("; ", errorMessages)}",
                    ErrorSeverity.Error));
            }
        }

        return await nextHandler(context, cancellationToken).ConfigureAwait(false);
    }
}
