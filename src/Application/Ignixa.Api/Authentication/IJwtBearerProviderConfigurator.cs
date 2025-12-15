// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ignixa.Api.Authentication;

/// <summary>
/// Configurator for a specific JWT Bearer authentication provider.
/// Implement this interface to add support for new OAuth2/OIDC providers.
/// </summary>
/// <remarks>
/// <para>
/// This interface follows the Open/Closed Principle - new authentication providers
/// can be added by implementing this interface without modifying existing code.
/// </para>
/// <para>
/// Implementations are automatically discovered and registered via
/// <see cref="JwtProviderRegistrationExtensions.AddJwtProviderConfigurators"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class MyCustomProviderConfigurator : IJwtBearerProviderConfigurator
/// {
///     public string ProviderName => "MyCustomProvider";
///
///     public void Configure(JwtBearerOptions options, IConfigurationSection authConfig)
///     {
///         var config = authConfig.GetSection("MyCustomProvider");
///         options.Authority = config["Authority"];
///         // ... additional configuration
///     }
/// }
/// </code>
/// </example>
public interface IJwtBearerProviderConfigurator
{
    /// <summary>
    /// Gets the provider name used to match against the Authentication:Provider configuration value.
    /// </summary>
    /// <remarks>
    /// This value is compared case-insensitively against the configured provider name.
    /// Examples: "Entra", "Okta", "OIDC", "OpenIddict", "JwtBearer".
    /// </remarks>
    string ProviderName { get; }

    /// <summary>
    /// Configures JWT Bearer options for this provider.
    /// </summary>
    /// <param name="options">The JWT Bearer options to configure.</param>
    /// <param name="authConfig">The Authentication configuration section from appsettings.</param>
    /// <remarks>
    /// Implementations should configure provider-specific settings such as:
    /// <list type="bullet">
    ///   <item><description>Authority URL</description></item>
    ///   <item><description>Audience</description></item>
    ///   <item><description>Token validation parameters</description></item>
    /// </list>
    /// Common settings (claim mappings, events) are applied after provider configuration.
    /// </remarks>
    void Configure(JwtBearerOptions options, IConfigurationSection authConfig);
}
