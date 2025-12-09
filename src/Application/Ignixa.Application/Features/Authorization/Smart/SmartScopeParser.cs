// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// Parser for SMART on FHIR scope strings.
/// Supports both SMART v1 and v2 scope formats.
/// </summary>
public static partial class SmartScopeParser
{
    // SMART scope format: [context]/[resource].[permission]
    // Examples:
    //   patient/Observation.read
    //   user/*.write
    //   system/Patient.*
    //   patient/Observation.cruds (SMART v2 granular)
    [GeneratedRegex(@"^(patient|user|system)/([A-Za-z*]+)\.([a-zA-Z*]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SmartScopeRegex();

    /// <summary>
    /// Parses a SMART on FHIR scope string.
    /// </summary>
    /// <param name="scope">The scope string to parse (e.g., "patient/Observation.read").</param>
    /// <returns>A parsed SmartScope, or null if the scope format is invalid.</returns>
    public static SmartScope? ParseScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var match = SmartScopeRegex().Match(scope);
        if (!match.Success)
        {
            return null;
        }

        var contextType = match.Groups[1].Value.ToUpperInvariant() switch
        {
            "PATIENT" => SmartScopeType.Patient,
            "USER" => SmartScopeType.User,
            "SYSTEM" => SmartScopeType.System,
            _ => SmartScopeType.User // Default fallback
        };

        return new SmartScope
        {
            Type = contextType,
            ResourceType = match.Groups[2].Value,
            Permission = match.Groups[3].Value.ToUpperInvariant(),
            OriginalScope = scope
        };
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
    /// Checks if a scope string is a valid SMART on FHIR scope.
    /// </summary>
    /// <param name="scope">The scope string to validate.</param>
    /// <returns>True if the scope is a valid SMART scope format.</returns>
    public static bool IsValidSmartScope(string scope)
    {
        return ParseScope(scope) != null;
    }
}
