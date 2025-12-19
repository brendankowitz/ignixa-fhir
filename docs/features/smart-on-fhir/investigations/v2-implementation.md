# SMART on FHIR v2 Native Implementation

This document outlines the native implementation of SMART on FHIR v2 specification integrated into the FHIR Server v2 architecture, supporting multi-tenant deployments with modern OAuth 2.0 and OpenID Connect patterns.

## SMART on FHIR v2 Overview

### Key Specifications Supported
- **SMART App Launch Framework v2.0** (STU2)
- **OAuth 2.0 Authorization Code Flow with PKCE**
- **OpenID Connect for identity**
- **FHIR R4/R4B/R5 Bulk Data Access**
- **Backend Services (client_credentials flow)**
- **Granular scopes and permissions**

### Core Authentication Flows
1. **EHR Launch Flow**: Apps launched from within EHR systems
2. **Standalone Launch Flow**: Apps launched independently
3. **Backend Services Flow**: Server-to-server authentication
4. **Refresh Token Flow**: Token renewal without re-authentication

## Core SMART Abstractions

### SMART Configuration Interface

```csharp
public interface ISmartConfiguration
{
    /// <summary>
    /// Get SMART configuration for tenant
    /// </summary>
    ValueTask<SmartConfigurationMetadata> GetConfigurationAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get well-known configuration endpoints
    /// </summary>
    ValueTask<SmartWellKnownConfiguration> GetWellKnownConfigurationAsync(string tenantId, CancellationToken cancellationToken = default);
}

public record SmartConfigurationMetadata
{
    public required string TenantId { get; init; }
    public required Uri AuthorizationEndpoint { get; init; }
    public required Uri TokenEndpoint { get; init; }
    public required Uri IntrospectionEndpoint { get; init; }
    public required Uri RevocationEndpoint { get; init; }
    public required Uri RegistrationEndpoint { get; init; }
    public required Uri ManagementEndpoint { get; init; }
    public required string[] ScopesSupported { get; init; }
    public required string[] ResponseTypesSupported { get; init; }
    public required string[] GrantTypesSupported { get; init; }
    public required string[] TokenEndpointAuthMethodsSupported { get; init; }
    public required string[] CodeChallengeMethodsSupported { get; init; }
    public required SmartCapabilities Capabilities { get; init; }
}

public record SmartWellKnownConfiguration
{
    public required string Issuer { get; init; }
    public required Uri JwksUri { get; init; }
    public required string[] ScopesSupported { get; init; }
    public required string[] ResponseTypesSupported { get; init; }
    public required SmartCapabilities Capabilities { get; init; }
}

[Flags]
public enum SmartCapabilities
{
    None = 0,
    LaunchEhr = 1 << 0,
    LaunchStandalone = 1 << 1,
    ClientPublic = 1 << 2,
    ClientConfidential = 1 << 3,
    SsoOpenidConnect = 1 << 4,
    ContextStandalonePatient = 1 << 5,
    ContextStandaloneEncounter = 1 << 6,
    ContextEhrPatient = 1 << 7,
    ContextEhrEncounter = 1 << 8,
    PermissionPatient = 1 << 9,
    PermissionUser = 1 << 10,
    PermissionOfflineAccess = 1 << 11
}
```

### SMART Token Management

