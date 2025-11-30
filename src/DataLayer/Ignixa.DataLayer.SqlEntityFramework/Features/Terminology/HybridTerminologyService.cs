// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Terminology;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;

/// <summary>
/// Hybrid terminology service that routes between SQL (fast) and JSON fallback implementations.
/// Uses SQL terminology tables when available (imported), falls back to JSON parsing otherwise.
/// </summary>
public class HybridTerminologyService : ITerminologyService
{
    private readonly SqlTerminologyService _sqlService;
    private readonly ITerminologyService _fallbackService;
    private readonly ILogger<HybridTerminologyService> _logger;

    public HybridTerminologyService(
        SqlTerminologyService sqlService,
        ITerminologyService fallbackService,
        ILogger<HybridTerminologyService> logger)
    {
        _sqlService = sqlService ?? throw new ArgumentNullException(nameof(sqlService));
        _fallbackService = fallbackService ?? throw new ArgumentNullException(nameof(fallbackService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// $lookup operation - Routes to SQL if CodeSystem is imported, otherwise uses fallback.
    /// </summary>
    public async Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken)
    {
        // Check if CodeSystem is imported
        var status = await _sqlService.GetImportStatusAsync(system, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            _logger.LogDebug("Using SQL service for lookup: {System}|{Code} (imported)", system, code);
            return await _sqlService.LookupCodeAsync(system, code, version, cancellationToken);
        }
        else
        {
            _logger.LogDebug("Using fallback service for lookup: {System}|{Code} (not imported)", system, code);
            return await _fallbackService.LookupCodeAsync(system, code, version, cancellationToken);
        }
    }

    /// <summary>
    /// $expand operation - Routes to SQL if ValueSet is imported, otherwise uses fallback.
    /// </summary>
    public async Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken)
    {
        // Check if ValueSet is imported
        var status = await _sqlService.GetImportStatusAsync(parameters.Url, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            _logger.LogDebug("Using SQL service for expand: {Url} (imported)", parameters.Url);
            return await _sqlService.ExpandValueSetAsync(parameters, cancellationToken);
        }
        else
        {
            _logger.LogDebug("Using fallback service for expand: {Url} (not imported)", parameters.Url);
            return await _fallbackService.ExpandValueSetAsync(parameters, cancellationToken);
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

        // Check if ValueSet is imported
        var status = await _sqlService.GetImportStatusAsync(valueSetUrl, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            _logger.LogDebug("Using SQL service for validate: {ValueSet}|{Code} (imported)", valueSetUrl, code);
            return await _sqlService.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
        }
        else
        {
            _logger.LogDebug("Using fallback service for validate: {ValueSet}|{Code} (not imported)", valueSetUrl, code);
            return await _fallbackService.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
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
        // Check if ValueSet is imported
        var status = await _sqlService.GetImportStatusAsync(valueSetUrl, cancellationToken);

        if (status == TerminologyImportStatus.Completed)
        {
            _logger.LogDebug(
                "Using SQL service for binding validation: {ValueSet}|{Code} (imported)",
                valueSetUrl,
                code);
            return await _sqlService.ValidateBindingAsync(
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
            _logger.LogDebug(
                "Using fallback service for binding validation: {ValueSet}|{Code} (not imported)",
                valueSetUrl,
                code);
            return await _fallbackService.ValidateBindingAsync(
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
        // Translation always uses SQL service (no in-memory ConceptMaps)
        _logger.LogDebug("Using SQL service for translation: {System}|{Code}", parameters.System, parameters.Code);
        return await _sqlService.TranslateCodeAsync(parameters, cancellationToken);
    }

    /// <summary>
    /// $subsumes operation - Routes to SQL service (no fallback for subsumption).
    /// </summary>
    public async Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken)
    {
        // Subsumption always uses SQL service (requires hierarchy data)
        _logger.LogDebug("Using SQL service for subsumption: {System}|{CodeA} vs {CodeB}",
            parameters.System, parameters.CodeA, parameters.CodeB);
        return await _sqlService.SubsumesAsync(parameters, cancellationToken);
    }

    /// <summary>
    /// Get import status - Delegates to SQL service.
    /// </summary>
    public async Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical,
        CancellationToken cancellationToken)
    {
        return await _sqlService.GetImportStatusAsync(canonical, cancellationToken);
    }
}
