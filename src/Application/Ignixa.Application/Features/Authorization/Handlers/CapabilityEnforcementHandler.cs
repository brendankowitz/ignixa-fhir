// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.FhirPath.Evaluation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Authorization handler that enforces CapabilityStatement compliance.
/// Validates that requested interactions are actually supported by the server.
/// Uses cached interaction lookup for O(1) performance.
/// Priority: 50 (final handler in pipeline).
/// </summary>
public class CapabilityEnforcementHandler : IAuthorizationHandler
{
    private readonly CapabilityStatementService _capabilityService;
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<CapabilityEnforcementHandler> _logger;

    // Per-tenant interaction caches for O(1) lookups
    private readonly ConcurrentDictionary<string, Services.CapabilityInteractionCache> _interactionCaches = new();

    public CapabilityEnforcementHandler(
        CapabilityStatementService capabilityService,
        IFhirVersionContext versionContext,
        ILogger<CapabilityEnforcementHandler> logger)
    {
        _capabilityService = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 50;

    /// <inheritdoc />
    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        // Always allow /metadata
        if (context.Interaction == FhirInteraction.Capabilities)
        {
            _logger.LogDebug("Capability enforcement: Allowing /metadata request");
            return AuthorizationResult.Success();
        }

        var interactionCode = context.Interaction.ToFhirCode();
        var resourceType = context.ResourceType;

        // Get or build interaction cache for this context
        var cacheKey = BuildCacheKey(context);
        var cache = await GetOrBuildCacheAsync(cacheKey, context, cancellationToken);

        // O(1) lookup
        var isSupported = cache.IsSupported(resourceType, interactionCode);

        if (!isSupported)
        {
            _logger.LogWarning(
                "Capability enforcement: Request denied - {Interaction} on {ResourceType} not supported",
                interactionCode,
                resourceType ?? "system");

            return AuthorizationResult.CapabilityNotSupported(resourceType, interactionCode);
        }

        _logger.LogDebug(
            "Capability enforcement: {Interaction} on {ResourceType} is supported",
            interactionCode,
            resourceType ?? "system");

        return AuthorizationResult.Success();
    }

    /// <summary>
    /// Gets or builds the interaction cache for a capability context.
    /// </summary>
    private async ValueTask<Services.CapabilityInteractionCache> GetOrBuildCacheAsync(
        string cacheKey,
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        if (_interactionCaches.TryGetValue(cacheKey, out var existingCache))
        {
            return existingCache;
        }

        // Build cache from CapabilityStatement
        var cache = await BuildCacheFromCapabilityStatementAsync(context, cancellationToken);
        _interactionCaches[cacheKey] = cache;

        _logger.LogInformation(
            "Built capability interaction cache for {CacheKey} with {Count} interactions",
            cacheKey,
            cache.Count);

        return cache;
    }

    /// <summary>
    /// Builds interaction cache from CapabilityStatement using FHIRPath.
    /// </summary>
    private async ValueTask<Services.CapabilityInteractionCache> BuildCacheFromCapabilityStatementAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        var cache = new Services.CapabilityInteractionCache();

        // Get FHIR version - default to R4 for now
        // TODO: Look up tenant configuration to get correct FHIR version when multi-version support is needed
        var fhirVersion = FhirVersion.R4;

        var capabilityContext = new CapabilityContext(
            FhirVersion: fhirVersion,
            TenantId: int.TryParse(context.TenantId, out var tid) ? tid : null);

        var statement = await _capabilityService.GetCapabilityStatementAsync(capabilityContext, cancellationToken);

        // Get schema provider for FHIRPath evaluation
        var schemaProvider = _versionContext.GetBaseSchemaProvider(fhirVersion);

        // Convert to IElement for FHIRPath queries
        var typedElement = statement.ToElement(schemaProvider);

        // Extract all resource interactions using FHIRPath
        var resources = typedElement.Select("rest.resource");

        foreach (var resource in resources)
        {
            var resourceType = resource.Select("type").FirstOrDefault()?.Value as string;
            if (string.IsNullOrEmpty(resourceType))
            {
                continue;
            }

            var interactions = resource.Select("interaction.code");
            foreach (var interaction in interactions)
            {
                if (interaction.Value is string interactionCode)
                {
                    cache.AddInteraction(resourceType, interactionCode);
                }
            }
        }

        // Extract system-level interactions
        var systemInteractions = typedElement.Select("rest.interaction.code");
        foreach (var interaction in systemInteractions)
        {
            if (interaction.Value is string interactionCode)
            {
                cache.AddInteraction("_system", interactionCode);
            }
        }

        return cache;
    }

    /// <summary>
    /// Builds a cache key for the authorization context.
    /// </summary>
    private static string BuildCacheKey(FhirAuthorizationContext context)
    {
        return $"capability:{context.TenantId ?? "default"}";
    }

    /// <summary>
    /// Invalidates the interaction cache for a specific tenant.
    /// Called when CapabilityStatement is rebuilt.
    /// </summary>
    public void InvalidateCache(string? tenantId)
    {
        var cacheKey = $"capability:{tenantId ?? "default"}";
        _interactionCaches.TryRemove(cacheKey, out _);
        _logger.LogInformation("Invalidated capability interaction cache for {CacheKey}", cacheKey);
    }

    /// <summary>
    /// Clears all interaction caches.
    /// </summary>
    public void ClearAllCaches()
    {
        _interactionCaches.Clear();
        _logger.LogInformation("Cleared all capability interaction caches");
    }
}
