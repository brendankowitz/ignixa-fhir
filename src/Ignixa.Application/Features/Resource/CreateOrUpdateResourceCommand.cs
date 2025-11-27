// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using Medino;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic command to create or update any FHIR resource.
/// Works for all resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
/// <param name="Resource">The resource as ResourceJsonNode (provides cached ISourceNode and IElement).</param>
/// <param name="HttpMethod">The HTTP method used (POST or PUT). POST means CREATE (always new resource), PUT means UPSERT (create or update).</param>
/// <param name="Coordinator">Optional deferred write coordinator for bundle operations. When provided, the handler queues the write for batch processing. When null, the handler writes immediately.</param>
/// <param name="IfMatch">Optional ETag for optimistic concurrency control. If specified, update only succeeds if resource version matches. Format: version ID (e.g., "5"), not weak ETag format.</param>
/// <param name="ValidationDepthOverride">Optional validation depth override from Prefer header. When provided, overrides tenant configuration. Null means use tenant default.</param>
/// <param name="ProvenanceResource">Optional Provenance resource from X-Provenance header. When provided, the handler will automatically fill the target reference and create the Provenance resource after the main resource is created/updated.</param>
public record CreateOrUpdateResourceCommand(
    string ResourceType,
    string Id,
    ResourceJsonNode JsonNode,
    HttpMethod HttpMethod,
    DeferredWriteCoordinator? Coordinator = null,
    string? IfMatch = null,
    ValidationDepth? ValidationDepthOverride = null,
    ProvenanceJsonNode? ProvenanceResource = null) : IRequest<UpdateResult>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate update capability for this resource type.
    /// Checks if CapabilityStatement advertises 'update' interaction for the resource type.
    /// Note: FHIR PUT is an upsert operation (create-or-update), but we check 'update' as it's more restrictive.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'update').exists()";
}
