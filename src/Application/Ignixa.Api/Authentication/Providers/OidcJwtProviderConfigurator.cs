// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Ignixa.Api.Authentication.Providers;

/// <summary>
/// Configures JWT Bearer authentication for OpenID Connect providers.
/// Works with any OIDC-compliant identity provider including Entra, Okta, Auth0, Keycloak, etc.
/// </summary>
/// <remarks>
/// <para>
/// Required configuration in appsettings.json:
/// </para>
/// <code>
/// {
///   "Authentication": {
///     "Authority": "https://your-identity-provider.com",
///     "Audience": "your-audience"  // optional
///   }
/// }
/// </code>
/// <para>
/// <strong>Common Provider Examples:</strong>
/// </para>
/// <list type="bullet">
///   <item>
///     <term>Microsoft Entra ID (Azure AD)</term>
///     <description>
///       Authority: https://login.microsoftonline.com/{tenant-id}/v2.0
///     </description>
///   </item>
///   <item>
///     <term>Okta</term>
///     <description>
///       Authority: https://your-org.okta.com
///     </description>
///   </item>
///   <item>
///     <term>Auth0</term>
///     <description>
///       Authority: https://your-tenant.auth0.com
///     </description>
///   </item>
///   <item>
///     <term>OpenIddict (self-hosted)</term>
///     <description>
///       Authority: https://your-server.com
///     </description>
///   </item>
///   <item>
///     <term>Keycloak</term>
///     <description>
///       Authority: https://your-keycloak.com/realms/{realm-name}
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class OidcJwtProviderConfigurator : IJwtBearerProviderConfigurator
{
    /// <inheritdoc/>
    public string ProviderName => "OIDC";

    /// <inheritdoc/>
    public void Configure(JwtBearerOptions options, IConfigurationSection authConfig)
    {
        var authority = authConfig["Authority"]
            ?? throw new InvalidOperationException(
                "Authentication:Authority is required. Specify the identity provider's base URL.");
        var audience = authConfig["Audience"];

        options.Authority = authority;

        if (!string.IsNullOrEmpty(audience))
        {
            options.Audience = audience;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrEmpty(audience),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }
}
