// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure.Behaviors;

/// <summary>
/// Medino pipeline behavior that validates FHIR resources before CREATE/UPDATE operations.
/// Runs AFTER CapabilityEnforcementBehavior to ensure validation only occurs for permitted operations.
/// Uses tenant-configured validation depth (Minimal/Spec/Full) and FHIR version from HTTP headers.
/// </summary>
public class ValidationBehavior : IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>
{
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly Func<FhirVersion, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<ValidationBehavior> _logger;

    public ValidationBehavior(
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext fhirVersionContext,
        Func<FhirVersion, IValidationSchemaResolver> schemaResolverFactory,
        ITerminologyService terminologyService,
        ILogger<ValidationBehavior> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
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

        // Get tenant configuration from context to determine validation depth
        var currentTenantConfig = context.TenantConfiguration;

        // Determine validation depth: Prefer header override takes precedence, then tenant config, then default to Spec
        var validationDepth = request.ValidationDepthOverride
            ?? ParseValidationDepth(currentTenantConfig?.ValidationDepth ?? "Spec");

        // Log if header overrode tenant config
        if (request.ValidationDepthOverride.HasValue && currentTenantConfig != null)
        {
            var tenantDepth = ParseValidationDepth(currentTenantConfig.ValidationDepth);
            if (request.ValidationDepthOverride.Value != tenantDepth)
            {
                _logger.LogInformation(
                    "Validation depth overridden by Prefer header: {HeaderDepth} (tenant default: {TenantDepth})",
                    request.ValidationDepthOverride.Value,
                    tenantDepth);
            }
        }

        // VALIDATE INCOMING RESOURCE using depth-aware ValidationSchema
        if (validationDepth != ValidationDepth.Minimal || validationDepth == ValidationDepth.Spec || validationDepth == ValidationDepth.Full)
        {
            _logger.LogDebug(
                "Validating incoming resource {ResourceType}/{Id} with depth {Depth} (FHIR {Version})",
                request.ResourceType,
                request.Id,
                validationDepth,
                fhirVersionEnum);

            // Get version-specific schema resolver from factory
            var schemaResolver = _schemaResolverFactory(fhirVersionEnum);

            // Build element first - need it both for ProfileAware resolution and for validation
            var schemaProvider = _fhirVersionContext.GetBaseSchemaProvider(fhirVersionEnum);
            var element = request.JsonNode.ToElement(schemaProvider);

            // Prefer element-aware resolution (composes meta.profile checks). The DI factory
            // returns a ProfileAwareValidationSchemaResolver wrapping the inner cached
            // resolver, but consumers see only IValidationSchemaResolver - downcast to
            // pick up the richer API. Falls back to canonical-URL lookup if downcast fails
            // (e.g. test doubles that don't use the production wrapping).
            ValidationSchema? schema = null;
            var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{request.ResourceType}";
            if (schemaResolver is ProfileAwareValidationSchemaResolver profileAware)
            {
                schema = profileAware.ResolveForElement(element);
            }
            schema ??= schemaResolver.GetSchema(canonicalUrl);

            if (schema != null)
            {
                var settings = new ValidationSettings
                {
                    Depth = validationDepth,
                    TerminologyService = _terminologyService
                };
                var state = new ValidationState();
                var validationResult = schema.Validate(element, settings, state);

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
                "Validation running with minimal depth for {ResourceType}/{Id}",
                request.ResourceType,
                request.Id);
        }

        // Validation passed or skipped - continue to handler
        return await next();
    }

    /// <summary>
    /// Parses a validation depth string from tenant configuration.
    /// </summary>
    /// <param name="depthString">The validation depth string (Minimal, Spec, Full, or legacy None/Fast/Profile).</param>
    /// <returns>The parsed ValidationDepth enum value.</returns>
    private static ValidationDepth ParseValidationDepth(string depthString)
    {
        return depthString switch
        {
            "Minimal" => ValidationDepth.Minimal,
            "Spec" => ValidationDepth.Spec,
            "Full" => ValidationDepth.Full,
            // Backward compatibility with old names
            "None" => ValidationDepth.Minimal,
            "Fast" => ValidationDepth.Minimal,
            "Profile" => ValidationDepth.Full,
            _ => ValidationDepth.Spec // Default to Spec if unknown
        };
    }
}
