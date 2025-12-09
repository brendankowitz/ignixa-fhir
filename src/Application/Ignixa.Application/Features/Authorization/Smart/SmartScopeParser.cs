// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Web;

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// Parser for SMART on FHIR v2 scope strings.
/// Implements SMART App Launch v2.2.0 specification.
/// Scope format: [context]/[resource].[permissions][?search-constraints]
/// </summary>
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

    /// <summary>
    /// Parses a SMART on FHIR v2 scope string.
    /// </summary>
    /// <param name="scope">The scope string to parse (e.g., "patient/Observation.rs").</param>
    /// <returns>A parsed SmartScope, or null if the scope format is invalid.</returns>
    public static SmartScope? ParseScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var match = SmartV2ScopeRegex().Match(scope);
        if (!match.Success)
        {
            return null;
        }

        var contextStr = match.Groups[1].Value;
        var resourceType = match.Groups[2].Value;
        var permissionStr = match.Groups[3].Value.ToUpperInvariant();
        var queryString = match.Groups[4].Success ? match.Groups[4].Value : null;

        // Validate permissions are in correct CRUDS order (case-insensitive)
        if (!IsValidPermissionOrder(permissionStr.ToUpperInvariant()))
        {
            return null;
        }

        var contextType = contextStr.ToUpperInvariant() switch
        {
            "PATIENT" => SmartScopeType.Patient,
            "USER" => SmartScopeType.User,
            "SYSTEM" => SmartScopeType.System,
            "PRACTITIONER" => SmartScopeType.Practitioner,
            _ => SmartScopeType.User
        };

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
            OriginalScope = scope
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
