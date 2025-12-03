// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Serialization;

/// <summary>
/// FHIR specification version.
/// </summary>
public enum FhirSpecification
{
    Stu3 = 0,
    R4 = 1,
    R4B = 2,
    R5 = 3,
    R6 = 4,

    /// <summary>
    /// Unspecified version - defaults to latest (R6) for version comparisons.
    /// Use this when you want forward-compatible behavior that assumes the latest FHIR version.
    /// </summary>
    Unspecified = 999
}

/// <summary>
/// Extension methods for FhirSpecification enum.
/// </summary>
public static class FhirSpecificationExtensions
{
    /// <summary>
    /// Converts FhirSpecification enum to version string.
    /// </summary>
    /// <param name="spec">The FHIR specification enum value.</param>
    /// <returns>Version string (e.g., "4.0", "5.0", "3.0").</returns>
    public static string ToVersionString(this FhirSpecification spec)
    {
        return spec switch
        {
            FhirSpecification.Stu3 => "3.0",
            FhirSpecification.R4 => "4.0",
            FhirSpecification.R4B => "4.3",
            FhirSpecification.R5 => "5.0",
            FhirSpecification.R6 => "6.0",
            FhirSpecification.Unspecified => "6.0", // Unspecified defaults to latest (R6)
            _ => "6.0" // Unknown values default to latest
        };
    }

    /// <summary>
    /// Converts version string to FhirSpecification enum.
    /// Supports both major.minor (e.g., "4.0") and major.minor.patch (e.g., "4.0.1") formats.
    /// </summary>
    /// <param name="versionString">Version string (e.g., "4.0", "4.0.1", "5.0", "3.0.2").</param>
    /// <returns>FhirSpecification enum value. Defaults to R6 (latest) for unknown versions.</returns>
    public static FhirSpecification FromVersionString(string versionString)
    {
        if (string.IsNullOrEmpty(versionString))
        {
            return FhirSpecification.R6; // Default to latest (R6)
        }

        // Extract major.minor by taking first 3 characters or until second dot
        // "3.0" -> "3.0", "3.0.2" -> "3.0", "4.0.1" -> "4.0", "4.3.0" -> "4.3"
        var majorMinor = versionString.Length >= 3 ? versionString.Substring(0, 3) : versionString;

        return majorMinor switch
        {
            "3.0" => FhirSpecification.Stu3,
            "4.0" => FhirSpecification.R4,
            "4.3" => FhirSpecification.R4B,
            "5.0" => FhirSpecification.R5,
            "6.0" => FhirSpecification.R6,
            _ => FhirSpecification.R6 // Default to latest (R6)
        };
    }
}
