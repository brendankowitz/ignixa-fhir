// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Validation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Middleware that validates input FHIR resources before anonymization.
/// Only performs validation when enabled in settings.
/// </summary>
public sealed class ValidationMiddleware(ILogger<ValidationMiddleware> logger) : AnonymizerMiddleware
{
    private readonly ResourceValidator _validator = new();

    /// <inheritdoc />
    public override async ValueTask<Result<AnonymizationResult>> InvokeAsync(
        AnonymizerContext context,
        AnonymizerDelegate nextMiddleware,
        CancellationToken cancellationToken)
    {
        if (context.Settings.ValidateInput)
        {
            logger.LogDebug("Validating input resource");

            try
            {
                var json = context.Resource.MutableNode.ToJsonString();
                _validator.ValidateInput(json);
            }
            catch (ResourceNotValidException ex)
            {
                logger.LogWarning(ex, "Input validation failed");
                return Result<AnonymizationResult>.Failure(new AnonymizerError(
                    "VALIDATION_FAILED",
                    $"Input validation failed: {ex.Message}",
                    ErrorSeverity.Error,
                    ex));
            }
        }

        return await nextMiddleware(context, cancellationToken).ConfigureAwait(false);
    }
}
