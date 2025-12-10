// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for SMART on FHIR discovery (/.well-known/smart-configuration).
/// Implements SMART App Launch v2.2.0 specification.
/// Supports both tenant-agnostic and tenant-explicit routes.
/// </summary>
public static class SmartEndpoints
{
    public static IEndpointRouteBuilder MapSmartDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSmartDiscoveryTenantEndpoints();
        endpoints.MapSmartDiscoveryAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit SMART discovery endpoints (/tenant/{tenantId}/.well-known/smart-configuration).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapSmartDiscoveryTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/tenant/{tenantId:int}/.well-known/smart-configuration", HandleGetTenantSmartConfiguration)
            .WithName("GetTenantSmartConfiguration")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic SMART discovery endpoints (/.well-known/smart-configuration).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapSmartDiscoveryAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/smart-configuration", HandleGetSmartConfiguration)
            .WithName("GetSmartConfiguration")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// GET /.well-known/smart-configuration
    /// Returns the SMART on FHIR discovery document (tenant-agnostic).
    /// </summary>
    private static IResult HandleGetSmartConfiguration(
        HttpContext context,
        [FromServices] IConfiguration configuration,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Ignixa.Api.Endpoints.SmartEndpoints");
        logger.LogInformation("GET /.well-known/smart-configuration (tenant-agnostic)");

        // Check if SMART configuration is enabled
        var smartEnabled = configuration.GetValue<bool>("Authorization:SmartOnFhir:EnableSmartConfiguration", true);
        if (!smartEnabled)
        {
            logger.LogWarning("SMART configuration endpoint is disabled");
            return Results.NotFound();
        }

        var smartConfig = BuildSmartConfiguration(context, configuration, tenantId: null, logger);
        return Results.Json(smartConfig, options: new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        });
    }

    /// <summary>
    /// GET /tenant/{tenantId}/.well-known/smart-configuration
    /// Returns the SMART on FHIR discovery document for a specific tenant.
    /// </summary>
    private static IResult HandleGetTenantSmartConfiguration(
        HttpContext context,
        int tenantId,
        [FromServices] IConfiguration configuration,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Ignixa.Api.Endpoints.SmartEndpoints");
        logger.LogInformation("GET /tenant/{TenantId}/.well-known/smart-configuration", tenantId);

        // Check if SMART configuration is enabled
        var smartEnabled = configuration.GetValue<bool>("Authorization:SmartOnFhir:EnableSmartConfiguration", true);
        if (!smartEnabled)
        {
            logger.LogWarning("SMART configuration endpoint is disabled");
            return Results.NotFound();
        }

        var smartConfig = BuildSmartConfiguration(context, configuration, tenantId, logger);
        return Results.Json(smartConfig, options: new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Builds the SMART on FHIR discovery configuration based on appsettings.json provider configuration.
    /// </summary>
    private static SmartConfiguration BuildSmartConfiguration(
        HttpContext context,
        IConfiguration configuration,
        int? tenantId,
        ILogger logger)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var tenantPrefix = tenantId.HasValue ? $"/tenant/{tenantId}" : string.Empty;

        // Determine OAuth endpoints based on configured provider
        var authConfig = configuration.GetSection("Authentication");
        var provider = authConfig["Provider"] ?? "JwtBearer";

        string authorizationEndpoint;
        string tokenEndpoint;
        string? introspectionEndpoint = null;
        string? revocationEndpoint = null;
        string? issuer = null;
        string? jwksUri = null;

        switch (provider)
        {
            case "OpenIddict":
                // OpenIddict for local development
                var openIddictConfig = authConfig.GetSection("OpenIddict");
                var openIddictIssuer = openIddictConfig["Issuer"] ?? baseUrl;
                issuer = openIddictIssuer;
                authorizationEndpoint = $"{openIddictIssuer}/connect/authorize";
                tokenEndpoint = $"{openIddictIssuer}/connect/token";
                introspectionEndpoint = $"{openIddictIssuer}/connect/introspect";
                revocationEndpoint = $"{openIddictIssuer}/connect/revoke";
                jwksUri = $"{openIddictIssuer}/.well-known/jwks";
                break;

            case "Entra":
                // Azure Entra ID (formerly Azure AD)
                var entraConfig = authConfig.GetSection("Entra");
                var entraInstance = entraConfig["Instance"] ?? "https://login.microsoftonline.com/";
                var entraTenantId = entraConfig["TenantId"] ?? "common";
                var entraAuthority = $"{entraInstance}{entraTenantId}/v2.0";
                issuer = entraAuthority;
                authorizationEndpoint = $"{entraAuthority}/authorize";
                tokenEndpoint = $"{entraAuthority}/token";
                jwksUri = $"{entraAuthority}/discovery/keys";
                // Entra supports introspection but requires different endpoint patterns
                break;

            case "Okta":
                // Okta configuration
                var oktaConfig = authConfig.GetSection("Okta");
                var oktaDomain = oktaConfig["Domain"] ?? "your-okta-domain.okta.com";
                var oktaAuthority = $"https://{oktaDomain}";
                issuer = oktaAuthority;
                authorizationEndpoint = $"{oktaAuthority}/oauth2/v1/authorize";
                tokenEndpoint = $"{oktaAuthority}/oauth2/v1/token";
                introspectionEndpoint = $"{oktaAuthority}/oauth2/v1/introspect";
                revocationEndpoint = $"{oktaAuthority}/oauth2/v1/revoke";
                jwksUri = $"{oktaAuthority}/oauth2/v1/keys";
                break;

            case "OIDC":
                // Generic OpenID Connect provider
                var oidcConfig = authConfig.GetSection("OIDC");
                var oidcAuthority = oidcConfig["Authority"] ?? baseUrl;
                issuer = oidcAuthority;
                authorizationEndpoint = $"{oidcAuthority}/authorize";
                tokenEndpoint = $"{oidcAuthority}/token";
                jwksUri = $"{oidcAuthority}/.well-known/jwks.json";
                // Generic OIDC may support introspection/revocation - check provider docs
                break;

            case "JwtBearer":
            default:
                // Generic JWT Bearer - use base configuration
                var authority = authConfig["Authority"] ?? baseUrl;
                issuer = authority;
                authorizationEndpoint = $"{authority}/authorize";
                tokenEndpoint = $"{authority}/token";
                jwksUri = $"{authority}/.well-known/jwks.json";
                break;
        }

        // Read SMART capabilities from configuration
        var smartConfig = configuration.GetSection("Authorization:SmartOnFhir");
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

        // SMART v2 scopes (examples - customize based on server capabilities)
        var supportedScopes = new List<string>
        {
            "openid",
            "fhirUser",
            "launch",
            "launch/patient",
            "patient/*.read",
            "patient/*.write",
            "patient/*.*",
            "user/*.read",
            "user/*.write",
            "user/*.*",
            "offline_access",
            "online_access"
        };

        // Add SMART v1 compatibility scopes if enabled
        var enableV1Compatibility = smartConfig.GetValue<bool>("EnableV1ScopeCompatibility", false);
        if (enableV1Compatibility)
        {
            supportedScopes.AddRange([
                "patient/Patient.read",
                "patient/Observation.read",
                "user/Patient.read",
                "user/Observation.read"
            ]);
        }

        return new SmartConfiguration
        {
            Issuer = issuer,
            JwksUri = jwksUri,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            IntrospectionEndpoint = introspectionEndpoint,
            RevocationEndpoint = revocationEndpoint,
            GrantTypes = ["authorization_code", "client_credentials"],
            TokenEndpointAuthMethods = ["client_secret_basic", "client_secret_post", "private_key_jwt"],
            TokenEndpointAuthSigningAlgs = ["RS256", "RS384", "RS512", "ES256", "ES384", "ES512"],
            SupportedScopes = supportedScopes,
            SupportedResponseTypes = ["code"],
            SupportedChallengeMethods = ["S256"], // MUST include S256, MUST NOT include "plain" per SMART v2
            Capabilities = capabilities
        };
    }
}

