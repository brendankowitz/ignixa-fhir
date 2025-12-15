// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure.Behaviors;

/// <summary>
/// Medino pipeline behavior that enforces capability statement compliance for all FHIR requests.
/// Validates requests by evaluating FHIRPath expressions declared by commands/queries against
/// the server's CapabilityStatement.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Architecture</strong>: Commands/queries implement <see cref="IRequireCapability"/>
/// interface and provide a FHIRPath expression describing their capability requirement.
/// </para>
/// <para>
/// <strong>Example</strong>:
/// <code>
/// public record GetResourceQuery(...) : IRequest&lt;...&gt;, IRequiresCapability
/// {
///     public string GetCapabilityRequirementExpression() =>
///         $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'read').exists()";
/// }
/// </code>
/// </para>
/// <para>
/// <strong>Benefits</strong>:
/// - Self-documenting: Each command declares its own requirement
/// - Extensible: New commands just implement interface - no behavior changes needed
/// - Flexible: FHIRPath expressions can be complex
/// - Type-safe: No brittle pattern matching
/// </para>
/// <para>
/// Phase 3: Validates resource types and CRUD interactions.
/// Phase 12: Will also validate custom search parameters.
/// Runs for ALL Medino requests, including bundle sub-requests.
/// </para>
/// </remarks>
public class CapabilityEnforcementBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly CapabilityStatementService _capabilityService;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<CapabilityEnforcementBehavior<TRequest, TResponse>> _logger;

    public CapabilityEnforcementBehavior(
        CapabilityStatementService capabilityService,
        ITenantConfigurationStore tenantConfigStore,
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext versionContext,
        ILogger<CapabilityEnforcementBehavior<TRequest, TResponse>> logger)
    {
        _capabilityService = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Only check if request declares capability requirements
        if (request is not IRequireCapability capabilityRequest)
        {
            // Not a FHIR resource operation (e.g., GetCapabilityStatementQuery)
            return await next();
        }

        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext;
        var tenantId = context?.TenantId;

        // Get FHIR version from context (or fallback to tenant lookup)
        var fhirVersion = context?.FhirVersion ?? await GetFhirVersionForTenantAsync(tenantId, cancellationToken);

        // Get CapabilityStatement
        var capabilityContext = new CapabilityContext(FhirVersion: fhirVersion, TenantId: tenantId);
        var capabilityStatement = await _capabilityService.GetCapabilityStatementAsync(capabilityContext, cancellationToken);

        // Get schema provider for FHIRPath evaluation
        var provider = _versionContext.GetBaseSchemaProvider(fhirVersion);

        // Convert CapabilityStatement to IElement for FHIRPath queries
        var typedElement = capabilityStatement.ToElement(provider);

        // Get FHIRPath expression from request
        var expression = capabilityRequest.GetCapabilityRequirementExpression();

        // Evaluate FHIRPath expression against CapabilityStatement
        var result = ((Ignixa.Abstractions.IElement)typedElement).Select(expression).FirstOrDefault();

        // Check if capability is supported (expression should return boolean true)
        var isSupported = result?.Value is bool supported && supported;

        if (!isSupported)
        {
            _logger.LogWarning(
                "Capability enforcement: Request rejected - FHIRPath expression '{Expression}' evaluated to false (TenantId: {TenantId})",
                expression,
                tenantId);

            // Throw exception - will be caught by FhirExceptionMiddleware
            // and converted to 403 Forbidden with OperationOutcome
            throw new InvalidOperationException(
                $"Server does not support this operation. Check GET /metadata for supported capabilities.");
        }

        // Capability requirement satisfied - continue pipeline
        return await next();
    }

    /// <summary>
    /// Gets the FHIR version for a tenant.
    /// </summary>
    private async Task<FhirVersion> GetFhirVersionForTenantAsync(int? tenantId, CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            // System-wide request - use first tenant's version or default to R4
            var tenants = await _tenantConfigStore.GetAllTenantsAsync(cancellationToken);
            if (tenants.Count > 0)
            {
                return FhirSpecificationExtensions.FromVersionString(tenants[0].FhirVersion);
            }

            return FhirVersion.R4;
        }

        var tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(tenantId.Value, cancellationToken);
        if (tenantConfig == null)
        {
            return FhirVersion.R4;
        }

        return FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
    }
}
