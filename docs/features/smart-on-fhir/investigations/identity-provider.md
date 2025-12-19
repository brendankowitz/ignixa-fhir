# SMART on FHIR Identity Provider Abstraction

This document outlines the provider-agnostic identity abstraction for SMART on FHIR v2, enabling integration with multiple identity providers like Entra ID, Azure B2C, Auth0, Okta, and custom implementations.

## Identity Provider Abstraction Layer

### Core Identity Provider Interface

```csharp
public interface ISmartIdentityProvider
{
    /// <summary>
    /// Provider identifier (e.g., "entra", "b2c", "auth0")
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Get provider-specific configuration
    /// </summary>
    ValueTask<IdentityProviderConfiguration> GetConfigurationAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Build authorization URL for SMART launch
    /// </summary>
    ValueTask<Uri> BuildAuthorizationUrlAsync(SmartAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchange authorization code for tokens
    /// </summary>
    ValueTask<IdentityProviderTokenResponse> ExchangeCodeAsync(IdentityProviderTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate and decode access token
    /// </summary>
    ValueTask<IdentityProviderTokenClaims?> ValidateTokenAsync(string accessToken, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh access token
    /// </summary>
    ValueTask<IdentityProviderTokenResponse> RefreshTokenAsync(IdentityProviderRefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke token
    /// </summary>
    ValueTask RevokeTokenAsync(string token, string tenantId, string? tokenTypeHint = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user information from token or API
    /// </summary>
    ValueTask<IdentityProviderUserInfo?> GetUserInfoAsync(string accessToken, string tenantId, CancellationToken cancellationToken = default);
}

public interface ISmartIdentityProviderFactory
{
    /// <summary>
    /// Create identity provider instance for tenant
    /// </summary>
    ISmartIdentityProvider CreateProvider(string tenantId, string providerId);

    /// <summary>
    /// Get available providers for tenant
    /// </summary>
    IReadOnlyList<string> GetAvailableProviders(string tenantId);
}
```

### Identity Provider Models

```csharp
public record IdentityProviderConfiguration
{
    public required string ProviderId { get; init; }
    public required string TenantId { get; init; }
    public required Uri AuthorizationEndpoint { get; init; }
    public required Uri TokenEndpoint { get; init; }
    public required Uri UserInfoEndpoint { get; init; }
    public required Uri JwksUri { get; init; }
    public required string Issuer { get; init; }
    public required string[] ScopesSupported { get; init; }
    public required string[] ResponseTypesSupported { get; init; }
    public required string[] GrantTypesSupported { get; init; }
    public required string[] TokenEndpointAuthMethodsSupported { get; init; }
    public IReadOnlyDictionary<string, object>? ProviderSpecificSettings { get; init; }
}

public record IdentityProviderTokenRequest
{
    public required string TenantId { get; init; }
    public required string ProviderId { get; init; }
    public required string GrantType { get; init; }
    public required string Code { get; init; }
    public required string RedirectUri { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? CodeVerifier { get; init; }
    public string? Scope { get; init; }
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }
}

public record IdentityProviderRefreshRequest
{
    public required string TenantId { get; init; }
    public required string ProviderId { get; init; }
    public required string RefreshToken { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Scope { get; init; }
}

public record IdentityProviderTokenResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public required int ExpiresIn { get; init; }
    public string? RefreshToken { get; init; }
    public string? Scope { get; init; }
    public string? IdToken { get; init; }
    public IReadOnlyDictionary<string, object>? AdditionalClaims { get; init; }
}

public record IdentityProviderTokenClaims
{
    public required string Subject { get; init; }
    public required string Issuer { get; init; }
    public required string[] Audience { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string[] Scopes { get; init; }
    public string? ClientId { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? PreferredUsername { get; init; }
    public IReadOnlyDictionary<string, object>? CustomClaims { get; init; }
}

public record IdentityProviderUserInfo
{
    public required string Subject { get; init; }
    public string? Email { get; init; }
    public bool EmailVerified { get; init; }
    public string? Name { get; init; }
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
    public string? PreferredUsername { get; init; }
    public string? Picture { get; init; }
    public IReadOnlyDictionary<string, object>? AdditionalInfo { get; init; }
}
```

## Entra ID Implementation

### Microsoft Entra ID Provider