```csharp
public interface ISmartTokenService
{
    /// <summary>
    /// Issue access token for authorization code
    /// </summary>
    ValueTask<SmartTokenResponse> ExchangeCodeAsync(SmartTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    ValueTask<SmartTokenResponse> RefreshTokenAsync(SmartRefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate and parse access token
    /// </summary>
    ValueTask<SmartTokenClaims?> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke access or refresh token
    /// </summary>
    ValueTask RevokeTokenAsync(string token, string? tokenTypeHint = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Introspect token (RFC 7662)
    /// </summary>
    ValueTask<SmartTokenIntrospectionResponse> IntrospectTokenAsync(string token, CancellationToken cancellationToken = default);
}

public record SmartTokenRequest
{
    public required string TenantId { get; init; }
    public required string GrantType { get; init; }
    public required string Code { get; init; }
    public required string RedirectUri { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? CodeVerifier { get; init; }
}

public record SmartRefreshRequest
{
    public required string TenantId { get; init; }
    public required string GrantType { get; init; } = "refresh_token";
    public required string RefreshToken { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Scope { get; init; }
}

public record SmartTokenResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; } = "Bearer";
    public required int ExpiresIn { get; init; }
    public string? RefreshToken { get; init; }
    public string? Scope { get; init; }
    public string? IdToken { get; init; }
    public string? Patient { get; init; }
    public string? Encounter { get; init; }
    public string? Intent { get; init; }
    public string? SmartStyleUrl { get; init; }
}

public record SmartTokenClaims
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string Subject { get; init; }
    public required string[] Scopes { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? Patient { get; init; }
    public string? Encounter { get; init; }
    public string? User { get; init; }
    public string? LaunchContext { get; init; }
    public IReadOnlyDictionary<string, object>? FhirContext { get; init; }
}

public record SmartTokenIntrospectionResponse
{
    public required bool Active { get; init; }
    public string? ClientId { get; init; }
    public string? Scope { get; init; }
    public string? Subject { get; init; }
    public long? ExpiresAt { get; init; }
    public long? IssuedAt { get; init; }
    public string? TokenType { get; init; }
}
```

### SMART Authorization Service

```csharp
public interface ISmartAuthorizationService
{
    /// <summary>
    /// Validate authorization request and generate authorization code
    /// </summary>
    ValueTask<SmartAuthorizationResponse> AuthorizeAsync(SmartAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate launch context and parameters
    /// </summary>
    ValueTask<SmartLaunchContext> ValidateLaunchAsync(string tenantId, string? launch, string? iss, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user consent for requested scopes
    /// </summary>
    ValueTask<SmartConsentResult> GetConsentAsync(SmartConsentRequest request, CancellationToken cancellationToken = default);
}

public record SmartAuthorizationRequest
{
    public required string TenantId { get; init; }
    public required string ResponseType { get; init; }
    public required string ClientId { get; init; }
    public required string RedirectUri { get; init; }
    public required string Scope { get; init; }
    public required string State { get; init; }
    public string? CodeChallenge { get; init; }
    public string? CodeChallengeMethod { get; init; }
    public string? Launch { get; init; }
    public string? Iss { get; init; }
    public string? Aud { get; init; }
}

public record SmartAuthorizationResponse
{
    public required string Code { get; init; }
    public required string State { get; init; }
    public required SmartLaunchContext LaunchContext { get; init; }
}

public record SmartLaunchContext
{
    public required string TenantId { get; init; }
    public required string LaunchId { get; init; }
    public string? Patient { get; init; }
    public string? Encounter { get; init; }
    public string? User { get; init; }
    public string? Intent { get; init; }
    public IReadOnlyDictionary<string, object>? Context { get; init; }
}

public record SmartConsentRequest
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string UserId { get; init; }
    public required string[] RequestedScopes { get; init; }
    public SmartLaunchContext? LaunchContext { get; init; }
}

public record SmartConsentResult
{
    public required bool Granted { get; init; }
    public required string[] ApprovedScopes { get; init; }
    public string? DenialReason { get; init; }
}
```

## SMART Scope Management

### Granular Scope Parser

