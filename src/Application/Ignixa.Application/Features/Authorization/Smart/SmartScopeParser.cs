// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Web;

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// Parser for SMART on FHIR scope strings.
/// Implements SMART App Launch v2.2.0 specification with backward compatibility for v1.
/// Scope format: [context]/[resource].[permissions][?search-constraints]
/// </summary>
/// <remarks>
/// <para>Supports both SMART v2 and v1 formats:</para>
/// <list type="bullet">
/// <item>v2: patient/Observation.rs (read + search)</item>
/// <item>v1: patient/Observation.read (converted to .rs)</item>
/// </list>
/// <para>V1 to V2 permission mappings:</para>
/// <list type="bullet">
/// <item>.read → .rs (read + search)</item>
/// <item>.write → .cud (create, update, delete)</item>
/// <item>.* → .cruds (all permissions)</item>
/// </list>
/// </remarks>
public static partial class SmartScopeParser
{
    // SMART v2 scope format: [context]/[resource].[cruds][?search-params]
    // - context: patient, user, system, practitioner
    // - resource: FHIR resource type or * for all
    // - cruds: permissions in order (c=create, r=read, u=update, d=delete, s=search)
    // - search-params: optional FHIR search parameter constraints
    //
    // Examples:
    //   patient/Observation.rs (read + search)
    //   user/Medication.cruds (all permissions)
    //   system/Patient.cud (create, update, delete)
    //   patient/Observation.rs?category=http://terminology.hl7.org/CodeSystem/observation-category|laboratory
    [GeneratedRegex(
        @"^(patient|user|system|practitioner)/([A-Za-z*]+)\.(c?r?u?d?s?)(\?.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SmartV2ScopeRegex();

    // SMART v1 scope format: [context]/[resource].[read|write|*]
    // Legacy format from SMART App Launch v1, converted to v2 format
    // - read → rs (read + search)
    // - write → cud (create, update, delete)
    // - * → cruds (all permissions)
    [GeneratedRegex(
        @"^(patient|user|system)/([A-Za-z*]+)\.(read|write|\*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SmartV1ScopeRegex();

    /// <summary>
    /// Parses a SMART on FHIR scope string (v2 or v1 format).
    /// </summary>
    /// <param name="scope">The scope string to parse (e.g., "patient/Observation.rs" or "patient/Observation.read").</param>
    /// <returns>A parsed SmartScope, or null if the scope format is invalid.</returns>
    /// <remarks>
    /// Attempts to parse as v2 format first, then falls back to v1 format for backward compatibility.
    /// V1 scopes are automatically converted to v2 format internally.
    /// </remarks>
    public static SmartScope? ParseScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        // Try v2 format first
        var v2Match = SmartV2ScopeRegex().Match(scope);
        if (v2Match.Success)
        {
            return ParseV2Scope(v2Match, scope);
        }

        // Fall back to v1 format for backward compatibility
        var v1Match = SmartV1ScopeRegex().Match(scope);
        if (v1Match.Success)
        {
            return ParseV1Scope(v1Match, scope);
        }

        return null;
    }

    /// <summary>
    /// Parses a SMART v2 scope from a regex match.
    /// </summary>
    private static SmartScope? ParseV2Scope(Match match, string originalScope)
    {
        var contextStr = match.Groups[1].Value;
        var resourceType = match.Groups[2].Value;
        var permissionStr = match.Groups[3].Value.ToUpperInvariant();
        var queryString = match.Groups[4].Success ? match.Groups[4].Value : null;

        // Validate permissions are in correct CRUDS order (case-insensitive)
        if (!IsValidPermissionOrder(permissionStr))
        {
            return null;
        }

        var contextType = ParseContextType(contextStr);
        var permissions = ParsePermissions(permissionStr);

        // Parse search constraints if present
        Dictionary<string, string>? searchConstraints = null;
        if (!string.IsNullOrEmpty(queryString))
        {
            searchConstraints = ParseSearchConstraints(queryString);
        }

        return new SmartScope
        {
            Type = contextType,
            ResourceType = resourceType,
            Permissions = permissions,
            PermissionString = permissionStr,
            SearchConstraints = searchConstraints,
            OriginalScope = originalScope
        };
    }

    /// <summary>
    /// Parses a SMART v1 scope and converts it to v2 format.
    /// </summary>
    /// <param name="match">The regex match for v1 scope format.</param>
    /// <param name="originalScope">The original scope string.</param>
    /// <returns>A SmartScope in v2 format, or null if conversion fails.</returns>
    /// <remarks>
    /// V1 to V2 permission mappings:
    /// <list type="bullet">
    /// <item>.read → .rs (read + search)</item>
    /// <item>.write → .cud (create, update, delete)</item>
    /// <item>.* → .cruds (all permissions)</item>
    /// </list>
    /// </remarks>
    private static SmartScope? ParseV1Scope(Match match, string originalScope)
    {
        var contextStr = match.Groups[1].Value;
        var resourceType = match.Groups[2].Value;
        var v1Permission = match.Groups[3].Value;

        var contextType = ParseContextType(contextStr);

        // Convert v1 permissions to v2 CRUDS format
        var (permissionStr, permissions) = ConvertV1ToV2Permissions(v1Permission);

        return new SmartScope
        {
            Type = contextType,
            ResourceType = resourceType,
            Permissions = permissions,
            PermissionString = permissionStr,
            SearchConstraints = null,
            OriginalScope = originalScope
        };
    }

    /// <summary>
    /// Converts SMART v1 permission syntax to v2 CRUDS format.
    /// </summary>
    /// <param name="v1Permission">The v1 permission string (read, write, or *).</param>
    /// <returns>A tuple of (v2 permission string, SmartPermissions flags).</returns>
    private static (string PermissionString, SmartPermissions Permissions) ConvertV1ToV2Permissions(string v1Permission)
    {
        return v1Permission.ToUpperInvariant() switch
        {
            "READ" => ("RS", SmartPermissions.Read | SmartPermissions.Search),
            "WRITE" => ("CUD", SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete),
            "*" => ("CRUDS", SmartPermissions.All),
            _ => ("", SmartPermissions.None)
        };
    }

    /// <summary>
    /// Parses context type from context string.
    /// </summary>
    private static SmartScopeType ParseContextType(string contextStr)
    {
        return contextStr.ToUpperInvariant() switch
        {
            "PATIENT" => SmartScopeType.Patient,
            "USER" => SmartScopeType.User,
            "SYSTEM" => SmartScopeType.System,
            "PRACTITIONER" => SmartScopeType.Practitioner,
            _ => SmartScopeType.User
        };
    }

    /// <summary>
    /// Validates that permissions are in correct CRUDS order.
    /// SMART v2 requires permissions in the order: c, r, u, d, s.
    /// </summary>
    private static bool IsValidPermissionOrder(string permissions)
    {
        if (string.IsNullOrEmpty(permissions))
        {
            return false;
        }

        const string validOrder = "CRUDS";
        int lastIndex = -1;
        foreach (char c in permissions)
        {
            int currentIndex = validOrder.IndexOf(c, StringComparison.OrdinalIgnoreCase);
            if (currentIndex == -1 || currentIndex <= lastIndex)
            {
                return false;
            }
            lastIndex = currentIndex;
        }

        return true;
    }

    /// <summary>
    /// Parses CRUDS permission string to flags.
    /// </summary>
    private static SmartPermissions ParsePermissions(string permissionStr)
    {
        var permissions = SmartPermissions.None;

        foreach (char c in permissionStr.ToUpperInvariant())
        {
            permissions |= c switch
            {
                'C' => SmartPermissions.Create,
                'R' => SmartPermissions.Read,
                'U' => SmartPermissions.Update,
                'D' => SmartPermissions.Delete,
                'S' => SmartPermissions.Search,
                _ => SmartPermissions.None
            };
        }

        return permissions;
    }

    /// <summary>
    /// Parses search parameter constraints from query string.
    /// </summary>
    private static Dictionary<string, string> ParseSearchConstraints(string queryString)
    {
        var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Remove leading '?' if present
        if (queryString.StartsWith('?'))
        {
            queryString = queryString[1..];
        }

        if (string.IsNullOrEmpty(queryString))
        {
            return constraints;
        }

        var pairs = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = HttpUtility.UrlDecode(parts[0]);
                var value = HttpUtility.UrlDecode(parts[1]);
                constraints[key] = value;
            }
        }

        return constraints;
    }

    /// <summary>
    /// Parses a special (non-FHIR resource) SMART scope.
    /// </summary>
    /// <param name="scope">The scope string to parse (e.g., "openid", "launch/patient").</param>
    /// <returns>A parsed SpecialScope, or null if not a valid special scope.</returns>
    public static SpecialScope? ParseSpecialScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        // Special scopes are case-insensitive per OAuth 2.0 spec, but comparison should be case-insensitive
        // CA1308 suppression justified: OAuth scope comparison requires lowercase normalization
#pragma warning disable CA1308 // Normalize strings to uppercase
        return scope.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            // OpenID Connect scopes
            "openid" => new SpecialScope("openid", SpecialScopeType.OpenIdConnect),
            "profile" => new SpecialScope("profile", SpecialScopeType.OpenIdConnect),
            "email" => new SpecialScope("email", SpecialScopeType.OpenIdConnect),
            "fhiruser" => new SpecialScope("fhirUser", SpecialScopeType.OpenIdConnect),

            // Offline access
            "offline_access" => new SpecialScope("offline_access", SpecialScopeType.OfflineAccess),

            // Launch context
            "launch" => new SpecialScope("launch", SpecialScopeType.Launch),
            "launch/patient" => new SpecialScope("launch/patient", SpecialScopeType.Launch),
            "launch/encounter" => new SpecialScope("launch/encounter", SpecialScopeType.Launch),

            _ => null
        };
    }

    /// <summary>
    /// Checks if a scope string is a valid special (non-FHIR resource) scope.
    /// </summary>
    /// <param name="scope">The scope string to validate.</param>
    /// <returns>True if the scope is a valid special scope.</returns>
    public static bool IsSpecialScope(string scope)
    {
        return ParseSpecialScope(scope) != null;
    }

    /// <summary>
    /// Parses multiple SMART scopes from a space-separated string.
    /// </summary>
    /// <param name="scopeString">Space-separated scope string from OAuth token.</param>
    /// <returns>List of parsed SmartScopes (invalid scopes are skipped).</returns>
    public static IReadOnlyList<SmartScope> ParseScopes(string? scopeString)
    {
        if (string.IsNullOrWhiteSpace(scopeString))
        {
            return Array.Empty<SmartScope>();
        }

        return scopeString
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseScope)
            .Where(s => s != null)
            .Cast<SmartScope>()
            .ToList();
    }

    /// <summary>
    /// Parses special (non-FHIR resource) scopes from a space-separated string.
    /// </summary>
    /// <param name="scopeString">Space-separated scope string from OAuth token.</param>
    /// <returns>List of parsed SpecialScopes (invalid special scopes are skipped).</returns>
    public static IReadOnlyList<SpecialScope> ParseSpecialScopes(string? scopeString)
    {
        if (string.IsNullOrWhiteSpace(scopeString))
        {
            return Array.Empty<SpecialScope>();
        }

        return scopeString
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseSpecialScope)
            .Where(s => s != null)
            .Cast<SpecialScope>()
            .ToList();
    }

    /// <summary>
    /// Parses SMART scopes from a collection of scope strings.
    /// </summary>
    /// <param name="scopes">Collection of individual scope strings.</param>
    /// <returns>List of parsed SmartScopes (invalid scopes are skipped).</returns>
    public static IReadOnlyList<SmartScope> ParseScopes(IEnumerable<string>? scopes)
    {
        if (scopes == null)
        {
            return Array.Empty<SmartScope>();
        }

        return scopes
            .Select(ParseScope)
            .Where(s => s != null)
            .Cast<SmartScope>()
            .ToList();
    }

    /// <summary>
    /// Checks if a scope string is a valid SMART on FHIR v2 scope.
    /// </summary>
    /// <param name="scope">The scope string to validate.</param>
    /// <returns>True if the scope is a valid SMART v2 scope format.</returns>
    public static bool IsValidSmartScope(string scope)
    {
        return ParseScope(scope) != null;
    }

    /// <summary>
    /// Builds a canonical SMART v2 scope string.
    /// </summary>
    /// <param name="type">The scope type.</param>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="permissions">The permissions.</param>
    /// <param name="searchConstraints">Optional search constraints.</param>
    /// <returns>A SMART v2 scope string.</returns>
    public static string BuildScope(
        SmartScopeType type,
        string resourceType,
        SmartPermissions permissions,
        IReadOnlyDictionary<string, string>? searchConstraints = null)
    {
        var contextStr = type switch
        {
            SmartScopeType.Patient => "patient",
            SmartScopeType.User => "user",
            SmartScopeType.System => "system",
            SmartScopeType.Practitioner => "practitioner",
            _ => "user"
        };

        var permStr = BuildPermissionString(permissions);
        var scope = $"{contextStr}/{resourceType}.{permStr}";

        if (searchConstraints != null && searchConstraints.Count > 0)
        {
            var queryParts = searchConstraints
                .Select(kvp => $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}");
            scope += "?" + string.Join("&", queryParts);
        }

        return scope;
    }

    /// <summary>
    /// Builds a permission string from flags in canonical CRUDS order.
    /// </summary>
    private static string BuildPermissionString(SmartPermissions permissions)
    {
        var chars = new List<char>(5);

        if ((permissions & SmartPermissions.Create) != 0) chars.Add('c');
        if ((permissions & SmartPermissions.Read) != 0) chars.Add('r');
        if ((permissions & SmartPermissions.Update) != 0) chars.Add('u');
        if ((permissions & SmartPermissions.Delete) != 0) chars.Add('d');
        if ((permissions & SmartPermissions.Search) != 0) chars.Add('s');

        return new string(chars.ToArray());
    }
}