/// <summary>
/// SMART on FHIR discovery configuration response model.
/// Implements SMART App Launch v2.2.0 specification.
/// </summary>
internal sealed record SmartConfiguration
{
    /// <summary>
    /// The OAuth2 issuer URL (optional, required for OpenID Connect).
    /// </summary>
    [JsonPropertyName("issuer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Issuer { get; init; }

    /// <summary>
    /// The JSON Web Key Set URL (optional, required for OpenID Connect).
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JwksUri { get; init; }

    /// <summary>
    /// The OAuth2 authorization endpoint URL.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    /// <summary>
    /// The OAuth2 token endpoint URL.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// The token introspection endpoint URL (optional).
    /// </summary>
    [JsonPropertyName("introspection_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntrospectionEndpoint { get; init; }

    /// <summary>
    /// The token revocation endpoint URL (optional).
    /// </summary>
    [JsonPropertyName("revocation_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RevocationEndpoint { get; init; }

    /// <summary>
    /// Array of grant types supported at the token endpoint.
    /// Examples: "authorization_code", "client_credentials".
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    public required IEnumerable<string> GrantTypes { get; init; }

    /// <summary>
    /// Array of client authentication methods supported by the token endpoint.
    /// Examples: "client_secret_basic", "client_secret_post", "private_key_jwt".
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public required IEnumerable<string> TokenEndpointAuthMethods { get; init; }

    /// <summary>
    /// Array of token endpoint authentication signing algorithms supported (optional).
    /// Examples: "RS256", "ES256".
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? TokenEndpointAuthSigningAlgs { get; init; }

    /// <summary>
    /// Array of scopes supported by the server.
    /// Examples: "openid", "fhirUser", "launch", "patient/*.read", "user/*.write".
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public required IEnumerable<string> SupportedScopes { get; init; }

    /// <summary>
    /// Array of OAuth2 response_type values supported.
    /// Typically includes "code" for authorization code flow.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public required IEnumerable<string> SupportedResponseTypes { get; init; }

    /// <summary>
    /// Array of PKCE code challenge methods supported.
    /// MUST include "S256", MUST NOT include "plain" per SMART v2.
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public required IEnumerable<string> SupportedChallengeMethods { get; init; }

    /// <summary>
    /// Array of SMART capabilities supported by the server.
    /// Examples: "launch-ehr", "launch-standalone", "client-public", "sso-openid-connect".
    /// </summary>
    [JsonPropertyName("capabilities")]
    public required IEnumerable<string> Capabilities { get; init; }
}