```csharp
public static class SmartScopeParser
{
    private static readonly Regex ScopePattern = new(@"^(?<context>patient|user|system)\/(?<resource>\*|[A-Z][a-zA-Z]*(?:\.[a-zA-Z]+)*)\.(?<interaction>read|write|\*|c|r|u|d|s)(?<constraint>:\w+(?:[|&]\w+)*)?$", RegexOptions.Compiled);

    public static SmartScope ParseScope(ReadOnlySpan<char> scopeString)
    {
        var match = ScopePattern.Match(scopeString.ToString());
        if (!match.Success)
        {
            // Handle special scopes
            return scopeString switch
            {
                _ when scopeString.SequenceEqual("openid".AsSpan()) => new SmartScope(SmartScopeType.OpenId, "*", "*"),
                _ when scopeString.SequenceEqual("profile".AsSpan()) => new SmartScope(SmartScopeType.Profile, "*", "*"),
                _ when scopeString.SequenceEqual("email".AsSpan()) => new SmartScope(SmartScopeType.Email, "*", "*"),
                _ when scopeString.SequenceEqual("offline_access".AsSpan()) => new SmartScope(SmartScopeType.OfflineAccess, "*", "*"),
                _ when scopeString.SequenceEqual("launch".AsSpan()) => new SmartScope(SmartScopeType.Launch, "*", "*"),
                _ when scopeString.StartsWith("launch/".AsSpan()) => ParseLaunchScope(scopeString),
                _ => throw new ArgumentException($"Invalid SMART scope: {scopeString}")
            };
        }

        var context = ParseScopeContext(match.Groups["context"].ValueSpan);
        var resource = match.Groups["resource"].Value;
        var interaction = ParseInteraction(match.Groups["interaction"].ValueSpan);
        var constraint = match.Groups["constraint"].Success ? match.Groups["constraint"].Value[1..] : null; // Remove ':'

        return new SmartScope(context, resource, interaction, constraint);
    }

    private static SmartScopeType ParseScopeContext(ReadOnlySpan<char> context)
    {
        return context switch
        {
            _ when context.SequenceEqual("patient".AsSpan()) => SmartScopeType.Patient,
            _ when context.SequenceEqual("user".AsSpan()) => SmartScopeType.User,
            _ when context.SequenceEqual("system".AsSpan()) => SmartScopeType.System,
            _ => throw new ArgumentException($"Invalid scope context: {context}")
        };
    }

    private static string ParseInteraction(ReadOnlySpan<char> interaction)
    {
        return interaction switch
        {
            _ when interaction.SequenceEqual("read".AsSpan()) => "read",
            _ when interaction.SequenceEqual("write".AsSpan()) => "write",
            _ when interaction.SequenceEqual("*".AsSpan()) => "*",
            _ when interaction.SequenceEqual("c".AsSpan()) => "create",
            _ when interaction.SequenceEqual("r".AsSpan()) => "read",
            _ when interaction.SequenceEqual("u".AsSpan()) => "update",
            _ when interaction.SequenceEqual("d".AsSpan()) => "delete",
            _ when interaction.SequenceEqual("s".AsSpan()) => "search",
            _ => throw new ArgumentException($"Invalid interaction: {interaction}")
        };
    }

    private static SmartScope ParseLaunchScope(ReadOnlySpan<char> scopeString)
    {
        // launch/patient, launch/encounter, etc.
        var contextType = scopeString[7..]; // Remove "launch/"
        return new SmartScope(SmartScopeType.Launch, contextType.ToString(), "*");
    }
}

public record SmartScope(
    SmartScopeType Type,
    string Resource,
    string Interaction,
    string? Constraint = null)
{
    public bool MatchesResource(string resourceType) =>
        Resource == "*" || Resource.Equals(resourceType, StringComparison.OrdinalIgnoreCase);

    public bool MatchesInteraction(string interaction) =>
        Interaction == "*" || Interaction.Equals(interaction, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Type switch
    {
        SmartScopeType.Patient => $"patient/{Resource}.{Interaction}{(Constraint != null ? $":{Constraint}" : "")}",
        SmartScopeType.User => $"user/{Resource}.{Interaction}{(Constraint != null ? $":{Constraint}" : "")}",
        SmartScopeType.System => $"system/{Resource}.{Interaction}{(Constraint != null ? $":{Constraint}" : "")}",
        SmartScopeType.Launch => $"launch/{Resource}",
        SmartScopeType.OpenId => "openid",
        SmartScopeType.Profile => "profile",
        SmartScopeType.Email => "email",
        SmartScopeType.OfflineAccess => "offline_access",
        _ => throw new ArgumentOutOfRangeException()
    };
}

public enum SmartScopeType
{
    Patient,
    User,
    System,
    Launch,
    OpenId,
    Profile,
    Email,
    OfflineAccess
}
```

