// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using Medino;
using Ignixa.Application.Features.Bundle;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic command to create or update any FHIR resource.
/// Works for all resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
/// <param name="Resource">The resource as ResourceJsonNode (provides cached ISourceNode and ITypedElement).</param>
/// <param name="HttpMethod">The HTTP method used (POST or PUT). POST means CREATE (always new resource), PUT means UPSERT (create or update).</param>
/// <param name="Coordinator">Optional deferred write coordinator for bundle operations. When provided, the handler queues the write for batch processing. When null, the handler writes immediately.</param>
/// <param name="IfMatch">Optional ETag for optimistic concurrency control. If specified, update only succeeds if resource version matches. Format: version ID (e.g., "5"), not weak ETag format.</param>
/// <param name="ValidationTierOverride">Optional validation tier override from Prefer header. When provided, overrides tenant configuration. Null means use tenant default.</param>
public record CreateOrUpdateResourceCommand(
    string ResourceType,
    string Id,
    ResourceJsonNode JsonNode,
    HttpMethod HttpMethod,
    DeferredWriteCoordinator? Coordinator = null,
    string? IfMatch = null,
    ValidationTier? ValidationTierOverride = null) : IRequest<ResourceKey>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate update capability for this resource type.
    /// Checks if CapabilityStatement advertises 'update' interaction for the resource type.
    /// Note: FHIR PUT is an upsert operation (create-or-update), but we check 'update' as it's more restrictive.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'update').exists()";
}
