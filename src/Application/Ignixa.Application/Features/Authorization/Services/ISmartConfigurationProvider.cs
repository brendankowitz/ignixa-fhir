// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Services;

/// <summary>
/// Provides SMART on FHIR configuration data for OAuth2/OIDC endpoints.
/// Implementations should fetch configuration from OIDC discovery documents when possible.
/// </summary>
public interface ISmartConfigurationProvider
{
    /// <summary>
    /// Gets the SMART configuration data for the specified tenant.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID for multi-tenant scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SMART configuration data.</returns>
    ValueTask<SmartConfigurationData> GetConfigurationAsync(string? tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// SMART on FHIR configuration data.
/// Maps OIDC discovery document fields to SMART configuration fields.
/// </summary>
/// <param name="Issuer">The OAuth2 issuer URL (optional, required for OpenID Connect).</param>
/// <param name="JwksUri">The JSON Web Key Set URL (optional, required for OpenID Connect).</param>
/// <param name="AuthorizationEndpoint">The OAuth2 authorization endpoint URL.</param>
/// <param name="TokenEndpoint">The OAuth2 token endpoint URL.</param>
/// <param name="IntrospectionEndpoint">The token introspection endpoint URL (optional).</param>
/// <param name="RevocationEndpoint">The token revocation endpoint URL (optional).</param>
/// <param name="GrantTypes">Array of grant types supported at the token endpoint.</param>
/// <param name="TokenEndpointAuthMethods">Array of client authentication methods supported by the token endpoint.</param>
/// <param name="TokenEndpointAuthSigningAlgs">Array of token endpoint authentication signing algorithms supported (optional).</param>
/// <param name="SupportedScopes">Array of scopes supported by the server.</param>
/// <param name="Capabilities">Array of SMART capabilities supported by the server.</param>
public sealed record SmartConfigurationData(
    string? Issuer,
    string? JwksUri,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string? IntrospectionEndpoint,
    string? RevocationEndpoint,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> TokenEndpointAuthMethods,
    IReadOnlyList<string>? TokenEndpointAuthSigningAlgs,
    IReadOnlyList<string> SupportedScopes,
    IReadOnlyList<string> Capabilities);