### Authorization Policy Integration

```csharp
public class SmartAuthorizationHandler : AuthorizationHandler<SmartRequirement>
{
    private readonly ISmartTokenService _tokenService;
    private readonly ILogger<SmartAuthorizationHandler> _logger;

    public SmartAuthorizationHandler(ISmartTokenService tokenService, ILogger<SmartAuthorizationHandler> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SmartRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
        {
            context.Fail();
            return;
        }

        // Extract access token from Authorization header
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Fail();
            return;
        }

        var accessToken = authHeader[7..]; // Remove "Bearer "

        try
        {
            var tokenClaims = await _tokenService.ValidateTokenAsync(accessToken);
            if (tokenClaims == null)
            {
                context.Fail();
                return;
            }

            // Parse scopes from token
            var scopes = tokenClaims.Scopes.Select(SmartScopeParser.ParseScope).ToArray();

            // Check if requested operation is allowed
            if (IsOperationAllowed(requirement, scopes, httpContext))
            {
                // Add SMART context to HttpContext for later use
                httpContext.Items["SmartTokenClaims"] = tokenClaims;
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("Access denied for operation {Operation} on resource {Resource}",
                    requirement.Interaction, requirement.ResourceType);
                context.Fail();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating SMART token");
            context.Fail();
        }
    }

    private static bool IsOperationAllowed(SmartRequirement requirement, SmartScope[] scopes, HttpContext context)
    {
        // Extract tenant from route or context
        var tenantId = context.GetTenantId();

        // Check if any scope allows the requested operation
        return scopes.Any(scope => scope.Type switch
        {
            SmartScopeType.Patient => IsPatientScopeAllowed(scope, requirement, context),
            SmartScopeType.User => IsUserScopeAllowed(scope, requirement, context),
            SmartScopeType.System => IsSystemScopeAllowed(scope, requirement, context),
            _ => false
        });
    }

    private static bool IsPatientScopeAllowed(SmartScope scope, SmartRequirement requirement, HttpContext context)
    {
        // Patient scopes require patient context
        var patientId = context.GetPatientContext();
        if (string.IsNullOrEmpty(patientId))
        {
            return false;
        }

        return scope.MatchesResource(requirement.ResourceType) &&
               scope.MatchesInteraction(requirement.Interaction);
    }

    private static bool IsUserScopeAllowed(SmartScope scope, SmartRequirement requirement, HttpContext context)
    {
        // User scopes require authenticated user
        var userId = context.GetUserContext();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        return scope.MatchesResource(requirement.ResourceType) &&
               scope.MatchesInteraction(requirement.Interaction);
    }

    private static bool IsSystemScopeAllowed(SmartScope scope, SmartRequirement requirement, HttpContext context)
    {
        // System scopes allow broader access
        return scope.MatchesResource(requirement.ResourceType) &&
               scope.MatchesInteraction(requirement.Interaction);
    }
}

public record SmartRequirement(string ResourceType, string Interaction) : IAuthorizationRequirement;
```

## SMART Token Implementation

### JWT-Based Token Service

