// <copyright file="ReferenceType.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Serialization.Models;

/// <summary>
/// Type of FHIR reference.
/// </summary>
public enum ReferenceType
{
    /// <summary>
    /// Relative reference (e.g., "Patient/123").
    /// </summary>
    Relative,

    /// <summary>
    /// Absolute URL reference (e.g., "https://example.org/fhir/Patient/123").
    /// </summary>
    Absolute,

    /// <summary>
    /// Logical identifier (e.g., "urn:uuid:...").
    /// </summary>
    Logical,
}
