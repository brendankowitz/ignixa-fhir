// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Terminology;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// Hybrid terminology service that routes between SQL (fast) and JSON fallback implementations.
/// Uses SQL terminology tables when available (imported), falls back to JSON parsing otherwise.
/// <para>
/// Pure routing: it holds no storage of its own, which is why the routing decision arrives as
/// <see cref="ITerminologyImportStatusProvider"/> rather than a concrete SQL service. Both terminology
/// dependencies are <see cref="ITerminologyService"/>, so which implementation sits on either side is a
/// composition-root choice.
/// </para>
/// </summary>
public class HybridTerminologyService(
    ITerminologyService sqlService,
    ITerminologyImportStatusProvider importStatusProvider,
    ITerminologyService fallbackService,
    ILogger<HybridTerminologyService> logger) : ITerminologyService, ITerminologyImportStatusProvider
{
    /// <summary>
    /// $lookup operation - Routes to SQL if CodeSystem is imported, otherwise uses fallback.
    /// </summary>
    public async Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken)
    {
        var status = await importStatusProvider.GetImportStatusAsync(system, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            logger.LogDebug("Using SQL service for lookup: {System}|{Code} (imported)", system, code);
            return await sqlService.LookupCodeAsync(system, code, version, cancellationToken);
        }
        else
        {
            logger.LogDebug("Using fallback service for lookup: {System}|{Code} (not imported)", system, code);
            return await fallbackService.LookupCodeAsync(system, code, version, cancellationToken);
        }
    }

    /// <summary>
    /// $expand operation - Routes to SQL if ValueSet is imported, otherwise uses fallback.
    /// </summary>
    public async Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var status = await importStatusProvider.GetImportStatusAsync(parameters.Url, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            logger.LogDebug("Using SQL service for expand: {Url} (imported)", parameters.Url);
            return await sqlService.ExpandValueSetAsync(parameters, cancellationToken);
        }
        else
        {
            logger.LogDebug("Using fallback service for expand: {Url} (not imported)", parameters.Url);
            return await fallbackService.ExpandValueSetAsync(parameters, cancellationToken);
        }
    }

    /// <summary>
    /// $validate-code operation - Routes to SQL if ValueSet is imported, otherwise uses fallback.
    /// </summary>
    public async Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: "ValueSet URL is required");
        }

        var status = await importStatusProvider.GetImportStatusAsync(valueSetUrl, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            logger.LogDebug("Using SQL service for validate: {ValueSet}|{Code} (imported)", valueSetUrl, code);
            return await sqlService.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
        }
        else
        {
            logger.LogDebug("Using fallback service for validate: {ValueSet}|{Code} (not imported)", valueSetUrl, code);
            return await fallbackService.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
        }
    }

    /// <summary>
    /// Validates a coded element against a terminology binding.
    /// Routes to SQL service if ValueSet is imported, otherwise uses fallback service.
    /// </summary>
    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        var status = await importStatusProvider.GetImportStatusAsync(valueSetUrl, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            logger.LogDebug(
                "Using SQL service for binding validation: {ValueSet}|{Code} (imported)",
                valueSetUrl,
                code);
            return await sqlService.ValidateBindingAsync(
                valueSetUrl,
                strength,
                system,
                code,
                display,
                version,
                cancellationToken);
        }
        else
        {
            logger.LogDebug(
                "Using fallback service for binding validation: {ValueSet}|{Code} (not imported)",
                valueSetUrl,
                code);
            return await fallbackService.ValidateBindingAsync(
                valueSetUrl,
                strength,
                system,
                code,
                display,
                version,
                cancellationToken);
        }
    }

    /// <summary>
    /// $translate operation - Routes to SQL service (no fallback for translation).
    /// </summary>
    public async Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Translation always uses SQL service (no in-memory ConceptMaps)
        logger.LogDebug("Using SQL service for translation: {System}|{Code}", parameters.System, parameters.Code);
        return await sqlService.TranslateCodeAsync(parameters, cancellationToken);
    }

    /// <summary>
    /// $subsumes operation - Routes to SQL service (no fallback for subsumption).
    /// </summary>
    public async Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Subsumption always uses SQL service (requires hierarchy data)
        logger.LogDebug(
            "Using SQL service for subsumption: {System}|{CodeA} vs {CodeB}",
            parameters.System,
            parameters.CodeA,
            parameters.CodeB);
        return await sqlService.SubsumesAsync(parameters, cancellationToken);
    }

    /// <summary>
    /// Get import status - Delegates to the import status provider.
    /// </summary>
    public async Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical,
        CancellationToken cancellationToken)
    {
        return await importStatusProvider.GetImportStatusAsync(canonical, cancellationToken);
    }
}