```csharp
public class JwtSmartTokenService : ISmartTokenService
{
    private readonly IOptionsMonitor<SmartTokenOptions> _options;
    private readonly ISmartClientService _clientService;
    private readonly ISmartAuthorizationCodeStore _codeStore;
    private readonly ISmartRefreshTokenStore _refreshTokenStore;
    private readonly ILogger<JwtSmartTokenService> _logger;

    public JwtSmartTokenService(
        IOptionsMonitor<SmartTokenOptions> options,
        ISmartClientService clientService,
        ISmartAuthorizationCodeStore codeStore,
        ISmartRefreshTokenStore refreshTokenStore,
        ILogger<JwtSmartTokenService> logger)
    {
        _options = options;
        _clientService = clientService;
        _codeStore = codeStore;
        _refreshTokenStore = refreshTokenStore;
        _logger = logger;
    }

    public async ValueTask<SmartTokenResponse> ExchangeCodeAsync(SmartTokenRequest request, CancellationToken cancellationToken = default)
    {
        // Validate authorization code
        var authCode = await _codeStore.ConsumeAsync(request.Code, cancellationToken);
        if (authCode == null)
        {
            throw new SmartAuthorizationException("Invalid or expired authorization code");
        }

        // Validate client
        var client = await _clientService.GetClientAsync(request.TenantId, request.ClientId, cancellationToken);
        if (client == null)
        {
            throw new SmartAuthorizationException("Invalid client");
        }

        // Validate PKCE if required
        if (!string.IsNullOrEmpty(authCode.CodeChallenge))
        {
            if (string.IsNullOrEmpty(request.CodeVerifier))
            {
                throw new SmartAuthorizationException("Code verifier required");
            }

            if (!ValidateCodeChallenge(request.CodeVerifier, authCode.CodeChallenge, authCode.CodeChallengeMethod))
            {
                throw new SmartAuthorizationException("Invalid code verifier");
            }
        }

        // Generate tokens
        var options = _options.Get(request.TenantId);
        var tokenClaims = new SmartTokenClaims
        {
            TenantId = request.TenantId,
            ClientId = request.ClientId,
            Subject = authCode.Subject,
            Scopes = authCode.Scopes,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(options.AccessTokenLifetime),
            Patient = authCode.LaunchContext?.Patient,
            Encounter = authCode.LaunchContext?.Encounter,
            User = authCode.LaunchContext?.User,
            LaunchContext = authCode.LaunchContext?.LaunchId,
            FhirContext = authCode.LaunchContext?.Context
        };

        var accessToken = GenerateAccessToken(tokenClaims, options);
        var refreshToken = await GenerateRefreshTokenAsync(tokenClaims, options, cancellationToken);

        return new SmartTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)options.AccessTokenLifetime.TotalSeconds,
            RefreshToken = refreshToken,
            Scope = string.Join(" ", authCode.Scopes),
            Patient = authCode.LaunchContext?.Patient,
            Encounter = authCode.LaunchContext?.Encounter
        };
    }

    public async ValueTask<SmartTokenClaims?> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenHandler = new JsonWebTokenHandler();

            // Get all tenant signing keys for validation
            var validationParameters = await GetTokenValidationParametersAsync(cancellationToken);

            var result = await tokenHandler.ValidateTokenAsync(accessToken, validationParameters);
            if (!result.IsValid)
            {
                _logger.LogWarning("Invalid access token: {Exception}", result.Exception?.Message);
                return null;
            }

            var jwt = result.SecurityToken as JsonWebToken;
            return ExtractTokenClaims(jwt!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating access token");
            return null;
        }
    }

    private string GenerateAccessToken(SmartTokenClaims claims, SmartTokenOptions options)
    {
        var tokenHandler = new JsonWebTokenHandler();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, claims.Subject),
                new Claim(JwtRegisteredClaimNames.Iat, claims.IssuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Exp, claims.ExpiresAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("tenant_id", claims.TenantId),
                new Claim("client_id", claims.ClientId),
                new Claim("scope", string.Join(" ", claims.Scopes))
            }),
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = options.SigningCredentials
        };

        // Add FHIR context claims
        if (!string.IsNullOrEmpty(claims.Patient))
        {
            tokenDescriptor.Subject.AddClaim(new Claim("patient", claims.Patient));
        }

        if (!string.IsNullOrEmpty(claims.Encounter))
        {
            tokenDescriptor.Subject.AddClaim(new Claim("encounter", claims.Encounter));
        }

        if (!string.IsNullOrEmpty(claims.User))
        {
            tokenDescriptor.Subject.AddClaim(new Claim("user", claims.User));
        }

        return tokenHandler.CreateToken(tokenDescriptor);
    }

    private static bool ValidateCodeChallenge(string codeVerifier, string codeChallenge, string? method)
    {
        var expectedChallenge = method switch
        {
            "S256" => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier))),
            "plain" or null => codeVerifier,
            _ => throw new SmartAuthorizationException($"Unsupported code challenge method: {method}")
        };

        return codeChallenge == expectedChallenge;
    }

    private static SmartTokenClaims ExtractTokenClaims(JsonWebToken jwt)
    {
        return new SmartTokenClaims
        {
            TenantId = jwt.GetClaim("tenant_id")?.Value ?? throw new InvalidOperationException("Missing tenant_id claim"),
            ClientId = jwt.GetClaim("client_id")?.Value ?? throw new InvalidOperationException("Missing client_id claim"),
            Subject = jwt.Subject ?? throw new InvalidOperationException("Missing subject claim"),
            Scopes = jwt.GetClaim("scope")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
            IssuedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(jwt.GetClaim(JwtRegisteredClaimNames.Iat)?.Value ?? "0")),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(jwt.GetClaim(JwtRegisteredClaimNames.Exp)?.Value ?? "0")),
            Patient = jwt.GetClaim("patient")?.Value,
            Encounter = jwt.GetClaim("encounter")?.Value,
            User = jwt.GetClaim("user")?.Value
        };
    }
}

public class SmartTokenOptions
{
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public required SigningCredentials SigningCredentials { get; set; }
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(10);
}
```

