// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// Type of special SMART scope.
/// These are non-FHIR resource scopes defined in SMART App Launch and OpenID Connect.
/// </summary>
public enum SpecialScopeType
{
    /// <summary>
    /// OpenID Connect scopes: openid, profile, email, fhirUser.
    /// Used for identity and user information.
    /// </summary>
    OpenIdConnect,

    /// <summary>
    /// Offline access scope: offline_access.
    /// Requests a refresh token for long-lived access.
    /// </summary>
    OfflineAccess,

    /// <summary>
    /// Launch context scopes: launch, launch/patient, launch/encounter.
    /// Used in EHR launch flow to receive launch context.
    /// </summary>
    Launch
}

/// <summary>
/// Represents a special (non-FHIR resource) SMART scope.
/// These scopes are defined in SMART App Launch and OpenID Connect specifications.
/// Examples: openid, profile, email, fhirUser, offline_access, launch, launch/patient, launch/encounter
/// </summary>
/// <param name="Name">The scope name (e.g., "openid", "launch/patient").</param>
/// <param name="Type">The type of special scope.</param>
public record SpecialScope(string Name, SpecialScopeType Type);
