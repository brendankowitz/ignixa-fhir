// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure.Behaviors;

/// <summary>
/// Medino pipeline behavior that validates FHIR resources before CREATE/UPDATE operations.
/// Runs AFTER CapabilityEnforcementBehavior to ensure validation only occurs for permitted operations.
/// Uses tenant-configured validation tier (None/Fast/Spec/Profile) and FHIR version from HTTP headers.
/// </summary>
public class ValidationBehavior : IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>
{
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly Func<FhirSpecification, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<ValidationBehavior> _logger;

    public ValidationBehavior(
        IFhirRequestContextAccessor contextAccessor,
        Func<FhirSpecification, IValidationSchemaResolver> schemaResolverFactory,
        ITerminologyService terminologyService,
        ILogger<ValidationBehavior> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _schemaResolverFactory = schemaResolverFactory ?? throw new ArgumentNullException(nameof(schemaResolverFactory));
        _terminologyService = terminologyService ?? throw new ArgumentNullException(nameof(terminologyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResourceKey> HandleAsync(
        CreateOrUpdateResourceCommand request,
        RequestHandlerDelegate<ResourceKey> next,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        // Use FHIR version from context
        var fhirVersionEnum = context.FhirVersion;

        // Get tenant configuration from context to determine validation tier
        var currentTenantConfig = context.TenantConfiguration;

        // Determine validation tier: Prefer header override takes precedence, then tenant config, then default to Spec
        var validationTier = request.ValidationTierOverride
            ?? ParseValidationTier(currentTenantConfig?.ValidationTier ?? "Spec");

        // Log if header overrode tenant config
        if (request.ValidationTierOverride.HasValue && currentTenantConfig != null)
        {
            var tenantTier = ParseValidationTier(currentTenantConfig.ValidationTier);
            if (request.ValidationTierOverride.Value != tenantTier)
            {
                _logger.LogInformation(
                    "Validation tier overridden by Prefer header: {HeaderTier} (tenant default: {TenantTier})",
                    request.ValidationTierOverride.Value,
                    tenantTier);
            }
        }

        // VALIDATE INCOMING RESOURCE using tier-aware ValidationSchema
        if (validationTier != ValidationTier.None)
        {
            _logger.LogDebug(
                "Validating incoming resource {ResourceType}/{Id} with tier {Tier} (FHIR {Version})",
                request.ResourceType,
                request.Id,
                validationTier,
                fhirVersionEnum);

            // Get version-specific schema resolver from factory
            var schemaResolver = _schemaResolverFactory(fhirVersionEnum);
            var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{request.ResourceType}";
            var schema = schemaResolver.GetSchema(canonicalUrl);

            if (schema != null)
            {
                var sourceNode = request.JsonNode.ToSourceNode(); // Use cached ISourceNode
                var settings = new ValidationSettings
                {
                    Tier = validationTier,
                    TerminologyService = _terminologyService
                };
                var state = new ValidationState();
                var validationResult = schema.Validate(sourceNode, settings, state);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "Validation failed for {ResourceType}/{Id}: {ErrorCount} error(s), {WarningCount} warning(s)",
                        request.ResourceType,
                        request.Id,
                        validationResult.Issues.Count(i => i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Fatal),
                        validationResult.Issues.Count(i => i.Severity == IssueSeverity.Warning));

                    // Throw ValidationException which will be caught by FhirExceptionMiddleware
                    // and converted to HTTP 400 with OperationOutcome
                    throw new ValidationException(validationResult);
                }

                _logger.LogDebug(
                    "Validation passed for {ResourceType}/{Id} (FHIR {Version})",
                    request.ResourceType,
                    request.Id,
                    fhirVersionEnum);
            }
            else
            {
                _logger.LogWarning(
                    "No validation schema found for {ResourceType} (canonical URL: {CanonicalUrl})",
                    request.ResourceType,
                    canonicalUrl);
            }
        }
        else
        {
            _logger.LogDebug(
                "Validation skipped for {ResourceType}/{Id} (tier: None)",
                request.ResourceType,
                request.Id);
        }

        // Validation passed or skipped - continue to handler
        return await next();
    }

    /// <summary>
    /// Parses a validation tier string from tenant configuration.
    /// </summary>
    /// <param name="tierString">The validation tier string (None, Fast, Spec, Profile).</param>
    /// <returns>The parsed ValidationTier enum value.</returns>
    private static ValidationTier ParseValidationTier(string tierString)
    {
        return tierString switch
        {
            "None" => ValidationTier.None,
            "Fast" => ValidationTier.Fast,
            "Spec" => ValidationTier.Spec,
            "Profile" => ValidationTier.Profile,
            _ => ValidationTier.Spec // Default to Spec if unknown
        };
    }
}