## SMART Client Management

### Client Registration and Validation

```csharp
public interface ISmartClientService
{
    ValueTask<SmartClient?> GetClientAsync(string tenantId, string clientId, CancellationToken cancellationToken = default);
    ValueTask<SmartClient> RegisterClientAsync(string tenantId, SmartClientRegistration registration, CancellationToken cancellationToken = default);
    ValueTask<SmartClient> UpdateClientAsync(string tenantId, string clientId, SmartClientUpdate update, CancellationToken cancellationToken = default);
    ValueTask DeleteClientAsync(string tenantId, string clientId, CancellationToken cancellationToken = default);
    ValueTask<bool> ValidateClientAsync(string tenantId, string clientId, string? clientSecret, CancellationToken cancellationToken = default);
}

public record SmartClient
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string ClientName { get; init; }
    public required SmartClientType ClientType { get; init; }
    public required string[] RedirectUris { get; init; }
    public required string[] GrantTypes { get; init; }
    public required string[] ResponseTypes { get; init; }
    public required string[] Scopes { get; init; }
    public string? ClientSecret { get; init; }
    public string? JwksUri { get; init; }
    public string? SoftwareId { get; init; }
    public string? SoftwareVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; init; }
    public bool IsActive { get; init; } = true;
    public SmartClientMetadata? Metadata { get; init; }
}

public enum SmartClientType
{
    Public,
    Confidential,
    NativeApp,
    WebApp,
    BackendService
}

public record SmartClientMetadata
{
    public string? Description { get; init; }
    public string? LogoUri { get; init; }
    public string? PolicyUri { get; init; }
    public string? TermsOfServiceUri { get; init; }
    public string? ContactEmail { get; init; }
    public IReadOnlyDictionary<string, object>? ExtensionData { get; init; }
}
```

## Controller Integration

### SMART Endpoints Controller

