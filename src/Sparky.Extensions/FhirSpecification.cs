// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Sparky.Extensions;

public enum FhirSpecification
{
    Stu3,
    R4,
    R4B,
    R5
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
            _ => "4.0" // Default to R4
        };
    }
}