```csharp
public class EntraIdSmartIdentityProvider : ISmartIdentityProvider
{
    private readonly IConfidentialClientApplication _clientApp;
    private readonly EntraIdProviderOptions _options;
    private readonly ILogger<EntraIdSmartIdentityProvider> _logger;

    public string ProviderId => "entra";

    public EntraIdSmartIdentityProvider(
        IConfidentialClientApplication clientApp,
        IOptions<EntraIdProviderOptions> options,
        ILogger<EntraIdSmartIdentityProvider> logger)
    {
        _clientApp = clientApp;
        _options = options.Value;
        _logger = logger;
    }

    public ValueTask<IdentityProviderConfiguration> GetConfigurationAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var tenantSpecificAuthority = $"https://login.microsoftonline.com/{tenantId}";

        var config = new IdentityProviderConfiguration
        {
            ProviderId = ProviderId,
            TenantId = tenantId,
            AuthorizationEndpoint = new Uri($"{tenantSpecificAuthority}/oauth2/v2.0/authorize"),
            TokenEndpoint = new Uri($"{tenantSpecificAuthority}/oauth2/v2.0/token"),
            UserInfoEndpoint = new Uri("https://graph.microsoft.com/oidc/userinfo"),
            JwksUri = new Uri($"{tenantSpecificAuthority}/discovery/v2.0/keys"),
            Issuer = $"{tenantSpecificAuthority}/v2.0",
            ScopesSupported = new[] { "openid", "profile", "email", "offline_access", "https://graph.microsoft.com/.default" },
            ResponseTypesSupported = new[] { "code" },
            GrantTypesSupported = new[] { "authorization_code", "refresh_token", "client_credentials" },
            TokenEndpointAuthMethodsSupported = new[] { "client_secret_post", "client_secret_basic", "private_key_jwt" },
            ProviderSpecificSettings = new Dictionary<string, object>
            {
                ["authority"] = tenantSpecificAuthority,
                ["instance"] = "https://login.microsoftonline.com",
                ["tenant_id"] = tenantId
            }
        };

        return ValueTask.FromResult(config);
    }

    public ValueTask<Uri> BuildAuthorizationUrlAsync(SmartAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var authorizeUriBuilder = _clientApp
            .GetAuthorizationRequestUrl(request.Scope.Split(' '))
            .WithRedirectUri(request.RedirectUri)
            .WithState(request.State);

        // Add PKCE if provided
        if (!string.IsNullOrEmpty(request.CodeChallenge))
        {
            authorizeUriBuilder = authorizeUriBuilder
                .WithPkceCodeChallenge(request.CodeChallenge, request.CodeChallengeMethod ?? "S256");
        }

        // Add SMART-specific parameters
        var additionalParams = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(request.Launch))
        {
            additionalParams["launch"] = request.Launch;
        }

        if (!string.IsNullOrEmpty(request.Iss))
        {
            additionalParams["iss"] = request.Iss;
        }

        if (!string.IsNullOrEmpty(request.Aud))
        {
            additionalParams["aud"] = request.Aud;
        }

        var authUri = authorizeUriBuilder.ExecuteAsync(cancellationToken: cancellationToken).Result;

        // Append additional parameters manually if needed
        if (additionalParams.Count > 0)
        {
            var uriBuilder = new UriBuilder(authUri);
            var queryParams = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

            foreach (var kvp in additionalParams)
            {
                queryParams[kvp.Key] = kvp.Value;
            }

            uriBuilder.Query = queryParams.ToString();
            authUri = uriBuilder.Uri;
        }

        return ValueTask.FromResult(authUri);
    }

    public async ValueTask<IdentityProviderTokenResponse> ExchangeCodeAsync(IdentityProviderTokenRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var authCodeProviderBuilder = _clientApp
                .AcquireTokenByAuthorizationCode(request.Scope?.Split(' ') ?? Array.Empty<string>(), request.Code);

            if (!string.IsNullOrEmpty(request.CodeVerifier))
            {
                authCodeProviderBuilder = authCodeProviderBuilder.WithPkceCodeVerifier(request.CodeVerifier);
            }

            var result = await authCodeProviderBuilder.ExecuteAsync(cancellationToken);

            return new IdentityProviderTokenResponse
            {
                AccessToken = result.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = (int)(result.ExpiresOn - DateTimeOffset.UtcNow).TotalSeconds,
                RefreshToken = result.RefreshToken,
                Scope = string.Join(" ", result.Scopes),
                IdToken = result.IdToken,
                AdditionalClaims = ExtractCustomClaims(result)
            };
        }
        catch (MsalException ex)
        {
            _logger.LogError(ex, "Failed to exchange authorization code for Entra ID tenant {TenantId}", request.TenantId);
            throw new SmartAuthorizationException($"Token exchange failed: {ex.ErrorCode}", ex);
        }
    }

    public async ValueTask<IdentityProviderTokenClaims?> ValidateTokenAsync(string accessToken, string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenHandler = new JsonWebTokenHandler();

            // Get validation parameters for this tenant
            var validationParameters = await GetTokenValidationParametersAsync(tenantId, cancellationToken);

            var result = await tokenHandler.ValidateTokenAsync(accessToken, validationParameters);

            if (!result.IsValid)
            {
                _logger.LogWarning("Invalid Entra ID token for tenant {TenantId}: {Error}", tenantId, result.Exception?.Message);
                return null;
            }

            var jwt = result.SecurityToken as JsonWebToken;
            return ExtractTokenClaims(jwt!, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Entra ID token for tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async ValueTask<IdentityProviderTokenResponse> RefreshTokenAsync(IdentityProviderRefreshRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var accounts = await _clientApp.GetAccountsAsync();
            var account = accounts.FirstOrDefault(a => a.HomeAccountId.TenantId == request.TenantId);

            if (account == null)
            {
                throw new SmartAuthorizationException("No account found for refresh");
            }

            var result = await _clientApp
                .AcquireTokenSilent(request.Scope?.Split(' ') ?? Array.Empty<string>(), account)
                .ExecuteAsync(cancellationToken);

            return new IdentityProviderTokenResponse
            {
                AccessToken = result.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = (int)(result.ExpiresOn - DateTimeOffset.UtcNow).TotalSeconds,
                RefreshToken = result.RefreshToken,
                Scope = string.Join(" ", result.Scopes)
            };
        }
        catch (MsalException ex)
        {
            _logger.LogError(ex, "Failed to refresh token for Entra ID tenant {TenantId}", request.TenantId);
            throw new SmartAuthorizationException($"Token refresh failed: {ex.ErrorCode}", ex);
        }
    }

    public async ValueTask RevokeTokenAsync(string token, string tenantId, string? tokenTypeHint = null, CancellationToken cancellationToken = default)
    {
        // Entra ID doesn't have a standard revocation endpoint
        // Tokens are revoked by clearing the token cache
        try
        {
            var accounts = await _clientApp.GetAccountsAsync();
            var account = accounts.FirstOrDefault(a => a.HomeAccountId.TenantId == tenantId);

            if (account != null)
            {
                await _clientApp.RemoveAsync(account);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke token for Entra ID tenant {TenantId}", tenantId);
        }
    }

    public async ValueTask<IdentityProviderUserInfo?> GetUserInfoAsync(string accessToken, string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use Microsoft Graph to get user info
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync("https://graph.microsoft.com/v1.0/me", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var userJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var userInfo = JsonSerializer.Deserialize<JsonElement>(userJson);

            return new IdentityProviderUserInfo
            {
                Subject = userInfo.GetProperty("id").GetString()!,
                Email = userInfo.TryGetProperty("mail", out var email) ? email.GetString() :
                        userInfo.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null,
                EmailVerified = true, // Entra ID emails are verified
                Name = userInfo.TryGetProperty("displayName", out var name) ? name.GetString() : null,
                GivenName = userInfo.TryGetProperty("givenName", out var given) ? given.GetString() : null,
                FamilyName = userInfo.TryGetProperty("surname", out var surname) ? surname.GetString() : null,
                PreferredUsername = userInfo.TryGetProperty("userPrincipalName", out var username) ? username.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info from Microsoft Graph for tenant {TenantId}", tenantId);
            return null;
        }
    }

    private async ValueTask<TokenValidationParameters> GetTokenValidationParametersAsync(string tenantId, CancellationToken cancellationToken)
    {
        var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid_configuration",
            new OpenIdConnectConfigurationRetriever());

        var config = await configManager.GetConfigurationAsync(cancellationToken);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.Issuer,
            ValidateAudience = true,
            ValidAudiences = new[] { _options.ClientId, $"api://{_options.ClientId}" },
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    }

    private static IdentityProviderTokenClaims ExtractTokenClaims(JsonWebToken jwt, string tenantId)
    {
        var scopes = jwt.GetClaim("scp")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ??
                    jwt.GetClaim("scope")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ??
                    Array.Empty<string>();

        var customClaims = new Dictionary<string, object>();
        foreach (var claim in jwt.Claims)
        {
            if (!IsStandardClaim(claim.Type))
            {
                customClaims[claim.Type] = claim.Value;
            }
        }

        return new IdentityProviderTokenClaims
        {
            Subject = jwt.Subject!,
            Issuer = jwt.Issuer!,
            Audience = jwt.Audiences.ToArray(),
            IssuedAt = DateTimeOffset.FromUnixTimeSeconds(jwt.IssuedAt ?? 0),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(jwt.ValidTo ?? 0),
            Scopes = scopes,
            ClientId = jwt.GetClaim("appid")?.Value ?? jwt.GetClaim("azp")?.Value,
            Email = jwt.GetClaim("email")?.Value ?? jwt.GetClaim("unique_name")?.Value,
            Name = jwt.GetClaim("name")?.Value,
            PreferredUsername = jwt.GetClaim("preferred_username")?.Value,
            CustomClaims = customClaims
        };
    }

    private static bool IsStandardClaim(string claimType) => claimType switch
    {
        "sub" or "iss" or "aud" or "iat" or "exp" or "nbf" or "email" or "name" or "preferred_username" or "scp" or "scope" => true,
        _ => false
    };

    private static IReadOnlyDictionary<string, object>? ExtractCustomClaims(AuthenticationResult result)
    {
        var customClaims = new Dictionary<string, object>();

        if (result.Account?.Username != null)
        {
            customClaims["username"] = result.Account.Username;
        }

        if (result.TenantId != null)
        {
            customClaims["tenant_id"] = result.TenantId;
        }

        return customClaims.Count > 0 ? customClaims : null;
    }
}

public class EntraIdProviderOptions
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public string Authority { get; set; } = "https://login.microsoftonline.com";
    public string[] DefaultScopes { get; set; } = { "openid", "profile", "email" };
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public TimeSpan TokenCacheExpiration { get; set; } = TimeSpan.FromHours(1);
}
```

