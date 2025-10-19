// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Utility for extracting FHIR version from HTTP headers.
/// Checks Content-Type and Accept headers for fhirVersion parameter.
/// </summary>
public static class FhirVersionExtractor
{
    /// <summary>
    /// Default FHIR version when not specified in headers.
    /// </summary>
    public const FhirSpecification DefaultFhirVersion = FhirSpecification.R4;

    /// <summary>
    /// Extracts FHIR version from Content-Type or Accept headers.
    /// Returns DefaultFhirVersion (R4) if not specified.
    /// </summary>
    /// <param name="context">The HTTP context containing the request headers.</param>
    /// <returns>FHIR version enum (e.g., FhirSpecification.R4).</returns>
    public static FhirSpecification ExtractFhirVersion(HttpContext? context)
    {
        if (context == null)
        {
            return DefaultFhirVersion;
        }

        // Check Content-Type header first (for PUT/POST requests)
        if (context.Request.Headers.TryGetValue("Content-Type", out StringValues contentType))
        {
            var version = ParseFhirVersionFromMediaType(contentType.ToString());
            if (version.HasValue)
            {
                return version.Value;
            }
        }

        // Check Accept header (for GET requests and fallback)
        if (context.Request.Headers.TryGetValue("Accept", out StringValues accept))
        {
            var version = ParseFhirVersionFromMediaType(accept.ToString());
            if (version.HasValue)
            {
                return version.Value;
            }
        }

        // Default to R4 if not specified
        return DefaultFhirVersion;
    }

    /// <summary>
    /// Parses fhirVersion parameter from media type string and converts to enum.
    /// Example: "application/fhir+json; fhirVersion=5.0" → FhirSpecification.R5
    /// </summary>
    /// <param name="mediaType">Media type string with optional parameters.</param>
    /// <returns>FHIR version enum if found and valid, null otherwise.</returns>
    private static FhirSpecification? ParseFhirVersionFromMediaType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return null;
        }

        // Split by semicolon to get parameters
        var parts = mediaType.Split(';');
        if (parts.Length < 2)
        {
            return null; // No parameters
        }

        // Look for fhirVersion parameter
        foreach (var part in parts.Skip(1))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("fhirVersion=", StringComparison.OrdinalIgnoreCase))
            {
                var versionString = trimmed.Substring("fhirVersion=".Length).Trim();
                // Remove quotes if present
                versionString = versionString.Trim('"', '\'');

                // Parse string to enum
                return FhirSpecificationExtensions.FromVersionString(versionString);
            }
        }

        return null;
    }
}
