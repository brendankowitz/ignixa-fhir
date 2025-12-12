// <copyright file="IValueSetProvider.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Abstractions;

/// <summary>
/// Provides access to ValueSet code definitions for a specific FHIR version.
/// </summary>
public interface IValueSetProvider
{
    /// <summary>
    /// Gets the codes for a ValueSet by its canonical URL.
    /// </summary>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet (e.g., "http://hl7.org/fhir/ValueSet/administrative-gender").</param>
    /// <returns>A list of codes in the ValueSet, or null if the ValueSet is not known.</returns>
    IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl);

    /// <summary>
    /// Checks if a ValueSet is known by this provider.
    /// </summary>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet.</param>
    /// <returns>True if the ValueSet is known; otherwise, false.</returns>
    bool IsKnownValueSet(string valueSetUrl);

    /// <summary>
    /// Validates whether a code is valid for a given ValueSet.
    /// </summary>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet.</param>
    /// <param name="code">The code to validate.</param>
    /// <returns>True if the code is valid for the ValueSet; false otherwise; null if ValueSet is unknown.</returns>
    bool? IsValidCode(string valueSetUrl, string code);
}