## Azure B2C Implementation

### Azure B2C Provider

```csharp
public class AzureB2CSmartIdentityProvider : ISmartIdentityProvider
{
    private readonly IConfidentialClientApplication _clientApp;
    private readonly AzureB2CProviderOptions _options;
    private readonly ILogger<AzureB2CSmartIdentityProvider> _logger;

    public string ProviderId => "b2c";

    public AzureB2CSmartIdentityProvider(
        IConfidentialClientApplication clientApp,
        IOptions<AzureB2CProviderOptions> options,
        ILogger<AzureB2CSmartIdentityProvider> logger)
    {
        _clientApp = clientApp;
        _options = options.Value;
        _logger = logger;
    }

    public ValueTask<IdentityProviderConfiguration> GetConfigurationAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var authority = $"https://{_options.TenantName}.b2clogin.com/{_options.TenantName}.onmicrosoft.com/{_options.SignUpSignInPolicy}";

        var config = new IdentityProviderConfiguration
        {
            ProviderId = ProviderId,
            TenantId = tenantId,
            AuthorizationEndpoint = new Uri($"{authority}/oauth2/v2.0/authorize"),
            TokenEndpoint = new Uri($"{authority}/oauth2/v2.0/token"),
            UserInfoEndpoint = new Uri($"{authority}/openid/v2.0/userinfo"),
            JwksUri = new Uri($"{authority}/discovery/v2.0/keys"),
            Issuer = authority,
            ScopesSupported = new[] { "openid", "profile", "email", "offline_access" },
            ResponseTypesSupported = new[] { "code", "id_token", "token" },
            GrantTypesSupported = new[] { "authorization_code", "refresh_token" },
            TokenEndpointAuthMethodsSupported = new[] { "client_secret_post" },
            ProviderSpecificSettings = new Dictionary<string, object>
            {
                ["tenant_name"] = _options.TenantName,
                ["policy"] = _options.SignUpSignInPolicy,
                ["edit_profile_policy"] = _options.EditProfilePolicy,
                ["reset_password_policy"] = _options.ResetPasswordPolicy
            }
        };

        return ValueTask.FromResult(config);
    }

    public ValueTask<Uri> BuildAuthorizationUrlAsync(SmartAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        // B2C requires policy-specific URLs
        var policy = ExtractPolicyFromScope(request.Scope) ?? _options.SignUpSignInPolicy;
        var authority = $"https://{_options.TenantName}.b2clogin.com/{_options.TenantName}.onmicrosoft.com/{policy}";

        var authorizeUriBuilder = new UriBuilder($"{authority}/oauth2/v2.0/authorize");
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = request.ClientId,
            ["response_type"] = request.ResponseType,
            ["redirect_uri"] = request.RedirectUri,
            ["scope"] = request.Scope,
            ["state"] = request.State,
            ["response_mode"] = "query"
        };

        // Add PKCE if provided
        if (!string.IsNullOrEmpty(request.CodeChallenge))
        {
            queryParams["code_challenge"] = request.CodeChallenge;
            queryParams["code_challenge_method"] = request.CodeChallengeMethod ?? "S256";
        }

        // Add SMART-specific parameters
        if (!string.IsNullOrEmpty(request.Launch))
        {
            queryParams["launch"] = request.Launch;
        }

        if (!string.IsNullOrEmpty(request.Aud))
        {
            queryParams["aud"] = request.Aud;
        }

        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
        authorizeUriBuilder.Query = queryString;

        return ValueTask.FromResult(authorizeUriBuilder.Uri);
    }

    public async ValueTask<IdentityProviderTokenResponse> ExchangeCodeAsync(IdentityProviderTokenRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var policy = ExtractPolicyFromScope(request.Scope) ?? _options.SignUpSignInPolicy;
            var authority = $"https://{_options.TenantName}.b2clogin.com/{_options.TenantName}.onmicrosoft.com/{policy}";

            using var httpClient = new HttpClient();

            var tokenRequestParams = new Dictionary<string, string>
            {
                ["grant_type"] = request.GrantType,
                ["client_id"] = request.ClientId,
                ["code"] = request.Code,
                ["redirect_uri"] = request.RedirectUri
            };

            if (!string.IsNullOrEmpty(request.ClientSecret))
            {
                tokenRequestParams["client_secret"] = request.ClientSecret;
            }

            if (!string.IsNullOrEmpty(request.CodeVerifier))
            {
                tokenRequestParams["code_verifier"] = request.CodeVerifier;
            }

            var tokenRequest = new FormUrlEncodedContent(tokenRequestParams);
            var response = await httpClient.PostAsync($"{authority}/oauth2/v2.0/token", tokenRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new SmartAuthorizationException($"B2C token exchange failed: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            return new IdentityProviderTokenResponse
            {
                AccessToken = tokenResponse.GetProperty("access_token").GetString()!,
                TokenType = tokenResponse.GetProperty("token_type").GetString()!,
                ExpiresIn = tokenResponse.GetProperty("expires_in").GetInt32(),
                RefreshToken = tokenResponse.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                Scope = tokenResponse.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
                IdToken = tokenResponse.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null
            };
        }
        catch (Exception ex) when (!(ex is SmartAuthorizationException))
        {
            _logger.LogError(ex, "Failed to exchange authorization code for B2C tenant {TenantId}", request.TenantId);
            throw new SmartAuthorizationException($"B2C token exchange failed: {ex.Message}", ex);
        }
    }

    // Implement other methods similar to Entra ID but with B2C-specific logic...

    private string? ExtractPolicyFromScope(string scope)
    {
        // Extract policy from scope like "policy:B2C_1_signupsignin"
        var policyPrefix = "policy:";
        var policyScope = scope.Split(' ').FirstOrDefault(s => s.StartsWith(policyPrefix));
        return policyScope?.Substring(policyPrefix.Length);
    }
}

public class AzureB2CProviderOptions
{
    public required string TenantName { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string SignUpSignInPolicy { get; set; } = "B2C_1_signupsignin";
    public string? EditProfilePolicy { get; set; }
    public string? ResetPasswordPolicy { get; set; }
    public string[] DefaultScopes { get; set; } = { "openid", "profile", "email" };
}
```

