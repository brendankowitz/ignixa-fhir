// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Ignixa.Extensions.ProfileBehaviors.Infrastructure;

/// <summary>
/// Utility for extracting FHIR version from HTTP headers with tenant configuration fallback.
/// Checks Content-Type and Accept headers for fhirVersion parameter, then falls back to tenant default.
/// </summary>
/// <remarks>
/// Duplicated from Ignixa.Application.Infrastructure.FhirVersionExtractor to avoid circular dependency.
/// Enhanced to respect tenant default FHIR version configuration.
/// </remarks>
internal static class FhirVersionExtractor
{
    /// <summary>
    /// Fallback FHIR version when tenant configuration is unavailable.
    /// </summary>
    public const FhirSpecification FallbackFhirVersion = FhirSpecification.R4;

    /// <summary>
    /// Extracts FHIR version from Content-Type or Accept headers.
    /// Falls back to tenant's configured FHIR version, then to R4 if tenant config unavailable.
    /// </summary>
    /// <param name="context">The HTTP context containing the request headers.</param>
    /// <param name="tenantConfigStore">Tenant configuration store for retrieving default FHIR version.</param>
    /// <param name="tenantId">The tenant ID to look up configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>FHIR version enum (e.g., FhirSpecification.R4).</returns>
    public static async Task<FhirSpecification> ExtractFhirVersionAsync(
        HttpContext? context,
        ITenantConfigurationStore tenantConfigStore,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // 1. Check Content-Type header first (for PUT/POST requests)
        if (context?.Request.Headers.TryGetValue("Content-Type", out StringValues contentType) == true)
        {
            var version = ParseFhirVersionFromMediaType(contentType.ToString());
            if (version.HasValue)
            {
                return version.Value;
            }
        }

        // 2. Check Accept header (for GET requests and fallback)
        if (context?.Request.Headers.TryGetValue("Accept", out StringValues accept) == true)
        {
            var version = ParseFhirVersionFromMediaType(accept.ToString());
            if (version.HasValue)
            {
                return version.Value;
            }
        }

        // 3. Fall back to tenant's configured default FHIR version
        var tenantConfig = await tenantConfigStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenantConfig != null && !string.IsNullOrEmpty(tenantConfig.FhirVersion))
        {
            var tenantVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            if (tenantVersion != FhirSpecification.Unknown)
            {
                return tenantVersion;
            }
        }

        // 4. Final fallback to R4
        return FallbackFhirVersion;
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
