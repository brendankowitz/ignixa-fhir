// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Enum for RFC 7240 Prefer header return preferences.
/// </summary>
public enum ReturnPreference
{
    /// <summary>
    /// Server may choose whether to return representation or minimal (client not specified).
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Server should return full resource representation in response body.
    /// </summary>
    Representation = 1,

    /// <summary>
    /// Server should return minimal representation (headers only, no body).
    /// </summary>
    Minimal = 2,

    /// <summary>
    /// Server should return OperationOutcome in response body.
    /// </summary>
    OperationOutcome = 3
}

/// <summary>
/// Parses and validates the Prefer header for FHIR validation level preferences and return preferences.
/// Supports:
///   - Prefer: validation=none|minimum|spec|full (FHIR profile validation)
///   - Prefer: return=representation|minimal|OperationOutcome (RFC 7240)
/// Maps validation to internal ValidationTier: None|Fast|Spec|Profile
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
    /// Parses the Prefer header to extract return preference (RFC 7240).
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    /// <returns>Parsed ReturnPreference, or Unspecified if header not present or invalid.</returns>
    /// <remarks>
    /// Supported format: Prefer: return=representation|minimal|OperationOutcome
    /// Examples:
    ///   - Prefer: return=representation → ReturnPreference.Representation
    ///   - Prefer: return=minimal → ReturnPreference.Minimal
    ///   - Prefer: return=OperationOutcome → ReturnPreference.OperationOutcome
    /// Also supports multiple preferences: Prefer: return=representation, validation=spec
    /// </remarks>
    public static ReturnPreference TryParseReturnPreference(IHeaderDictionary headers, ILogger? logger = null)
    {
        if (!headers.TryGetValue("Prefer", out var preferHeader))
        {
            return ReturnPreference.Unspecified;
        }

        var preferValue = preferHeader.ToString();
        if (string.IsNullOrWhiteSpace(preferValue))
        {
            return ReturnPreference.Unspecified;
        }

        // Parse comma-separated preferences
        // Example: "return=representation, validation=spec"
        foreach (var preference in preferValue.Split(','))
        {
            var trimmedPref = preference.Trim();
            if (trimmedPref.StartsWith("return=", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmedPref.Substring("return=".Length).Trim();
                return ParseReturnPreferenceValue(value, logger);
            }
        }

        return ReturnPreference.Unspecified;
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
    /// Converts the return preference to a response header value.
    /// </summary>
    /// <param name="returnPref">The return preference that was applied.</param>
    /// <returns>Header value for Preference-Applied (e.g., "return=representation").</returns>
    public static string ToPreferenceAppliedHeader(ReturnPreference returnPref)
    {
        return returnPref switch
        {
            ReturnPreference.Representation => "return=representation",
            ReturnPreference.Minimal => "return=minimal",
            ReturnPreference.OperationOutcome => "return=OperationOutcome",
            _ => "return=minimal"  // Default to minimal if unspecified
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

    /// <summary>
    /// Parses a single return preference value (RFC 7240).
    /// </summary>
    private static ReturnPreference ParseReturnPreferenceValue(string value, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            logger?.LogWarning("Prefer header return value is empty");
            return ReturnPreference.Unspecified;
        }

        var result = value.ToUpperInvariant() switch
        {
            "REPRESENTATION" => ReturnPreference.Representation,
            "MINIMAL" => ReturnPreference.Minimal,
            "OPERATIONOUTCOME" => ReturnPreference.OperationOutcome,
            _ => ReturnPreference.Unspecified
        };

        if (result == ReturnPreference.Unspecified)
        {
            logger?.LogWarning("Unknown Prefer header return value: {Value}", value);
        }

        return result;
    }
}
