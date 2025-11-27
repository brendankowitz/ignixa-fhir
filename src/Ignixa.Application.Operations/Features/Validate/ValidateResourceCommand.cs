// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Medino;

namespace Ignixa.Application.Operations.Features.Validate;

/// <summary>
/// Command for FHIR $validate operation.
/// Validates a resource against FHIR specification and optionally against a profile.
/// Returns an OperationOutcome resource with validation issues.
/// </summary>
/// <param name="TenantId">The tenant ID for multi-tenant isolation.</param>
/// <param name="ResourceType">The FHIR resource type being validated (or null for system-level $validate).</param>
/// <param name="JsonNode">The resource JSON to validate.</param>
/// <param name="ValidationDepth">Validation depth: Minimal (structure only), Spec (+ required bindings), Full (+ extensible bindings + display).</param>
/// <param name="Mode">Optional validation mode: 'create' | 'update' | 'delete' (default: no mode).</param>
/// <param name="Profile">Optional profile URL to validate against specific profile.</param>
/// <param name="InstanceId">Optional instance ID for instance-level validation (required for update/delete modes).</param>
/// <param name="RequestId">Optional request ID for logging correlation.</param>
public record ValidateResourceCommand(
    int TenantId,
    string? ResourceType,
    ResourceJsonNode JsonNode,
    ValidationDepth ValidationDepth = ValidationDepth.Minimal,
    string? Mode = null,
    string? Profile = null,
    string? InstanceId = null,
    string? RequestId = null) : IRequest<ValidateResourceResult>;
