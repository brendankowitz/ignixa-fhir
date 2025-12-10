// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// Claims extracted from a SMART on FHIR OAuth token.
/// </summary>
public record SmartTokenClaims
{
    /// <summary>
    /// The raw scope string from the token.
    /// </summary>
    public required string ScopeString { get; init; }

    /// <summary>
    /// Parsed SMART scopes.
    /// </summary>
    public required IReadOnlyList<SmartScope> Scopes { get; init; }

    /// <summary>
    /// Parsed special (non-FHIR resource) scopes from the token.
    /// Includes OpenID Connect, offline access, and launch scopes.
    /// </summary>
    public IReadOnlyList<SpecialScope> SpecialScopes { get; init; } = Array.Empty<SpecialScope>();

    /// <summary>
    /// Indicates if the token includes the openid scope.
    /// Required for OpenID Connect authentication flows.
    /// </summary>
    public bool HasOpenIdScope { get; init; }

    /// <summary>
    /// Indicates if the token includes the offline_access scope.
    /// When true, the client may request refresh tokens.
    /// </summary>
    public bool HasOfflineAccess { get; init; }

    /// <summary>
    /// Launch context type from launch scopes.
    /// Returns "patient" for launch/patient, "encounter" for launch/encounter, or null for standalone launch.
    /// </summary>
    public string? LaunchContext { get; init; }

    /// <summary>
    /// Patient ID from the launch context (if present).
    /// Required for patient/*.* scopes.
    /// </summary>
    public string? PatientId { get; init; }

    /// <summary>
    /// Encounter ID from the launch context (if present).
    /// </summary>
    public string? EncounterId { get; init; }

    /// <summary>
    /// FHIR User reference from the fhirUser claim (e.g., "Practitioner/123").
    /// </summary>
    public string? FhirUser { get; init; }

    /// <summary>
    /// Token expiration time.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Token issued time.
    /// </summary>
    public DateTimeOffset? IssuedAt { get; init; }

    /// <summary>
    /// Client ID that requested the token.
    /// </summary>
    public string? ClientId { get; init; }
}

/// <summary>
/// SMART authorization context for a request.
/// Contains parsed token claims and scopes.
/// </summary>
public record SmartAuthorizationContext
{
    /// <summary>
    /// Token claims extracted from the OAuth access token.
    /// </summary>
    public required SmartTokenClaims TokenClaims { get; init; }

    /// <summary>
    /// Parsed SMART scopes from the token.
    /// </summary>
    public required IReadOnlyList<SmartScope> Scopes { get; init; }

    /// <summary>
    /// Patient context from the token (patient claim or launch context).
    /// Used for patient/*.* scopes to filter results to patient compartment.
    /// </summary>
    public string? PatientContext { get; init; }

    /// <summary>
    /// Encounter context from the token (encounter claim or launch context).
    /// </summary>
    public string? EncounterContext { get; init; }

    /// <summary>
    /// User context from the fhirUser claim.
    /// Contains a FHIR resource reference (e.g., "Practitioner/123").
    /// </summary>
    public string? UserContext { get; init; }
}
