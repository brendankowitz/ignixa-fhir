// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Exceptions;
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
/// Uses tenant-configured validation depth (Minimal/Spec/Full) and FHIR version from HTTP headers.
/// </summary>
public class ValidationBehavior : IPipelineBehavior<CreateOrUpdateResourceCommand, UpdateResult>
{
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly Func<FhirVersion, int, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<ValidationBehavior> _logger;

    public ValidationBehavior(
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext fhirVersionContext,
        Func<FhirVersion, int, IValidationSchemaResolver> schemaResolverFactory,
        ITerminologyService terminologyService,
        ILogger<ValidationBehavior> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _schemaResolverFactory = schemaResolverFactory ?? throw new ArgumentNullException(nameof(schemaResolverFactory));
        _terminologyService = terminologyService ?? throw new ArgumentNullException(nameof(terminologyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateResult> HandleAsync(
        CreateOrUpdateResourceCommand request,
        RequestHandlerDelegate<UpdateResult> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The handler assigns this ID after validation; checking only the body would miss it.
        if (request.Id.Length is < 1 or > 64 ||
            request.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '.'))
        {
            throw new ValidationException(ValidationResult.Failure(
                ValidationIssue.InvariantFailure(
                    "id-1",
                    "Resource ID must contain 1–64 ASCII letters, digits, hyphens, or periods.",
                    $"{request.ResourceType}.id")));
        }

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

        // ValidationSchema owns tier selection; Minimal must still execute universal checks.
        _logger.LogDebug(
            "Validating incoming resource {ResourceType}/{Id} with depth {Depth} (FHIR {Version})",
            request.ResourceType,
            request.Id,
            validationDepth,
            fhirVersionEnum);

        int tenantId = context.TenantId;
        var schemaResolver = _schemaResolverFactory(fhirVersionEnum, tenantId);
        var schemaProvider = _fhirVersionContext.GetSchemaProvider(fhirVersionEnum, tenantId);
        var element = request.JsonNode.ToElement(schemaProvider);

        var canonicalUrl = schemaProvider is Ignixa.Application.Features.Specification.CompositeStructureDefinitionSummaryProvider composite
            ? composite.GetResourceCanonical(request.ResourceType)
                ?? $"http://hl7.org/fhir/StructureDefinition/{request.ResourceType}"
            : $"http://hl7.org/fhir/StructureDefinition/{request.ResourceType}";
        var schema = schemaResolver.GetSchema(canonicalUrl);
        if (schema == null)
        {
            _logger.LogError("Base validation schema {CanonicalUrl} is unavailable for tenant {TenantId}", canonicalUrl, tenantId);
            throw new InternalServerErrorException($"Base validation schema is unavailable for {request.ResourceType}.");
        }

        // Prefer element-aware resolution for meta.profile composition; test doubles and legacy
        // resolvers may expose only canonical-URL lookup.
        if (schemaResolver is IElementSchemaResolver elementResolver)
        {
            schema = elementResolver.ResolveForElement(element) ?? schema;
        }

        var settings = new ValidationSettings
        {
            Depth = validationDepth,
            TerminologyService = _terminologyService
        };
        var validationResult = schema.Validate(element, settings);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Validation failed for {ResourceType}/{Id}: {ErrorCount} error(s), {WarningCount} warning(s)",
                request.ResourceType,
                request.Id,
                validationResult.Issues.Count(i => i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Fatal),
                validationResult.Issues.Count(i => i.Severity == IssueSeverity.Warning));

            throw new ValidationException(validationResult);
        }

        foreach (var issue in validationResult.Issues.Where(issue => issue.Severity == IssueSeverity.Warning))
        {
            _logger.LogWarning(
                "Validation warning for {ResourceType}/{Id} at {Path}: {Message}",
                request.ResourceType, request.Id, issue.Path, issue.Message);
        }

        _logger.LogDebug(
            "Validation passed for {ResourceType}/{Id} (FHIR {Version})",
            request.ResourceType, request.Id, fhirVersionEnum);

        cancellationToken.ThrowIfCancellationRequested();
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