## Generic OAuth Provider

### Generic OAuth 2.0 Provider

```csharp
public class GenericOAuthSmartIdentityProvider : ISmartIdentityProvider
{
    private readonly GenericOAuthProviderOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericOAuthSmartIdentityProvider> _logger;

    public string ProviderId { get; }

    public GenericOAuthSmartIdentityProvider(
        string providerId,
        IOptions<GenericOAuthProviderOptions> options,
        HttpClient httpClient,
        ILogger<GenericOAuthSmartIdentityProvider> logger)
    {
        ProviderId = providerId;
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public ValueTask<IdentityProviderConfiguration> GetConfigurationAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var config = new IdentityProviderConfiguration
        {
            ProviderId = ProviderId,
            TenantId = tenantId,
            AuthorizationEndpoint = _options.AuthorizationEndpoint,
            TokenEndpoint = _options.TokenEndpoint,
            UserInfoEndpoint = _options.UserInfoEndpoint,
            JwksUri = _options.JwksUri,
            Issuer = _options.Issuer,
            ScopesSupported = _options.ScopesSupported,
            ResponseTypesSupported = _options.ResponseTypesSupported,
            GrantTypesSupported = _options.GrantTypesSupported,
            TokenEndpointAuthMethodsSupported = _options.TokenEndpointAuthMethodsSupported,
            ProviderSpecificSettings = _options.AdditionalSettings
        };

        return ValueTask.FromResult(config);
    }

    public ValueTask<Uri> BuildAuthorizationUrlAsync(SmartAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = request.ClientId,
            ["response_type"] = request.ResponseType,
            ["redirect_uri"] = request.RedirectUri,
            ["scope"] = request.Scope,
            ["state"] = request.State
        };

        // Add PKCE if provided
        if (!string.IsNullOrEmpty(request.CodeChallenge))
        {
            queryParams["code_challenge"] = request.CodeChallenge;
            queryParams["code_challenge_method"] = request.CodeChallengeMethod ?? "S256";
        }

        // Add SMART-specific parameters
        if (!string.IsNullOrEmpty(request.Launch))
        {
            queryParams["launch"] = request.Launch;
        }

        if (!string.IsNullOrEmpty(request.Aud))
        {
            queryParams["aud"] = request.Aud;
        }

        // Add provider-specific parameters
        foreach (var kvp in _options.AdditionalAuthParameters ?? new Dictionary<string, string>())
        {
            queryParams[kvp.Key] = kvp.Value;
        }

        var authorizeUriBuilder = new UriBuilder(_options.AuthorizationEndpoint);
        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
        authorizeUriBuilder.Query = queryString;

        return ValueTask.FromResult(authorizeUriBuilder.Uri);
    }

    // Implement standard OAuth flows with customizable endpoints and claim mappings...
}

public class GenericOAuthProviderOptions
{
    public required Uri AuthorizationEndpoint { get; set; }
    public required Uri TokenEndpoint { get; set; }
    public required Uri UserInfoEndpoint { get; set; }
    public required Uri JwksUri { get; set; }
    public required string Issuer { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public string[] ScopesSupported { get; set; } = { "openid", "profile", "email" };
    public string[] ResponseTypesSupported { get; set; } = { "code" };
    public string[] GrantTypesSupported { get; set; } = { "authorization_code", "refresh_token" };
    public string[] TokenEndpointAuthMethodsSupported { get; set; } = { "client_secret_post" };
    public IReadOnlyDictionary<string, string>? AdditionalAuthParameters { get; set; }
    public IReadOnlyDictionary<string, object>? AdditionalSettings { get; set; }
    public IReadOnlyDictionary<string, string>? ClaimMappings { get; set; }
}
```

