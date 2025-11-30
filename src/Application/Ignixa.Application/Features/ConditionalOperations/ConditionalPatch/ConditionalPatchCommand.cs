// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;

/// <summary>
/// Command for conditional patch operation.
/// If search matches 0 resources: error (404 Not Found)
/// If search matches 1 resource: patch existing
/// If search matches multiple: error (412 Precondition Failed)
/// </summary>
public record ConditionalPatchCommand(
    int TenantId,
    string ResourceType,
    string SearchCriteria,  // Query string parameters (e.g., "identifier=system|value")
    ResourceJsonNode PatchDocument,  // FHIR Parameters resource (parsed at endpoint layer)
    string? RequestId = null) : IRequest<ConditionalPatchResult>;
