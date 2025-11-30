// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.Transform;

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

        logger.LogDebug(
            "Translating code '{SourceSystem}#{SourceCode}' using ConceptMap '{MapUrl}' (targetSystem: {TargetSystem})",
            sourceSystem,
            sourceCode,
            mapUrl,
            targetSystem ?? "any");

        try
        {
            // Build TranslateParameters for ITerminologyService
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

            // Delegate to ITerminologyService for translation
            var result = await terminologyService.TranslateCodeAsync(parameters, cancellationToken);

            if (!result.Result || result.Matches.Count == 0)
            {
                logger.LogDebug(
                    "No translation found for code '{SourceSystem}#{SourceCode}' in ConceptMap '{MapUrl}' (targetSystem: {TargetSystem})",
                    sourceSystem,
                    sourceCode,
                    mapUrl,
                    targetSystem ?? "any");

                return null;
            }

            // Return first matching target code
            var firstMatch = result.Matches[0];

            logger.LogInformation(
                "Translated '{SourceSystem}#{SourceCode}' → '{TargetSystem}#{TargetCode}' using ConceptMap '{MapUrl}' (equivalence: {Equivalence})",
                sourceSystem,
                sourceCode,
                firstMatch.Concept.System,
                firstMatch.Concept.Code,
                mapUrl,
                firstMatch.Equivalence);

            return firstMatch.Concept.Code;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to translate code '{SourceSystem}#{SourceCode}' using ConceptMap '{MapUrl}': {Message}",
                sourceSystem,
                sourceCode,
                mapUrl,
                ex.Message);

            // Rethrow to let caller handle (will be caught by MappingEvaluator)
            throw new InvalidOperationException(
                $"ConceptMap translation failed for '{sourceSystem}#{sourceCode}' using map '{mapUrl}': {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Synchronous wrapper for TranslateAsync (used by FML translate() transform).
    /// Uses blocking wait pattern (required since MappingEvaluator is synchronous).
    /// </summary>
    /// <param name="sourceCode">The source code to translate.</param>
    /// <param name="sourceSystem">The source code system URL.</param>
    /// <param name="mapUrl">Canonical URL of the ConceptMap to use.</param>
    /// <param name="targetSystem">Optional: Target system to filter by.</param>
    /// <returns>The target code if translation found, null otherwise.</returns>
    public string? Translate(
        string sourceCode,
        string sourceSystem,
        string mapUrl,
        string? targetSystem)
    {
        try
        {
            // Use blocking wait (MappingEvaluator is synchronous, cannot await)
            return TranslateAsync(sourceCode, sourceSystem, mapUrl, targetSystem, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Synchronous translate failed for code '{SourceSystem}#{SourceCode}' using ConceptMap '{MapUrl}': {Message}",
                sourceSystem,
                sourceCode,
                mapUrl,
                ex.Message);

            // Return null to let mapping execution handle gracefully
            return null;
        }
    }
}
