// <copyright file="BindingStrength.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// FHIR ElementDefinition.binding.strength values.
/// Determines how strictly a coded element must conform to a ValueSet.
/// See https://hl7.org/fhir/R4/valueset-binding-strength.html
/// </summary>
public enum BindingStrength
{
    /// <summary>
    /// Required: Code MUST be from the ValueSet. Validation ERROR if not found.
    /// </summary>
    Required = 0,

    /// <summary>
    /// Extensible: Code SHOULD be from the ValueSet. Validation WARNING if not found (mode=full only).
    /// Custom codes are allowed but discouraged.
    /// </summary>
    Extensible = 1,

    /// <summary>
    /// Preferred: Code MAY be from the ValueSet. Informational only.
    /// ValueSet is recommended but not enforced.
    /// </summary>
    Preferred = 2,

    /// <summary>
    /// Example: ValueSet is provided as an example. No validation.
    /// </summary>
    Example = 3
}
