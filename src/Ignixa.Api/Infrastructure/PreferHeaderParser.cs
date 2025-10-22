// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Parses and validates the Prefer header for FHIR validation level preferences.
/// Supports: Prefer: validation=none|minimum|spec|full
/// Maps to internal ValidationTier: None|Fast|Spec|Profile
/// </summary>
public static class PreferHeaderParser
{
    /// <summary>
    /// Parses the Prefer header to extract validation level preference.
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    /// <returns>Parsed ValidationTier, or null if header not present or invalid.</returns>
    /// <remarks>
    /// Supported format: Prefer: validation=none|minimum|spec|full
    /// Examples:
    ///   - Prefer: validation=spec → ValidationTier.Spec
    ///   - Prefer: validation=full → ValidationTier.Profile
    ///   - Prefer: validation=minimum → ValidationTier.Fast
    ///   - Prefer: validation=none → ValidationTier.None
    /// Also supports multiple preferences: Prefer: return=representation, validation=spec
    /// </remarks>
    public static ValidationTier? TryParseValidationLevel(IHeaderDictionary headers, ILogger? logger = null)
    {
        if (!headers.TryGetValue("Prefer", out var preferHeader))
        {
            return null;
        }

        var preferValue = preferHeader.ToString();
        if (string.IsNullOrWhiteSpace(preferValue))
        {
            return null;
        }

        // Parse comma-separated preferences
        // Example: "return=representation, validation=spec"
        foreach (var preference in preferValue.Split(','))
        {
            var trimmedPref = preference.Trim();
            if (trimmedPref.StartsWith("validation=", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmedPref.Substring("validation=".Length).Trim();
                return ParseValidationLevelValue(value, logger);
            }
        }

        return null;
    }

    /// <summary>
    /// Converts the validation level to a response header value.
    /// </summary>
    /// <param name="tier">The validation tier that was applied.</param>
    /// <returns>Header value for Preference-Applied (e.g., "validation=spec").</returns>
    public static string ToPreferenceAppliedHeader(ValidationTier tier)
    {
        return tier switch
        {
            ValidationTier.None => "validation=none",
            ValidationTier.Fast => "validation=minimum",
            ValidationTier.Spec => "validation=spec",
            ValidationTier.Profile => "validation=full",
            _ => "validation=spec"
        };
    }

    /// <summary>
    /// Parses a single validation level value.
    /// </summary>
    private static ValidationTier? ParseValidationLevelValue(string value, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            logger?.LogWarning("Prefer header validation value is empty");
            return null;
        }

        var result = value.ToUpperInvariant() switch
        {
            "NONE" => ValidationTier.None,
            "MINIMUM" => ValidationTier.Fast,
            "SPEC" => ValidationTier.Spec,
            "FULL" => ValidationTier.Profile,
            _ => (ValidationTier?)null
        };

        if (result is null)
        {
            logger?.LogWarning("Unknown Prefer header validation value: {Value}", value);
        }

        return result;
    }
}
