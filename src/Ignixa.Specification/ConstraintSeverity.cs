// <copyright file="ConstraintSeverity.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Specification;

/// <summary>
/// Constraint severity levels for FHIR invariants.
/// </summary>
public enum ConstraintSeverity
{
    /// <summary>
    /// Error-level constraint that MUST be satisfied.
    /// </summary>
    Error,

    /// <summary>
    /// Warning-level constraint that SHOULD be satisfied.
    /// </summary>
    Warning
}