## Provider Factory and Management

### Identity Provider Factory

```csharp
public class SmartIdentityProviderFactory : ISmartIdentityProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITenantProviderConfiguration _tenantConfig;
    private readonly ILogger<SmartIdentityProviderFactory> _logger;
    private readonly ConcurrentDictionary<string, ISmartIdentityProvider> _providerCache = new();

    public SmartIdentityProviderFactory(
        IServiceProvider serviceProvider,
        ITenantProviderConfiguration tenantConfig,
        ILogger<SmartIdentityProviderFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _tenantConfig = tenantConfig;
        _logger = logger;
    }

    public ISmartIdentityProvider CreateProvider(string tenantId, string providerId)
    {
        var cacheKey = $"{tenantId}:{providerId}";

        return _providerCache.GetOrAdd(cacheKey, _ =>
        {
            _logger.LogInformation("Creating identity provider {ProviderId} for tenant {TenantId}", providerId, tenantId);

            return providerId.ToLowerInvariant() switch
            {
                "entra" => _serviceProvider.GetRequiredService<EntraIdSmartIdentityProvider>(),
                "b2c" => _serviceProvider.GetRequiredService<AzureB2CSmartIdentityProvider>(),
                "auth0" => CreateAuth0Provider(tenantId),
                "okta" => CreateOktaProvider(tenantId),
                "local" => _serviceProvider.GetRequiredService<LocalSmartIdentityProvider>(),
                _ => CreateGenericProvider(tenantId, providerId)
            };
        });
    }

    public IReadOnlyList<string> GetAvailableProviders(string tenantId)
    {
        var tenantConfig = _tenantConfig.GetProviderConfiguration(tenantId);
        return tenantConfig.EnabledProviders;
    }

    private ISmartIdentityProvider CreateAuth0Provider(string tenantId)
    {
        // Create Auth0-specific provider with tenant configuration
        var options = _tenantConfig.GetAuth0Options(tenantId);
        return new GenericOAuthSmartIdentityProvider(
            "auth0",
            Microsoft.Extensions.Options.Options.Create(ConvertToGenericOptions(options)),
            _serviceProvider.GetRequiredService<HttpClient>(),
            _serviceProvider.GetRequiredService<ILogger<GenericOAuthSmartIdentityProvider>>());
    }

    private ISmartIdentityProvider CreateOktaProvider(string tenantId)
    {
        // Create Okta-specific provider with tenant configuration
        var options = _tenantConfig.GetOktaOptions(tenantId);
        return new GenericOAuthSmartIdentityProvider(
            "okta",
            Microsoft.Extensions.Options.Options.Create(ConvertToGenericOptions(options)),
            _serviceProvider.GetRequiredService<HttpClient>(),
            _serviceProvider.GetRequiredService<ILogger<GenericOAuthSmartIdentityProvider>>());
    }

    private ISmartIdentityProvider CreateGenericProvider(string tenantId, string providerId)
    {
        var options = _tenantConfig.GetGenericProviderOptions(tenantId, providerId);
        return new GenericOAuthSmartIdentityProvider(
            providerId,
            Microsoft.Extensions.Options.Options.Create(options),
            _serviceProvider.GetRequiredService<HttpClient>(),
            _serviceProvider.GetRequiredService<ILogger<GenericOAuthSmartIdentityProvider>>());
    }
}
```

