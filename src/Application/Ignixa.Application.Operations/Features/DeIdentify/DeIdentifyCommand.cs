// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Operations.Features.DeIdentify;

/// <summary>
/// Command for FHIR $de-identify operation.
/// De-identifies a FHIR resource using a configurable DARTS policy.
/// </summary>
/// <param name="TenantId">The tenant ID for multi-tenant isolation.</param>
/// <param name="InputResource">The FHIR resource to de-identify.</param>
/// <param name="Policy">The de-identification policy code (e.g., HHS_SAFE_HARBOR_DETERMINISTIC_METHOD).</param>
/// <param name="ConfigurationLibrary">The Library resource containing DeIdOptions configuration.</param>
public record DeIdentifyCommand(
    int TenantId,
    ResourceJsonNode InputResource,
    string Policy,
    ResourceJsonNode ConfigurationLibrary) : IRequest<DeIdentifyResult>;

/// <summary>
/// Result of a FHIR $de-identify operation.
/// </summary>
/// <param name="IsSuccess">Whether the de-identification succeeded.</param>
/// <param name="OutputResource">The de-identified FHIR resource, or null if failed.</param>
/// <param name="ErrorMessage">Error message if de-identification failed.</param>
public record DeIdentifyResult(
    bool IsSuccess,
    ResourceJsonNode? OutputResource,
    string? ErrorMessage);
