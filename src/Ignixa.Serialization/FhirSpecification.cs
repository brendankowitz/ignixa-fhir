// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Serialization;

public enum FhirSpecification
{
    Stu3,
    R4,
    R4B,
    R5,
    R6
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
            _ => "4.0" // Default to R4
        };
    }

    /// <summary>
    /// Converts version string to FhirSpecification enum.
    /// Supports both major.minor (e.g., "4.0") and major.minor.patch (e.g., "4.0.1") formats.
    /// </summary>
    /// <param name="versionString">Version string (e.g., "4.0", "4.0.1", "5.0", "3.0.2").</param>
    /// <returns>FhirSpecification enum value. Defaults to R4 for unknown versions.</returns>
    public static FhirSpecification FromVersionString(string versionString)
    {
        if (string.IsNullOrEmpty(versionString))
        {
            return FhirSpecification.R4; // Default to R4
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
            _ => FhirSpecification.R4 // Default to R4
        };
    }
}
