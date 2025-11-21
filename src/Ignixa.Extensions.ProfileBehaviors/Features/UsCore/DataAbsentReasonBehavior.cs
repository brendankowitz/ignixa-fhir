// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Extensions.ProfileBehaviors.Abstractions;
using Ignixa.Extensions.ProfileBehaviors.Infrastructure;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.Extensions.ProfileBehaviors.Features.UsCore;

/// <summary>
/// Medino pipeline behavior that automatically injects data-absent-reason extensions
/// for missing mandatory elements in US Core resources.
/// </summary>
/// <remarks>
/// <para>
/// <strong>US Core Requirement</strong>:
/// "For mandatory elements with missing data and unknown reason, include the element
/// with a data-absent-reason extension using code 'unknown'."
/// </para>
/// <para>
/// <strong>Pipeline Position</strong>:
/// Runs BEFORE ValidationBehavior to ensure injected elements pass CardinalityCheck.
/// Order: CapabilityEnforcement → DataAbsentReason → Validation → Handler
/// </para>
/// <para>
/// <strong>Activation</strong>:
/// Only activates when US Core package is loaded for the tenant (detected via ProfileDetectionService).
/// </para>
/// </remarks>
/// <typeparam name="TRequest">Request type (must implement IResourceCommand for resource access).</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class DataAbsentReasonBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IResourceCommand
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IProfileDetectionService _profileDetection;
    private readonly IFhirVersionContext _versionContext;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ILogger<DataAbsentReasonBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataAbsentReasonBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    /// <param name="profileDetection">Profile detection service.</param>
    /// <param name="versionContext">FHIR version context for schema providers.</param>
    /// <param name="tenantConfigStore">Tenant configuration store for retrieving default FHIR version.</param>
    /// <param name="logger">Logger instance.</param>
    public DataAbsentReasonBehavior(
        IHttpContextAccessor httpContextAccessor,
        IProfileDetectionService profileDetection,
        IFhirVersionContext versionContext,
        ITenantConfigurationStore tenantConfigStore,
        ILogger<DataAbsentReasonBehavior<TRequest, TResponse>> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _profileDetection = profileDetection ?? throw new ArgumentNullException(nameof(profileDetection));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Get tenant context from HttpContext
        var httpContext = _httpContextAccessor.HttpContext;
        var tenantId = httpContext?.Items["TenantId"] as int?;

        if (!tenantId.HasValue)
        {
            // No tenant context - skip
            return await next();
        }

        // Check if US Core is active for this tenant
        if (!await _profileDetection.IsUSCoreActiveAsync(tenantId.Value, cancellationToken))
        {
            _logger.LogTrace(
                "US Core not active for tenant {TenantId} - skipping data-absent-reason injection",
                tenantId.Value);
            return await next();
        }

        // Get FHIR version from HTTP headers, falling back to tenant's default configuration
        var fhirVersion = await FhirVersionExtractor.ExtractFhirVersionAsync(
            httpContext,
            _tenantConfigStore,
            tenantId.Value,
            cancellationToken);

        // Get schema provider for this FHIR version (base provider with element definitions)
        var schemaProvider = _versionContext.GetBaseSchemaProvider(fhirVersion);

        // Visit the resource JsonNode and inject data-absent-reason for missing mandatory elements
        try
        {
            var propertyVisitor = new DataAbsentReasonVisitor();
            var visitor = new ExtensibleJsonNodeVisitor(schemaProvider, propertyVisitor);

            visitor.Visit(
                request.JsonNode.MutableNode,
                request.ResourceType,
                fhirVersion,
                maxDepth: 0); // Only inject at root level

            _logger.LogDebug(
                "Processed {ResourceType} for US Core data-absent-reason injection (tenant {TenantId})",
                request.ResourceType,
                tenantId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to inject data-absent-reason for {ResourceType} - continuing to validation",
                request.ResourceType);
            // Continue to next handler - validation will catch any issues
        }

        // Continue to next handler (ValidationBehavior should now pass)
        return await next();
    }
}
