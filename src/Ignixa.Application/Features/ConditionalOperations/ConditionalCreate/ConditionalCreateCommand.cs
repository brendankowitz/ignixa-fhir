// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalCreate;

/// <summary>
/// Command for conditional create operation.
/// If search matches 0 resources: create new
/// If search matches 1 resource: return existing
/// If search matches multiple: error (412 Precondition Failed)
/// </summary>
/// <param name="TenantId">The tenant ID for multi-tenant isolation.</param>
/// <param name="ResourceType">The FHIR resource type.</param>
/// <param name="IfNoneExist">Search query from If-None-Exist header.</param>
/// <param name="RequestBody">Raw FHIR JSON resource to create.</param>
/// <param name="RequestId">Optional request ID for logging correlation.</param>
public record ConditionalCreateCommand(
    int TenantId,
    string ResourceType,
    string IfNoneExist,
    string RequestBody,
    string? RequestId = null) : IRequest<ConditionalCreateResult>;