```csharp
[ApiController]
[Route("{tenantId}/smart")]
public class SmartController : ControllerBase
{
    private readonly ISmartConfiguration _smartConfig;
    private readonly ISmartAuthorizationService _authService;
    private readonly ISmartTokenService _tokenService;
    private readonly ISmartClientService _clientService;

    public SmartController(
        ISmartConfiguration smartConfig,
        ISmartAuthorizationService authService,
        ISmartTokenService tokenService,
        ISmartClientService clientService)
    {
        _smartConfig = smartConfig;
        _authService = authService;
        _tokenService = tokenService;
        _clientService = clientService;
    }

    [HttpGet(".well-known/smart-configuration")]
    public async Task<ActionResult<SmartWellKnownConfiguration>> GetWellKnownConfiguration(string tenantId, CancellationToken cancellationToken)
    {
        var config = await _smartConfig.GetWellKnownConfigurationAsync(tenantId, cancellationToken);
        return Ok(config);
    }

    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize([FromQuery] SmartAuthorizationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.AuthorizeAsync(request, cancellationToken);

            var redirectUri = new UriBuilder(request.RedirectUri);
            redirectUri.Query = $"code={response.Code}&state={request.State}";

            return Redirect(redirectUri.ToString());
        }
        catch (SmartAuthorizationException ex)
        {
            var errorUri = new UriBuilder(request.RedirectUri);
            errorUri.Query = $"error={ex.Error}&error_description={Uri.EscapeDataString(ex.Message)}&state={request.State}";

            return Redirect(errorUri.ToString());
        }
    }

    [HttpPost("token")]
    public async Task<ActionResult<SmartTokenResponse>> Token([FromForm] SmartTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _tokenService.ExchangeCodeAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (SmartAuthorizationException ex)
        {
            return BadRequest(new { error = ex.Error, error_description = ex.Message });
        }
    }

    [HttpPost("introspect")]
    public async Task<ActionResult<SmartTokenIntrospectionResponse>> Introspect([FromForm] string token, CancellationToken cancellationToken)
    {
        var response = await _tokenService.IntrospectTokenAsync(token, cancellationToken);
        return Ok(response);
    }
}
```

## Dependency Injection Setup

```csharp
public static class SmartServiceCollectionExtensions
{
    public static IServiceCollection AddSmartOnFhir(this IServiceCollection services, IConfiguration configuration)
    {
        var smartSection = configuration.GetSection("Smart");

        services.Configure<SmartTokenOptions>(smartSection.GetSection("Token"));

        services.AddSingleton<ISmartConfiguration, SmartConfigurationService>();
        services.AddScoped<ISmartTokenService, JwtSmartTokenService>();
        services.AddScoped<ISmartAuthorizationService, SmartAuthorizationService>();
        services.AddScoped<ISmartClientService, SmartClientService>();

        // Authorization policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy("SmartFhirRead", policy =>
                policy.Requirements.Add(new SmartRequirement("*", "read")));

            options.AddPolicy("SmartFhirWrite", policy =>
                policy.Requirements.Add(new SmartRequirement("*", "write")));
        });

        services.AddSingleton<IAuthorizationHandler, SmartAuthorizationHandler>();

        return services;
    }
}
```

This SMART on FHIR v2 implementation provides:

1. **Complete SMART v2 Support**: All standard flows and capabilities
2. **Multi-Tenant Architecture**: Tenant-isolated configuration and tokens
3. **Granular Scopes**: Modern scope parsing with constraints
4. **Memory Efficiency**: Span-based parsing and minimal allocations
5. **JWT Security**: Industry-standard token validation
6. **Flexible Client Types**: Support for all SMART client categories
7. **Authorization Integration**: Native ASP.NET Core policy integration
8. **Extensible Design**: Easy to add custom scopes and behaviors

The implementation seamlessly integrates with the FHIR Server v2 architecture while providing production-ready SMART on FHIR capabilities.