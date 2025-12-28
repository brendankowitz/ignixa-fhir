// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Validation.Abstractions;
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
    [EnumLiteral("representation")]
    Representation = 1,

    /// <summary>
    /// Server should return minimal representation (headers only, no body).
    /// </summary>
    [EnumLiteral("minimal")]
    Minimal = 2,

    /// <summary>
    /// Server should return OperationOutcome in response body.
    /// </summary>
    [EnumLiteral("OperationOutcome")]
    OperationOutcome = 3
}

/// <summary>
/// Parses and validates the Prefer header for FHIR validation level preferences and return preferences.
/// Supports:
///   - Prefer: validation=minimal|spec|full (FHIR profile validation)
///   - Prefer: return=representation|minimal|OperationOutcome (RFC 7240)
/// Maps validation to internal ValidationDepth: Minimal|Spec|Full
/// </summary>
public static class PreferHeaderParser
{
    /// <summary>
    /// Parses the Prefer header to extract validation level preference.
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    /// <returns>Parsed ValidationDepth, or null if header not present or invalid.</returns>
    /// <remarks>
    /// Supported format: Prefer: validation=minimal|spec|full
    /// Examples:
    ///   - Prefer: validation=minimal → ValidationDepth.Minimal
    ///   - Prefer: validation=spec → ValidationDepth.Spec
    ///   - Prefer: validation=full → ValidationDepth.Full
    /// Also supports multiple preferences: Prefer: return=representation, validation=spec
    /// Backward compatibility: "none"/"minimum" map to Minimal
    /// </remarks>
    public static ValidationDepth? TryParseValidationLevel(IHeaderDictionary headers, ILogger? logger = null)
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
    /// Parses the Prefer header with strict validation, throwing on invalid values.
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    /// <returns>
    /// A tuple containing:
    /// - ReturnPreference: The parsed preference (or Unspecified if not present)
    /// - bool: True if parsing succeeded (or header not present), false if header was malformed
    /// - string?: Error message if parsing failed
    /// </returns>
    /// <remarks>
    /// Use this method when invalid Prefer headers should result in a 400 Bad Request.
    /// Returns false for:
    /// - Empty return value (e.g., "return=")
    /// - Unknown return value (e.g., "return=unknown")
    /// </remarks>
    public static (ReturnPreference Preference, bool IsValid, string? ErrorMessage) ParseReturnPreferenceStrict(
        IHeaderDictionary headers,
        ILogger? logger = null)
    {
        if (!headers.TryGetValue("Prefer", out var preferHeader))
        {
            return (ReturnPreference.Unspecified, true, null);
        }

        var preferValue = preferHeader.ToString();
        if (string.IsNullOrWhiteSpace(preferValue))
        {
            return (ReturnPreference.Unspecified, true, null);
        }

        // Parse comma-separated preferences
        foreach (var preference in preferValue.Split(','))
        {
            var trimmedPref = preference.Trim();
            if (trimmedPref.StartsWith("return=", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmedPref.Substring("return=".Length).Trim();

                // Empty value is invalid
                if (string.IsNullOrWhiteSpace(value))
                {
                    logger?.LogWarning("Prefer header has empty return value");
                    return (ReturnPreference.Unspecified, false, "Prefer header 'return' has empty value");
                }

                var result = EnumUtility.ParseLiteral<ReturnPreference>(value);

                if (result is null)
                {
                    logger?.LogWarning("Unknown Prefer header return value: {Value}", value);
                    return (ReturnPreference.Unspecified, false, $"Prefer header has invalid return value: '{value}'");
                }

                return (result.Value, true, null);
            }
        }

        return (ReturnPreference.Unspecified, true, null);
    }

    /// <summary>
    /// Converts the validation level to a response header value.
    /// </summary>
    /// <param name="depth">The validation depth that was applied.</param>
    /// <returns>Header value for Preference-Applied (e.g., "validation=spec").</returns>
    public static string ToPreferenceAppliedHeader(ValidationDepth depth)
    {
        return depth switch
        {
            ValidationDepth.Minimal => "validation=minimal",
            ValidationDepth.Spec => "validation=spec",
            ValidationDepth.Full => "validation=full",
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
    /// Parses the Prefer header to extract handling preference (strict vs lenient).
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <returns>True if handling=strict, false if handling=lenient or not specified.</returns>
    /// <remarks>
    /// Supported format: Prefer: handling=strict|lenient
    /// Examples:
    ///   - Prefer: handling=strict → true
    ///   - Prefer: handling=lenient → false
    ///   - No Prefer header → false (default to lenient)
    /// Also supports multiple preferences: Prefer: handling=strict, return=representation
    /// </remarks>
    public static bool IsStrictHandling(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Prefer", out var preferHeader))
        {
            return false;
        }

        var preferValue = preferHeader.ToString();
        if (string.IsNullOrWhiteSpace(preferValue))
        {
            return false;
        }

        foreach (var preference in preferValue.Split(','))
        {
            var trimmedPref = preference.Trim();
            if (trimmedPref.StartsWith("handling=", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmedPref.Substring("handling=".Length).Trim();
                return value.Equals("strict", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a single validation level value.
    /// </summary>
    private static ValidationDepth? ParseValidationLevelValue(string value, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            logger?.LogWarning("Prefer header validation value is empty");
            return null;
        }

        var result = value.ToUpperInvariant() switch
        {
            "MINIMAL" => ValidationDepth.Minimal,
            "SPEC" => ValidationDepth.Spec,
            "FULL" => ValidationDepth.Full,
            // Backward compatibility
            "NONE" => ValidationDepth.Minimal,
            "MINIMUM" => ValidationDepth.Minimal,
            _ => (ValidationDepth?)null
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

        var result = EnumUtility.ParseLiteral<ReturnPreference>(value);

        if (result is null)
        {
            logger?.LogWarning("Unknown Prefer header return value: {Value}", value);
            return ReturnPreference.Unspecified;
        }

        return result.Value;
    }
}
