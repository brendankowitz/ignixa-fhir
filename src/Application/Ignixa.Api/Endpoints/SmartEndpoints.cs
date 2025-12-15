// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for SMART on FHIR discovery (/.well-known/smart-configuration).
/// Implements SMART App Launch v2.2.0 specification.
/// Supports both tenant-agnostic and tenant-explicit routes.
/// </summary>
public static class SmartEndpoints
{
    /// <summary>
    /// Static JSON serialization options to avoid creating new instance on every request.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
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
    private static async Task<IResult> HandleGetSmartConfiguration(
        HttpContext context,
        [FromServices] IOptions<SmartOptions> smartOptions,
        [FromServices] ISmartConfigurationProvider smartConfigurationProvider,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Ignixa.Api.Endpoints.SmartEndpoints");
        logger.LogInformation("GET /.well-known/smart-configuration (tenant-agnostic)");

        // Check if SMART configuration is enabled using typed options
        if (!smartOptions.Value.EnableSmartConfiguration)
        {
            logger.LogWarning("SMART configuration endpoint is disabled");
            return Results.NotFound();
        }

        var configData = await smartConfigurationProvider.GetConfigurationAsync(tenantId: null, cancellationToken);
        var smartConfig = BuildSmartConfigurationResponse(context, configData, tenantId: null);
        return Results.Json(smartConfig, options: JsonOptions);
    }

    /// <summary>
    /// GET /tenant/{tenantId}/.well-known/smart-configuration
    /// Returns the SMART on FHIR discovery document for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleGetTenantSmartConfiguration(
        HttpContext context,
        int tenantId,
        [FromServices] IOptions<SmartOptions> smartOptions,
        [FromServices] ISmartConfigurationProvider smartConfigurationProvider,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Ignixa.Api.Endpoints.SmartEndpoints");
        logger.LogInformation("GET /tenant/{TenantId}/.well-known/smart-configuration", tenantId);

        // Check if SMART configuration is enabled using typed options
        if (!smartOptions.Value.EnableSmartConfiguration)
        {
            logger.LogWarning("SMART configuration endpoint is disabled");
            return Results.NotFound();
        }

        var configData = await smartConfigurationProvider.GetConfigurationAsync(tenantId.ToString(), cancellationToken);
        var smartConfig = BuildSmartConfigurationResponse(context, configData, tenantId);
        return Results.Json(smartConfig, options: JsonOptions);
    }

    /// <summary>
    /// Builds the SMART on FHIR discovery configuration response from configuration data.
    /// </summary>
    private static SmartConfiguration BuildSmartConfigurationResponse(
        HttpContext context,
        SmartConfigurationData configData,
        int? tenantId)
    {
        return new SmartConfiguration
        {
            Issuer = configData.Issuer,
            JwksUri = configData.JwksUri,
            AuthorizationEndpoint = configData.AuthorizationEndpoint,
            TokenEndpoint = configData.TokenEndpoint,
            IntrospectionEndpoint = configData.IntrospectionEndpoint,
            RevocationEndpoint = configData.RevocationEndpoint,
            GrantTypes = configData.GrantTypes,
            TokenEndpointAuthMethods = configData.TokenEndpointAuthMethods,
            TokenEndpointAuthSigningAlgs = configData.TokenEndpointAuthSigningAlgs,
            SupportedScopes = configData.SupportedScopes,
            SupportedResponseTypes = ["code"],
            SupportedChallengeMethods = ["S256"], // MUST include S256, MUST NOT include "plain" per SMART v2
            Capabilities = configData.Capabilities
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
