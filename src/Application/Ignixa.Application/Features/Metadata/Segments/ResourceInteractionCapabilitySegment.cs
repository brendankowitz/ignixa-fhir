// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using IType = Ignixa.Abstractions.IType;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
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
    private const int InteractionSetRevision = 4;

    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<ResourceInteractionCapabilitySegment> _logger;
    private readonly IFhirRepositoryFactory _repositoryFactory;

    public ResourceInteractionCapabilitySegment(
        IFhirVersionContext versionContext,
        ILogger<ResourceInteractionCapabilitySegment> logger,
        IFhirRepositoryFactory repositoryFactory)
    {
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    }

    public string SegmentKey => "interactions";

    public int Priority => 20; // Execute after static

    public async ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying resource interaction capability segment for {FhirVersion}", context.FhirVersion);
        var capabilities = await GetStorageCapabilitiesAsync(context, cancellationToken);

        // Get schema provider for this FHIR version and tenant (includes custom resource types)
        var schemaProvider = _versionContext.GetSchemaProvider(context.FhirVersion, context.TenantId);

        // Get all resource types from schema provider
        // (ResourceTypeNames contains all concrete FHIR resource types for this version)
        var resourceTypes = schemaProvider.ResourceTypeNames
            .OrderBy(rt => rt, StringComparer.Ordinal)
            .ToList();

        _logger.LogDebug("Found {Count} resource types for {FhirVersion}", resourceTypes.Count, context.FhirVersion);

        // Initialize REST component if not exists
        if (statement.Rest.Count == 0)
        {
            statement.Rest.Add(new RestComponentJsonNode
            {
                FhirVersion = context.FhirVersion,
                Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            });
        }

        var restComponent = statement.Rest[0];

        // Add resource components with interactions
        foreach (var resourceType in resourceTypes)
        {
            IType? schema = schemaProvider.GetTypeDefinition(resourceType);
            if (schema == null)
            {
                _logger.LogWarning("Could not load schema for resource type {ResourceType}", resourceType);
                continue;
            }

            string? canonicalUrl = GetResourceCanonical(schemaProvider, resourceType);
            if (canonicalUrl == null)
            {
                _logger.LogWarning("Could not resolve defining canonical for resource type {ResourceType}", resourceType);
                continue;
            }

            var resourceComponent = new ResourceComponentJsonNode
            {
                FhirVersion = context.FhirVersion,
                Type = resourceType,
                Profile = ReferenceOrCanonicalJsonNode.FromCanonical(canonicalUrl),
                Versioning = ResourceComponentJsonNode.ResourceVersionPolicy.Versioned,
                ReadHistory = capabilities.VersionedRead,
                UpdateCreate = true,
                ConditionalCreate = true,
                ConditionalUpdate = true,
                ConditionalDelete = ConditionalDeleteStatus.Single,
            };

            // Add interactions (will be populated by SearchParameterCapabilitySegment for SearchParam)
            foreach (var interaction in BuildResourceInteractions(resourceType, capabilities.VersionedRead))
            {
                resourceComponent.Interaction.Add(interaction);
            }

            restComponent.Resource.Add(resourceComponent);
        }

        // Add system-level interactions
        restComponent.Interaction.Clear();
        foreach (var interaction in BuildSystemInteractions(capabilities.AtomicWrite))
        {
            restComponent.Interaction.Add(interaction);
        }

        _logger.LogDebug("Added {Count} resource components with interactions", resourceTypes.Count);

    }

    public async ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Hash is based on FHIR version + sorted resource type list (includes custom resource types)
        var schemaProvider = _versionContext.GetSchemaProvider(context.FhirVersion, context.TenantId);

        var resourceTypes = schemaProvider.ResourceTypeNames
            .OrderBy(rt => rt, StringComparer.Ordinal)
            .ToList();

        // InteractionSetRevision participates so that changing the declared interaction set
        // invalidates statements cached under the old hash; the resource type list alone would
        // not move. Bump it whenever BuildResourceInteractions or BuildSystemInteractions changes.
        var capabilities = await GetStorageCapabilitiesAsync(context, cancellationToken);
        var identities = resourceTypes.Select(type => $"{type}:{GetResourceCanonical(schemaProvider, type)}");
        var hashInput = $"{context.FhirVersion}|{string.Join(",", identities)}|{InteractionSetRevision}|{capabilities}";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
    }

    private static string? GetResourceCanonical(IFhirSchemaProvider provider, string resourceType)
        => provider is CompositeStructureDefinitionSummaryProvider composite
            ? composite.GetResourceCanonical(resourceType)
            : $"http://hl7.org/fhir/StructureDefinition/{resourceType}";

    private async ValueTask<(bool AtomicWrite, bool VersionedRead)> GetStorageCapabilitiesAsync(
        CapabilityContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
        {
            return (false, false);
        }
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);
        return (repository is IAtomicFhirRepository, repository is IVersionedResourceRepository);
    }

    private IReadOnlyList<ResourceInteractionJsonNode> BuildResourceInteractions(string resourceType, bool versionedRead)
    {
        // History is served by HistoryEndpoints for every resource type. Undeclared interactions
        // are indistinguishable from unimplemented ones to a conformance client, which silently
        // skips the corresponding tests rather than reporting them.
        var interactions = new List<ResourceInteractionJsonNode>
        {
            new() { Code = TypeRestfulInteraction.Read },
            new() { Code = TypeRestfulInteraction.Create },
            new() { Code = TypeRestfulInteraction.SearchType },
            new() { Code = TypeRestfulInteraction.HistoryInstance },
            new() { Code = TypeRestfulInteraction.HistoryType },
        };
        if (versionedRead)
        {
            interactions.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Vread });
        }

        // AuditEvent special case: no mutating interactions (per FHIR spec)
        if (resourceType != "AuditEvent")
        {
            interactions.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Update });
            interactions.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Patch });
            interactions.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Delete });
        }

        return interactions;
    }

    private IReadOnlyList<SystemInteractionJsonNode> BuildSystemInteractions(bool atomicWrite)
    {
        var interactions = new List<SystemInteractionJsonNode>
        {
            new() { Code = SystemRestfulInteraction.Batch },
            new() { Code = SystemRestfulInteraction.HistorySystem },
        };
        if (atomicWrite)
        {
            interactions.Add(new SystemInteractionJsonNode { Code = SystemRestfulInteraction.Transaction });
        }
        return interactions;
    }
}
