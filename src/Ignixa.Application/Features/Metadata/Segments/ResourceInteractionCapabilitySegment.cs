// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Resource interaction capability segment.
/// Provides CRUD operations and system-level interactions for all resource types.
/// Changes when FHIR version changes or when resource types are added/removed.
/// </summary>
public class ResourceInteractionCapabilitySegment : ICapabilitySegment
{
    private readonly VersionAwareSearchParameterDefinitionManager _searchParamManager;
    private readonly ILogger<ResourceInteractionCapabilitySegment> _logger;

    public ResourceInteractionCapabilitySegment(
        VersionAwareSearchParameterDefinitionManager searchParamManager,
        ILogger<ResourceInteractionCapabilitySegment> logger)
    {
        _searchParamManager = searchParamManager ?? throw new ArgumentNullException(nameof(searchParamManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SegmentKey => "interactions";

    public int Priority => 20; // Execute after static

    public async ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying resource interaction capability segment for {FhirVersion}", context.FhirVersion);

        // Get manager for this FHIR version
        var manager = await _searchParamManager.GetManagerForVersionAsync(context.FhirVersion, cancellationToken);

        // Get all resource types from search parameter manager
        // (ResourceTypeNames is already expanded from abstract base types at initialization)
        var resourceTypes = manager.ResourceTypeNames
            .OrderBy(rt => rt)
            .ToList();

        _logger.LogDebug("Found {Count} resource types for {FhirVersion}", resourceTypes.Count, context.FhirVersion);

        // Initialize REST component if not exists
        if (statement.Rest == null || statement.Rest.Count == 0)
        {
            var newRestComponent = new RestComponentJsonNode
            {
                Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            };
            statement.AddRest(newRestComponent);
        }

        var restComponent = statement.Rest![0];

        // Add resource components with interactions
        foreach (var resourceType in resourceTypes)
        {
            var resourceComponent = new ResourceComponentJsonNode
            {
                Type = resourceType,
                Profile = ReferenceOrCanonicalJsonNode.FromCanonical($"http://hl7.org/fhir/StructureDefinition/{resourceType}"),
                Interaction = BuildResourceInteractions(resourceType),
                Versioning = ResourceComponentJsonNode.ResourceVersionPolicy.Versioned,
                ReadHistory = false,
                UpdateCreate = true,
                ConditionalCreate = false,
                ConditionalUpdate = false,
                ConditionalDelete = ConditionalDeleteStatus.NotSupported,
                SearchParam = new List<SearchParamJsonNode>(), // Will be populated by SearchParameterCapabilitySegment
            };

            restComponent.AddResource(resourceComponent);
        }

        // Add system-level interactions
        restComponent.Interaction = BuildSystemInteractions();

        _logger.LogDebug("Added {Count} resource components with interactions", resourceTypes.Count);
    }

    public async ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Hash is based on FHIR version + sorted resource type list
        var manager = await _searchParamManager.GetManagerForVersionAsync(context.FhirVersion, cancellationToken);

        var resourceTypes = manager.ResourceTypeNames
            .OrderBy(rt => rt)
            .ToList();

        var hashInput = $"{context.FhirVersion}|{string.Join(",", resourceTypes)}";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
    }

    private IReadOnlyList<ResourceInteractionJsonNode> BuildResourceInteractions(string resourceType)
    {
        var interactions = new List<ResourceInteractionJsonNode>
        {
            new() { Code = TypeRestfulInteraction.Read },
            new() { Code = TypeRestfulInteraction.Create },
            new() { Code = TypeRestfulInteraction.SearchType },
        };

        // AuditEvent special case: no update or delete (per FHIR spec)
        if (resourceType != "AuditEvent")
        {
            interactions.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Update });
            interactions.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Delete });
        }

        return interactions;
    }

    private IReadOnlyList<SystemInteractionJsonNode> BuildSystemInteractions()
    {
        return new List<SystemInteractionJsonNode>
        {
            new() { Code = SystemRestfulInteraction.Transaction },
            new() { Code = SystemRestfulInteraction.Batch },
        };
    }
}
