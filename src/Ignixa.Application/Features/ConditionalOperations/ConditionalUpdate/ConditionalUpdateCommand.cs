// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate;

/// <summary>
/// Command for conditional update operation.
/// FHIR R4 Section 3.1.0.6: Conditional Update
/// If search matches 0 resources: create new (generate ID)
/// If search matches 1 resource: update existing
/// If search matches multiple: error (412 Precondition Failed)
/// </summary>
public record ConditionalUpdateCommand(
    int TenantId,
    string ResourceType,
    string SearchCriteria,  // Query string parameters (e.g., "identifier=system|value")
    ResourceJsonNode JsonNode,  // Parsed FHIR resource (parsed at endpoint layer)
    ProvenanceJsonNode? ProvenanceResource = null,
    string? RequestId = null) : IRequest<ConditionalUpdateResult>;
