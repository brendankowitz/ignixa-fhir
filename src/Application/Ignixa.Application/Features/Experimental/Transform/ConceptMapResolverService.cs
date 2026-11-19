// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirPath;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Transform;

/// <summary>
/// Service that resolves ConceptMap translations for the translate() FML function.
/// Delegates to ITerminologyService for actual translation logic.
///
/// Flow:
/// 1. Build TranslateParameters from FML function arguments
/// 2. Call ITerminologyService.TranslateCodeAsync()
/// 3. Extract first matching target code from TranslateResult
/// 4. Return target code if found, null otherwise
/// </summary>
public class ConceptMapResolverService(
    ITerminologyService terminologyService,
    ILogger<ConceptMapResolverService> logger)
{
    /// <summary>
    /// Translates a source code to a target code using a ConceptMap.
    /// </summary>
    /// <param name="sourceCode">The source code to translate.</param>
    /// <param name="sourceSystem">The source code system URL.</param>
    /// <param name="mapUrl">Canonical URL of the ConceptMap to use.</param>
    /// <param name="targetSystem">Optional: Target system to filter by (if null, returns first matching target).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The target code if translation found, null otherwise.</returns>
    /// <exception cref="MappingExecutionException">The translation could not be performed.</exception>
    public async Task<string?> TranslateAsync(
        string sourceCode,
        string sourceSystem,
        string mapUrl,
        string? targetSystem,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapUrl);

        // Codes and systems are resource data and are not logged; the map URL is the request's, so it is sanitized.
        logger.LogDebug("Translating a code using ConceptMap '{MapUrl}'", mapUrl.SanitizeForLog());

        try
        {
            var parameters = new TranslateParameters(
                Url: mapUrl,
                ConceptMapVersion: null,
                Code: sourceCode,
                System: sourceSystem,
                Version: null,
                Source: null,
                Target: null,
                TargetSystem: targetSystem,
                Reverse: false);

            var result = await terminologyService.TranslateCodeAsync(parameters, cancellationToken);

            if (!result.Result || result.Matches.Count == 0)
            {
                logger.LogDebug("No translation found in ConceptMap '{MapUrl}'", mapUrl.SanitizeForLog());
                return null;
            }

            var firstMatch = result.Matches[0];
            logger.LogDebug(
                "Translated a code using ConceptMap '{MapUrl}' (equivalence: {Equivalence})",
                mapUrl.SanitizeForLog(),
                firstMatch.Equivalence);

            return firstMatch.Concept.Code;
        }
        catch (Exception ex) when (!ExceptionFilters.IsCritical(ex))
        {
            // Cancellation, the transform timeout, resource limits and memory or stack exhaustion propagate as
            // themselves; anything else fails the translation, and with it the mapping, rather than leaving the
            // code untranslated.
            logger.LogWarning(
                "ConceptMap translation failed using map '{MapUrl}' ({ExceptionType})",
                mapUrl.SanitizeForLog(),
                ex.GetType().Name);

            throw new MappingExecutionException(
                $"ConceptMap translation failed using map '{LogSafeText.Sanitize(mapUrl)}': {LogSafeText.Sanitize(ex.Message)}",
                code: "TRANSLATE_FAILED",
                innerException: ex);
        }
    }

    /// <summary>
    /// Synchronous wrapper for <see cref="TranslateAsync"/>, for the FML <c>translate()</c> transform, which
    /// <see cref="MappingEvaluator"/> calls synchronously.
    /// </summary>
    /// <param name="sourceCode">The source code to translate.</param>
    /// <param name="sourceSystem">The source code system URL.</param>
    /// <param name="mapUrl">Canonical URL of the ConceptMap to use.</param>
    /// <param name="targetSystem">Optional: Target system to filter by.</param>
    /// <param name="cancellationToken">The mapping's token: the request's, linked with the transform timeout.</param>
    /// <returns>The target code, or <see langword="null"/> when the map has no translation for it.</returns>
    /// <exception cref="MappingExecutionException">The translation could not be performed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public string? Translate(
        string sourceCode,
        string sourceSystem,
        string mapUrl,
        string? targetSystem,
        CancellationToken cancellationToken)
    {
        // Blocking is required here: MappingEvaluator is synchronous and cannot await.
        return TranslateAsync(sourceCode, sourceSystem, mapUrl, targetSystem, cancellationToken)
            .GetAwaiter()
            .GetResult();
    }
}
