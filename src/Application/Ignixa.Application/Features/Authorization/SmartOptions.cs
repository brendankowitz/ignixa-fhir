// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace Ignixa.Application.Features.Authorization;

/// <summary>
/// Configuration options for SMART on FHIR authorization.
/// </summary>
public class SmartOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "SmartOnFhir";

    /// <summary>
    /// Whether to enable SMART configuration endpoint (.well-known/smart-configuration).
    /// Default: true.
    /// </summary>
    public bool EnableSmartConfiguration { get; set; } = true;

    /// <summary>
    /// Whether to support legacy SMART v1 scope format (e.g., patient/*.read).
    /// Default: false. Set to true for backwards compatibility with older SMART apps.
    /// </summary>
    public bool EnableV1ScopeCompatibility { get; set; }

    /// <summary>
    /// Whether to enable refresh token support in SMART authorization flows.
    /// Default: true.
    /// </summary>
    public bool EnableRefreshTokens { get; set; } = true;

    /// <summary>
    /// List of SMART capabilities to advertise in .well-known/smart-configuration.
    /// Common values include: launch-ehr, launch-standalone, client-public, client-confidential-symmetric,
    /// sso-openid-connect, context-ehr-patient, context-standalone-patient, permission-offline,
    /// permission-patient, permission-user.
    /// </summary>
    public Collection<string> SupportedCapabilities { get; } = new();

    /// <summary>
    /// OAuth 2.0 authorization endpoint URL.
    /// Used in SMART oauth-uris extension in CapabilityStatement.
    /// </summary>
    public string? AuthorizeUrl { get; set; }

    /// <summary>
    /// OAuth 2.0 token endpoint URL.
    /// Used in SMART oauth-uris extension in CapabilityStatement.
    /// </summary>
    public string? TokenUrl { get; set; }

    /// <summary>
    /// OAuth 2.0 token introspection endpoint URL (optional).
    /// Used in SMART oauth-uris extension in CapabilityStatement.
    /// </summary>
    public string? IntrospectUrl { get; set; }

    /// <summary>
    /// OAuth 2.0 token revocation endpoint URL (optional).
    /// Used in SMART oauth-uris extension in CapabilityStatement.
    /// </summary>
    public string? RevokeUrl { get; set; }
}