### Tenant Provider Configuration

```csharp
public interface ITenantProviderConfiguration
{
    TenantProviderConfig GetProviderConfiguration(string tenantId);
    EntraIdProviderOptions GetEntraIdOptions(string tenantId);
    AzureB2CProviderOptions GetB2COptions(string tenantId);
    Auth0ProviderOptions GetAuth0Options(string tenantId);
    OktaProviderOptions GetOktaOptions(string tenantId);
    GenericOAuthProviderOptions GetGenericProviderOptions(string tenantId, string providerId);
}

public record TenantProviderConfig
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<string> EnabledProviders { get; init; }
    public required string DefaultProvider { get; init; }
    public IReadOnlyDictionary<string, object>? ProviderSettings { get; init; }
}
```

## Updated SMART Token Service

### Multi-Provider Token Service

```csharp
public class MultiProviderSmartTokenService : ISmartTokenService
{
    private readonly ISmartIdentityProviderFactory _providerFactory;
    private readonly ISmartClientService _clientService;
    private readonly ISmartAuthorizationCodeStore _codeStore;
    private readonly ISmartRefreshTokenStore _refreshTokenStore;
    private readonly ILogger<MultiProviderSmartTokenService> _logger;

    public MultiProviderSmartTokenService(
        ISmartIdentityProviderFactory providerFactory,
        ISmartClientService clientService,
        ISmartAuthorizationCodeStore codeStore,
        ISmartRefreshTokenStore refreshTokenStore,
        ILogger<MultiProviderSmartTokenService> logger)
    {
        _providerFactory = providerFactory;
        _clientService = clientService;
        _codeStore = codeStore;
        _refreshTokenStore = refreshTokenStore;
        _logger = logger;
    }

    public async ValueTask<SmartTokenResponse> ExchangeCodeAsync(SmartTokenRequest request, CancellationToken cancellationToken = default)
    {
        // Validate authorization code and get associated provider
        var authCode = await _codeStore.ConsumeAsync(request.Code, cancellationToken);
        if (authCode == null)
        {
            throw new SmartAuthorizationException("Invalid or expired authorization code");
        }

        // Get the identity provider that issued this code
        var provider = _providerFactory.CreateProvider(request.TenantId, authCode.ProviderId);

        // Exchange code with the identity provider
        var providerRequest = new IdentityProviderTokenRequest
        {
            TenantId = request.TenantId,
            ProviderId = authCode.ProviderId,
            GrantType = request.GrantType,
            Code = request.Code,
            RedirectUri = request.RedirectUri,
            ClientId = request.ClientId,
            ClientSecret = request.ClientSecret,
            CodeVerifier = request.CodeVerifier
        };

        var providerResponse = await provider.ExchangeCodeAsync(providerRequest, cancellationToken);

        // Validate token and extract claims
        var tokenClaims = await provider.ValidateTokenAsync(providerResponse.AccessToken, request.TenantId, cancellationToken);
        if (tokenClaims == null)
        {
            throw new SmartAuthorizationException("Invalid token received from identity provider");
        }

        // Map provider claims to SMART token response
        return new SmartTokenResponse
        {
            AccessToken = providerResponse.AccessToken,
            TokenType = providerResponse.TokenType,
            ExpiresIn = providerResponse.ExpiresIn,
            RefreshToken = providerResponse.RefreshToken,
            Scope = string.Join(" ", tokenClaims.Scopes),
            IdToken = providerResponse.IdToken,
            Patient = authCode.LaunchContext?.Patient,
            Encounter = authCode.LaunchContext?.Encounter
        };
    }

    public async ValueTask<SmartTokenClaims?> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        // Try to decode token to determine which provider issued it
        var providerId = ExtractProviderFromToken(accessToken);
        if (string.IsNullOrEmpty(providerId))
        {
            _logger.LogWarning("Unable to determine identity provider from token");
            return null;
        }

        // Extract tenant from token or use configured mapping
        var tenantId = ExtractTenantFromToken(accessToken);
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Unable to determine tenant from token");
            return null;
        }

        var provider = _providerFactory.CreateProvider(tenantId, providerId);
        var providerClaims = await provider.ValidateTokenAsync(accessToken, tenantId, cancellationToken);

        if (providerClaims == null)
        {
            return null;
        }

        // Convert provider claims to SMART claims
        return new SmartTokenClaims
        {
            TenantId = tenantId,
            ClientId = providerClaims.ClientId ?? "unknown",
            Subject = providerClaims.Subject,
            Scopes = providerClaims.Scopes,
            IssuedAt = providerClaims.IssuedAt,
            ExpiresAt = providerClaims.ExpiresAt,
            User = providerClaims.Subject,
            // Map custom claims as needed
            FhirContext = ExtractFhirContextFromClaims(providerClaims)
        };
    }

    private static string? ExtractProviderFromToken(string accessToken)
    {
        try
        {
            var handler = new JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(accessToken);

            // Check issuer to determine provider
            return jwt.Issuer switch
            {
                var iss when iss.Contains("login.microsoftonline.com") => "entra",
                var iss when iss.Contains("b2clogin.com") => "b2c",
                var iss when iss.Contains("auth0.com") => "auth0",
                var iss when iss.Contains("okta.com") => "okta",
                _ => "generic"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractTenantFromToken(string accessToken)
    {
        try
        {
            var handler = new JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(accessToken);

            // Try various tenant claim locations
            return jwt.GetClaim("tenant_id")?.Value ??
                   jwt.GetClaim("tid")?.Value ??
                   jwt.GetClaim("tenantId")?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, object>? ExtractFhirContextFromClaims(IdentityProviderTokenClaims claims)
    {
        var fhirContext = new Dictionary<string, object>();

        // Extract FHIR-specific context from custom claims
        if (claims.CustomClaims != null)
        {
            foreach (var claim in claims.CustomClaims)
            {
                if (claim.Key.StartsWith("fhir:", StringComparison.OrdinalIgnoreCase) ||
                    claim.Key.Equals("patient", StringComparison.OrdinalIgnoreCase) ||
                    claim.Key.Equals("encounter", StringComparison.OrdinalIgnoreCase))
                {
                    fhirContext[claim.Key] = claim.Value;
                }
            }
        }

        return fhirContext.Count > 0 ? fhirContext : null;
    }
}
```

