// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Services;

/// <summary>
/// SMART configuration provider that fetches OAuth2/OIDC endpoints from the provider's discovery document.
/// Requires Authentication:Authority to be configured. Uses standard OIDC discovery.
/// </summary>
public sealed class OidcDiscoverySmartConfigurationProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<OidcDiscoverySmartConfigurationProvider> logger) : ISmartConfigurationProvider
{
    private const string CacheKeyPrefix = "SmartConfig:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(
        configuration.GetValue("Authorization:SmartOnFhir:DiscoveryCacheTtlSeconds", 3600));

    /// <inheritdoc />
    public async ValueTask<SmartConfigurationData> GetConfigurationAsync(string? tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId ?? "default"}";

        if (cache.TryGetValue<SmartConfigurationData>(cacheKey, out var cachedConfig) && cachedConfig is not null)
        {
            return cachedConfig;
        }

        var config = await FetchConfigurationAsync(cancellationToken);
        cache.Set(cacheKey, config, _cacheTtl);

        return config;
    }

    private async Task<SmartConfigurationData> FetchConfigurationAsync(CancellationToken cancellationToken)
    {
        var authority = configuration["Authentication:Authority"]
            ?? configuration["Authentication:OpenIddict:Issuer"]
            ?? throw new InvalidOperationException(
                "Authentication:Authority must be configured for SMART configuration discovery");

        var discoveryUrl = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
        logger.LogDebug("Fetching OIDC discovery from {DiscoveryUrl}", discoveryUrl);

        var httpClient = httpClientFactory.CreateClient("OidcDiscovery");
        var response = await httpClient.GetAsync(new Uri(discoveryUrl), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OIDC discovery failed with status {response.StatusCode} for {discoveryUrl}. " +
                "Ensure the OAuth2 server is running and Authentication:Authority is correct.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var discovery = JsonSerializer.Deserialize<OidcDiscoveryDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse OIDC discovery document from {discoveryUrl}");

        logger.LogInformation("Loaded OIDC discovery from {Issuer}", discovery.Issuer);

        return BuildConfiguration(discovery);
    }

    private SmartConfigurationData BuildConfiguration(OidcDiscoveryDocument discovery)
    {
        var smartConfig = configuration.GetSection("Authorization:SmartOnFhir");

        // SMART capabilities from config (these are FHIR-specific, not in OIDC discovery)
        var capabilities = smartConfig.GetSection("SupportedCapabilities").Get<List<string>>() ??
        [
            "launch-ehr",
            "launch-standalone",
            "client-public",
            "client-confidential-symmetric",
            "sso-openid-connect",
            "context-ehr-patient",
            "context-standalone-patient",
            "permission-offline",
            "permission-patient",
            "permission-user"
        ];

        // SMART-specific scopes (not in standard OIDC discovery)
        var smartScopes = new List<string>
        {
            "openid",
            "fhirUser",
            "launch",
            "launch/patient",
            "patient/*.rs",
            "patient/*.cruds",
            "user/*.rs",
            "user/*.cruds",
            "system/*.rs",
            "system/*.cruds",
            "offline_access"
        };

        // Merge with any scopes from discovery
        if (discovery.ScopesSupported is not null)
        {
            foreach (var scope in discovery.ScopesSupported)
            {
                if (!smartScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                {
                    smartScopes.Add(scope);
                }
            }
        }

        return new SmartConfigurationData(
            Issuer: discovery.Issuer,
            JwksUri: discovery.JwksUri,
            AuthorizationEndpoint: discovery.AuthorizationEndpoint ?? throw new InvalidOperationException("authorization_endpoint missing from discovery"),
            TokenEndpoint: discovery.TokenEndpoint ?? throw new InvalidOperationException("token_endpoint missing from discovery"),
            IntrospectionEndpoint: discovery.IntrospectionEndpoint,
            RevocationEndpoint: discovery.RevocationEndpoint,
            GrantTypes: discovery.GrantTypesSupported?.ToList() ?? ["authorization_code", "client_credentials"],
            TokenEndpointAuthMethods: discovery.TokenEndpointAuthMethodsSupported?.ToList() ?? ["client_secret_basic", "client_secret_post"],
            TokenEndpointAuthSigningAlgs: discovery.TokenEndpointAuthSigningAlgValuesSupported?.ToList(),
            SupportedScopes: smartScopes,
            Capabilities: capabilities);
    }
}

/// <summary>
/// OIDC discovery document model (standard fields).
/// </summary>
internal sealed record OidcDiscoveryDocument
{
    public string? Issuer { get; init; }
    public string? JwksUri { get; init; }
    public string? AuthorizationEndpoint { get; init; }
    public string? TokenEndpoint { get; init; }
    public string? IntrospectionEndpoint { get; init; }
    public string? RevocationEndpoint { get; init; }
    public IEnumerable<string>? ScopesSupported { get; init; }
    public IEnumerable<string>? GrantTypesSupported { get; init; }
    public IEnumerable<string>? TokenEndpointAuthMethodsSupported { get; init; }
    public IEnumerable<string>? TokenEndpointAuthSigningAlgValuesSupported { get; init; }
}
