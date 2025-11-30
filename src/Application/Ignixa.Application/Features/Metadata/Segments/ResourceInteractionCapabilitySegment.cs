// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
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
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<ResourceInteractionCapabilitySegment> _logger;

    public ResourceInteractionCapabilitySegment(
        IFhirVersionContext versionContext,
        ILogger<ResourceInteractionCapabilitySegment> logger)
    {
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SegmentKey => "interactions";

    public int Priority => 20; // Execute after static

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying resource interaction capability segment for {FhirVersion}", context.FhirVersion);

        // Get schema provider for this FHIR version and tenant (includes custom resource types)
        var schemaProvider = _versionContext.GetSchemaProvider(context.FhirVersion, context.TenantId);

        // Get all resource types from schema provider
        // (ResourceTypeNames contains all concrete FHIR resource types for this version)
        var resourceTypes = schemaProvider.ResourceTypeNames
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
            IType? schema = schemaProvider.GetTypeDefinition(resourceType);
            if (schema == null)
            {
                _logger.LogWarning("Could not load schema for resource type {ResourceType}", resourceType);
                continue;
            }

            // Build canonical URL from resource type name
            string canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";

            var resourceComponent = new ResourceComponentJsonNode
            {
                Type = resourceType,
                Profile = ReferenceOrCanonicalJsonNode.FromCanonical(canonicalUrl),
                Interaction = BuildResourceInteractions(resourceType),
                Versioning = ResourceComponentJsonNode.ResourceVersionPolicy.Versioned,
                ReadHistory = true,
                UpdateCreate = true,
                ConditionalCreate = true,
                ConditionalUpdate = true,
                ConditionalDelete = ConditionalDeleteStatus.Single,
                SearchParam = new List<SearchParamJsonNode>(), // Will be populated by SearchParameterCapabilitySegment
            };

            restComponent.AddResource(resourceComponent);
        }

        // Add system-level interactions
        restComponent.Interaction = BuildSystemInteractions();

        _logger.LogDebug("Added {Count} resource components with interactions", resourceTypes.Count);

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Hash is based on FHIR version + sorted resource type list (includes custom resource types)
        var schemaProvider = _versionContext.GetSchemaProvider(context.FhirVersion, context.TenantId);

        var resourceTypes = schemaProvider.ResourceTypeNames
            .OrderBy(rt => rt)
            .ToList();

        var hashInput = $"{context.FhirVersion}|{string.Join(",", resourceTypes)}";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return ValueTask.FromResult(Convert.ToBase64String(hashBytes));
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
