// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization;

/// <summary>
/// Constants for JWT/OAuth claim types used in FHIR authorization.
/// Consolidates claim names to avoid typos and improve maintainability.
/// </summary>
public static class FhirClaimTypes
{
    // ========== User Identity Claims ==========

    /// <summary>
    /// Standard subject claim (sub).
    /// </summary>
    public const string Subject = "sub";

    /// <summary>
    /// Azure AD object ID claim (oid).
    /// </summary>
    public const string ObjectId = "oid";

    /// <summary>
    /// WS-Federation name identifier claim.
    /// </summary>
    public const string NameIdentifier = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

    // ========== Tenant Claims ==========

    /// <summary>
    /// Custom tenant ID claim.
    /// </summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// Azure AD B2C extension tenant ID claim.
    /// </summary>
    public const string ExtensionTenantId = "extension_TenantId";

    // ========== Role Claims ==========

    /// <summary>
    /// Standard role claim.
    /// </summary>
    public const string Role = "role";

    /// <summary>
    /// Standard roles claim (plural).
    /// </summary>
    public const string Roles = "roles";

    /// <summary>
    /// WS-Federation role claim.
    /// </summary>
    public const string WsFederationRole = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    // ========== SMART on FHIR Claims ==========

    /// <summary>
    /// OAuth scope claim.
    /// </summary>
    public const string Scope = "scope";

    /// <summary>
    /// Azure AD scope claim (scp).
    /// </summary>
    public const string Scp = "scp";

    /// <summary>
    /// SMART patient context claim.
    /// </summary>
    public const string Patient = "patient";

    /// <summary>
    /// SMART launch context patient claim.
    /// </summary>
    public const string LaunchContextPatient = "launch_context_patient";

    /// <summary>
    /// SMART encounter context claim.
    /// </summary>
    public const string Encounter = "encounter";

    /// <summary>
    /// SMART launch context encounter claim.
    /// </summary>
    public const string LaunchContextEncounter = "launch_context_encounter";

    /// <summary>
    /// SMART fhirUser claim (FHIR resource reference).
    /// </summary>
    public const string FhirUser = "fhirUser";

    /// <summary>
    /// OAuth client ID claim.
    /// </summary>
    public const string ClientId = "client_id";

    /// <summary>
    /// OAuth authorized party claim (azp).
    /// </summary>
    public const string AuthorizedParty = "azp";
}
