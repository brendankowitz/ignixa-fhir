// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Serialization;

/// <summary>
/// Extension methods for FhirVersion enum.
/// </summary>
public static class FhirSpecificationExtensions
{
    /// <summary>
    /// Converts FhirVersion enum to version string.
    /// </summary>
    /// <param name="spec">The FHIR specification enum value.</param>
    /// <returns>Version string (e.g., "4.0", "5.0", "3.0").</returns>
    public static string ToVersionString(this FhirVersion spec)
    {
        return spec switch
        {
            FhirVersion.Stu3 => "3.0",
            FhirVersion.R4 => "4.0",
            FhirVersion.R4B => "4.3",
            FhirVersion.R5 => "5.0",
            FhirVersion.R6 => "6.0",
            _ => "Unspecified"
        };
    }

    /// <summary>
    /// Converts version string to FhirVersion enum.
    /// Supports both major.minor (e.g., "4.0") and major.minor.patch (e.g., "4.0.1") formats.
    /// </summary>
    /// <param name="versionString">Version string (e.g., "4.0", "4.0.1", "5.0", "3.0.2").</param>
    /// <returns>FhirVersion enum value. Defaults to R6 (latest) for unknown versions.</returns>
    public static FhirVersion FromVersionString(string versionString)
    {
        if (string.IsNullOrEmpty(versionString))
        {
            return FhirVersion.R6; // Default to latest (R6)
        }

        // Extract major.minor by taking first 3 characters or until second dot
        // "3.0" -> "3.0", "3.0.2" -> "3.0", "4.0.1" -> "4.0", "4.3.0" -> "4.3"
        var majorMinor = versionString.Length >= 3 ? versionString.Substring(0, 3) : versionString;

        return majorMinor switch
        {
            "3.0" => FhirVersion.Stu3,
            "4.0" => FhirVersion.R4,
            "4.3" => FhirVersion.R4B,
            "5.0" => FhirVersion.R5,
            "6.0" => FhirVersion.R6,
            _ => FhirVersion.Unspecified
        };
    }
}
