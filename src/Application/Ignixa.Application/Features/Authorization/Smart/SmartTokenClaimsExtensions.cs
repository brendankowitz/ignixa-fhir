// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// Extension methods for SmartTokenClaims to simplify working with special scopes.
/// </summary>
public static class SmartTokenClaimsExtensions
{
    /// <summary>
    /// Creates SmartTokenClaims from a scope string, parsing both FHIR resource scopes and special scopes.
    /// </summary>
    /// <param name="scopeString">Space-separated scope string from OAuth token.</param>
    /// <returns>SmartTokenClaims with parsed scopes and special scope flags.</returns>
    public static SmartTokenClaims FromScopeString(string scopeString)
    {
        ArgumentNullException.ThrowIfNull(scopeString);

        var scopes = SmartScopeParser.ParseScopes(scopeString);
        var specialScopes = SmartScopeParser.ParseSpecialScopes(scopeString);

        return new SmartTokenClaims
        {
            ScopeString = scopeString,
            Scopes = scopes,
            SpecialScopes = specialScopes,
            HasOpenIdScope = specialScopes.Any(s => s.Name == "openid"),
            HasOfflineAccess = specialScopes.Any(s => s.Name == "offline_access"),
            LaunchContext = GetLaunchContext(specialScopes)
        };
    }

    /// <summary>
    /// Determines the launch context type from special scopes.
    /// </summary>
    /// <param name="specialScopes">Collection of special scopes.</param>
    /// <returns>"patient" for launch/patient, "encounter" for launch/encounter, or null for standalone launch.</returns>
    private static string? GetLaunchContext(IReadOnlyList<SpecialScope> specialScopes)
    {
        if (specialScopes.Any(s => s.Name == "launch/patient"))
        {
            return "patient";
        }

        if (specialScopes.Any(s => s.Name == "launch/encounter"))
        {
            return "encounter";
        }

        // "launch" scope without context means standalone launch
        return null;
    }

    /// <summary>
    /// Gets all OpenID Connect scopes from the token.
    /// </summary>
    /// <param name="claims">The token claims.</param>
    /// <returns>List of OpenID Connect scope names.</returns>
    public static IEnumerable<string> GetOpenIdConnectScopes(this SmartTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return claims.SpecialScopes
            .Where(s => s.Type == SpecialScopeType.OpenIdConnect)
            .Select(s => s.Name);
    }

    /// <summary>
    /// Gets all launch scopes from the token.
    /// </summary>
    /// <param name="claims">The token claims.</param>
    /// <returns>List of launch scope names.</returns>
    public static IEnumerable<string> GetLaunchScopes(this SmartTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return claims.SpecialScopes
            .Where(s => s.Type == SpecialScopeType.Launch)
            .Select(s => s.Name);
    }

    /// <summary>
    /// Checks if the token has a specific special scope.
    /// </summary>
    /// <param name="claims">The token claims.</param>
    /// <param name="scopeName">The scope name to check (case-insensitive).</param>
    /// <returns>True if the token includes this scope.</returns>
    public static bool HasSpecialScope(this SmartTokenClaims claims, string scopeName)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(scopeName);

        return claims.SpecialScopes.Any(s =>
            string.Equals(s.Name, scopeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if the token is an EHR launch (has launch context scopes).
    /// </summary>
    /// <param name="claims">The token claims.</param>
    /// <returns>True if this is an EHR launch with context.</returns>
    public static bool IsEhrLaunch(this SmartTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return claims.SpecialScopes.Any(s => s.Type == SpecialScopeType.Launch);
    }

    /// <summary>
    /// Checks if the token is a standalone launch (no launch context scopes).
    /// </summary>
    /// <param name="claims">The token claims.</param>
    /// <returns>True if this is a standalone launch.</returns>
    public static bool IsStandaloneLaunch(this SmartTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return !IsEhrLaunch(claims);
    }
}