## Dependency Injection Configuration

```csharp
public static class MultiProviderSmartServiceCollectionExtensions
{
    public static IServiceCollection AddMultiProviderSmartOnFhir(this IServiceCollection services, IConfiguration configuration)
    {
        var smartSection = configuration.GetSection("Smart");

        // Configure provider options
        services.Configure<EntraIdProviderOptions>(smartSection.GetSection("Entra"));
        services.Configure<AzureB2CProviderOptions>(smartSection.GetSection("B2C"));

        // Register provider implementations
        services.AddTransient<EntraIdSmartIdentityProvider>();
        services.AddTransient<AzureB2CSmartIdentityProvider>();
        services.AddTransient<LocalSmartIdentityProvider>();

        // Register factory and main services
        services.AddSingleton<ISmartIdentityProviderFactory, SmartIdentityProviderFactory>();
        services.AddSingleton<ITenantProviderConfiguration, TenantProviderConfigurationService>();
        services.AddScoped<ISmartTokenService, MultiProviderSmartTokenService>();

        // Configure MSAL for Entra ID and B2C
        services.AddMsal(smartSection);

        return services;
    }

    private static IServiceCollection AddMsal(this IServiceCollection services, IConfigurationSection smartSection)
    {
        var entraOptions = smartSection.GetSection("Entra").Get<EntraIdProviderOptions>();
        var b2cOptions = smartSection.GetSection("B2C").Get<AzureB2CProviderOptions>();

        if (entraOptions != null)
        {
            services.AddSingleton<IConfidentialClientApplication>(provider =>
            {
                return ConfidentialClientApplicationBuilder
                    .Create(entraOptions.ClientId)
                    .WithClientSecret(entraOptions.ClientSecret)
                    .WithAuthority(entraOptions.Authority)
                    .Build();
            });
        }

        return services;
    }
}
```

This multi-provider abstraction allows the SMART on FHIR implementation to work with any identity provider while maintaining consistent SMART semantics. The system can be configured per-tenant to use different providers or even multiple providers simultaneously.