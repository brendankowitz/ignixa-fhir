// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.Domain.ElementModel;
using Medino;
using Sparky.Application.Features.Bundle;
using Sparky.Domain.Models;
using Sparky.SourceNodeSerialization;
using Sparky.SourceNodeSerialization.SourceNodes.Models;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic command to create or update any FHIR resource.
/// Works for all resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
/// <param name="Resource">The resource as ResourceJsonNode (provides cached ISourceNode and ITypedElement).</param>
/// <param name="RawJson">The raw JSON for fast storage.</param>
/// <param name="Coordinator">Optional deferred write coordinator for bundle operations. When provided, the handler queues the write for batch processing. When null, the handler writes immediately.</param>
public record CreateOrUpdateResourceCommand(
    string ResourceType,
    string Id,
    ResourceJsonNode Resource,
    string RawJson,
    DeferredWriteCoordinator? Coordinator = null) : IRequest<ResourceKey>;
