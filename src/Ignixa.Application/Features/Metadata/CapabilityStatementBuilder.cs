// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using IgnixaSearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Features.Metadata;

/// <summary>
/// Builds FHIR CapabilityStatement resources for the server using custom SourceNode models.
/// Supports multi-version FHIR (R4, R4B, R5, STU3) and multi-tenancy.
/// NO FIRELY SDK DEPENDENCY.
/// </summary>
/// <remarks>
/// OBSOLETE: This class has been replaced by the segmented architecture (Phase 1.2).
/// Use <see cref="CapabilityStatementService"/> with <see cref="Segments.ICapabilitySegment"/> implementations instead.
/// The new architecture provides:
/// - Smart caching with version hash validation
/// - Segmented capability generation (static, interactions, search params)
/// - Runtime updates without rebuilding entire statement
/// - Better separation of concerns
/// This class will be removed in Phase 3.
/// </remarks>
[Obsolete("Use CapabilityStatementService with ICapabilitySegment implementations. This class will be removed in Phase 3.", error: false)]
public class CapabilityStatementBuilder
{
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly VersionAwareSearchParameterDefinitionManager _searchParamManager;
    private readonly ILogger<CapabilityStatementBuilder> _logger;

    public CapabilityStatementBuilder(
        ITenantConfigurationStore tenantConfigStore,
        VersionAwareSearchParameterDefinitionManager searchParamManager,
        ILogger<CapabilityStatementBuilder> logger)
    {
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _searchParamManager = searchParamManager ?? throw new ArgumentNullException(nameof(searchParamManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CapabilityStatementJsonNode> BuildAsync(
        int? tenantId,
        CancellationToken cancellationToken = default)
    {
        TenantConfiguration? tenantConfig = null;
        FhirSpecification fhirVersion = FhirSpecification.R4; // Default
        string fhirVersionString = "4.0.1";

        if (tenantId.HasValue)
        {
            tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(
                tenantId.Value,
                cancellationToken);

            if (tenantConfig == null)
            {
                throw new InvalidOperationException($"Tenant {tenantId} not found or inactive");
            }

            fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            fhirVersionString = tenantConfig.FhirVersion;
        }
        else
        {
            // System-wide: Use first active tenant's version, or default to R4
            var allTenants = await _tenantConfigStore.GetAllTenantsAsync(cancellationToken);
            if (allTenants.Count > 0)
            {
                fhirVersion = FhirSpecificationExtensions.FromVersionString(allTenants[0].FhirVersion);
                fhirVersionString = allTenants[0].FhirVersion;
            }
        }

        _logger.LogDebug(
            "Building capability statement for TenantId={TenantId}, FhirVersion={FhirVersion}",
            tenantId?.ToString() ?? "system-wide",
            fhirVersion);

        // Create base capability statement (version-aware construction)
        var capability = new CapabilityStatementJsonNode
        {
            FhirVersion = fhirVersion,
            Url = "http://ignixa.example.com/fhir/CapabilityStatement",
            Version = "0.1.0",
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Experimental = false,
            Date = DateTimeOffset.UtcNow.ToString("O"),
            Publisher = "Ignixa Contributors",
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
            FhirVersionString = fhirVersionString,
            Format = new List<string> { "application/fhir+json" },
            PatchFormat = new List<string> { "application/json-patch+json" },
            Software = new SoftwareComponentJsonNode
            {
                Name = "Ignixa FHIR Server",
                Version = "0.1.0",
                ReleaseDate = "2025-10-16",
            },
        };

        // Update name/title based on tenant
        if (tenantConfig != null)
        {
            capability.Name = $"IgnixaFhirServer_{tenantConfig.DisplayName.Replace(" ", string.Empty, StringComparison.Ordinal)}";
        }
        else
        {
            capability.Name = "IgnixaFhirServer";
        }

        // Build REST component
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Resource = await BuildResourceDefinitionsAsync(fhirVersion, cancellationToken),
            Interaction = BuildSystemInteractions(),
        };

        capability.Rest = new List<RestComponentJsonNode> { restComponent };

        _logger.LogDebug(
            "Built capability statement with {ResourceCount} resource types",
            restComponent.Resource?.Count ?? 0);

        return capability;
    }

    private async Task<IList<ResourceComponentJsonNode>> BuildResourceDefinitionsAsync(
        FhirSpecification fhirVersion,
        CancellationToken cancellationToken)
    {
        var resources = new List<ResourceComponentJsonNode>();

        // Get manager for this FHIR version
        var manager = await _searchParamManager.GetManagerForVersionAsync(fhirVersion, cancellationToken);

        // Group search parameters by resource type
        var resourceTypes = manager.AllSearchParameters
            .SelectMany(sp => sp.BaseResourceTypes ?? Enumerable.Empty<string>())
            .Distinct()
            .OrderBy(rt => rt)
            .ToList();

        foreach (var resourceType in resourceTypes)
        {
            var searchParams = manager.GetSearchParameters(resourceType).ToList();

            if (searchParams.Count == 0)
            {
                continue; // Skip resources with no search parameters
            }

            var resourceComponent = new ResourceComponentJsonNode
            {
                Type = resourceType,
                Profile = ReferenceOrCanonicalJsonNode.FromCanonical($"http://hl7.org/fhir/StructureDefinition/{resourceType}"),
                Interaction = BuildResourceInteractions(),
                Versioning = ResourceComponentJsonNode.ResourceVersionPolicy.Versioned,
                ReadHistory = false,
                UpdateCreate = true,
                ConditionalCreate = false,
                ConditionalUpdate = false,
                ConditionalDelete = ConditionalDeleteStatus.NotSupported,
                SearchParam = BuildSearchParameters(searchParams),
            };

            resources.Add(resourceComponent);
        }

        _logger.LogDebug("Built {Count} resource definitions for {FhirVersion}", resources.Count, fhirVersion);

        return resources;
    }

    private IList<ResourceInteractionJsonNode> BuildResourceInteractions()
    {
        return new List<ResourceInteractionJsonNode>
        {
            new() { Code = TypeRestfulInteraction.Read },
            new() { Code = TypeRestfulInteraction.Create },
            new() { Code = TypeRestfulInteraction.Update },
            new() { Code = TypeRestfulInteraction.Delete },
            new() { Code = TypeRestfulInteraction.SearchType },
        };
    }

    private IList<SystemInteractionJsonNode> BuildSystemInteractions()
    {
        return new List<SystemInteractionJsonNode>
        {
            new() { Code = SystemRestfulInteraction.Transaction },
            new() { Code = SystemRestfulInteraction.Batch },
        };
    }

    private IList<SearchParamJsonNode> BuildSearchParameters(List<SearchParameterInfo> searchParams)
    {
        var result = new List<SearchParamJsonNode>();

        foreach (var sp in searchParams.Where(p => p.IsSupported))
        {
            result.Add(new SearchParamJsonNode
            {
                Name = sp.Code,
                Definition = sp.Url?.ToString() ?? string.Empty,
                Type = MapSearchParamType(sp.Type),
                Documentation = sp.Description,
            });
        }

        return result;
    }

    private static SearchParamType MapSearchParamType(IgnixaSearchParamType type)
    {
        return type switch
        {
            IgnixaSearchParamType.Number => SearchParamType.Number,
            IgnixaSearchParamType.Date => SearchParamType.Date,
            IgnixaSearchParamType.String => SearchParamType.String,
            IgnixaSearchParamType.Token => SearchParamType.Token,
            IgnixaSearchParamType.Reference => SearchParamType.Reference,
            IgnixaSearchParamType.Composite => SearchParamType.Composite,
            IgnixaSearchParamType.Quantity => SearchParamType.Quantity,
            IgnixaSearchParamType.Uri => SearchParamType.Uri,
            IgnixaSearchParamType.Special => SearchParamType.Special,
            _ => SearchParamType.String,
        };
    }
}
