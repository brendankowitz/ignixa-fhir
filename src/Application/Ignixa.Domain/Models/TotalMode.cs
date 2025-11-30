// <copyright file="TotalMode.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Domain.Models;

/// <summary>
/// Defines how the server should handle the total count in Bundle responses.
/// Maps to FHIR R4 _total search parameter values.
/// </summary>
public enum TotalMode
{
    /// <summary>
    /// Do not calculate or include total count in the Bundle.
    /// This is the default and most performant option as it avoids expensive count queries.
    /// Bundles will not include a "total" property.
    /// </summary>
    None,

    /// <summary>
    /// Include an estimated total count if it can be computed efficiently.
    /// If an estimate is not available cheaply, behaves like None.
    /// Not yet implemented - behaves like None.
    /// </summary>
    Estimate,

    /// <summary>
    /// Calculate and include the exact total count across all pages.
    /// This requires a separate query to count all matching results,
    /// which can be expensive for large result sets.
    /// Use sparingly and only when clients genuinely need the total.
    /// </summary>
    Accurate,
}
